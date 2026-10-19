using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Distinct compression/receipt-bound archive download method capabilities.</summary>
public sealed class FileTreePackingGrpcService(FileTransferRunner runner) : FileTreePackingService.FileTreePackingServiceBase
{
    public override async Task<FileTreePackingPlan> Plan(FileTreePackingPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreePackingPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-pack-plan", input, context.CancellationToken);
        return FileTreePackingPolicy.Plan(response.RootElement, request.Command);
    }
    public override Task<FileTreePackingReceipt> Apply(FileTreePackingApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreePackingReceipt> Discard(FileTreePackingApplyRequest request, ServerCallContext context) => Write(request, context, true);
    private async Task<FileTreePackingReceipt> Write(FileTreePackingApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreePackingPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-pack-discard" : "tree-pack-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreePackingPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileTreePackingReceipt> Inspect(FileTreePackingLookup request, ServerCallContext context)
    {
        var input = Input(() => Lookup(request.OperationId, request.Owner));
        using var response = await runner.OnceAsync("tree-pack-status", input, context.CancellationToken);
        return FileTreePackingPolicy.Receipt(response.RootElement, request.OperationId);
    }
    public override Task Download(FileTreePackingDownloadRequest request, IServerStreamWriter<FileDownloadFrame> responseStream, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return Lookup(request.OperationId, request.Owner); });
        return runner.TreePackingDownloadAsync(input, responseStream, context.CancellationToken);
    }
    private static string Lookup(string operation, string owner)
    {
        FileTransferPolicy.Identity(operation); FileTransferPolicy.Hash(owner);
        return JsonSerializer.Serialize(new { operationId = operation, owner });
    }
    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_PACK_INPUT_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreePackingPolicy
{
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "format", "entryCount", "totalBytes", "archiveBytes", "archiveSha256", "version", "partialRetained"];
    private static void Format(string value)
    {
        if (value is not ("tar" or "tar.gz")) throw new ArgumentException("FILES_ARCHIVE_FORMAT_INVALID");
    }
    public static string CommandJson(FileTreePackingCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_PACK_COMMAND_REQUIRED");
        Format(command.Format);
        var input = JsonNode.Parse(FileTreeCopyPolicy.CommandJson(command.Tree))!.AsObject();
        if (!command.Tree.Target.Path.EndsWith("." + command.Format, StringComparison.Ordinal)) throw new ArgumentException("FILES_ARCHIVE_FORMAT_INVALID");
        input.Add("format", command.Format); return input.ToJsonString();
    }
    private static FileTreeCopyPlan CopyFields(FileTreePackingPlan plan) => new()
    {
        Command = plan.Command.Tree, ManifestSha256 = plan.ManifestSha256, EntryCount = plan.EntryCount, TotalBytes = plan.TotalBytes,
        TargetParent = plan.TargetParent, SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint
    };
    public static JsonElement PlanInput(FileTreePackingPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_PACK_PLAN_REQUIRED");
        var command = JsonNode.Parse(CommandJson(plan.Command));
        var value = JsonNode.Parse(FileTreeCopyPolicy.PlanInput(CopyFields(plan)).GetRawText())!;
        value["command"] = command;
        return JsonSerializer.SerializeToElement(value);
    }
    public static FileTreePackingPlan Plan(JsonElement value, FileTreePackingCommand expected)
    {
        _ = CommandJson(expected);
        var node = JsonNode.Parse(value.GetRawText())!;
        if (node["command"] is not JsonObject rootCommand || rootCommand.Count != 3 || !rootCommand.ContainsKey("source") || !rootCommand.ContainsKey("target") || !rootCommand.ContainsKey("format"))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var format = rootCommand["format"]!.GetValue<string>(); Format(format);
        if (format != expected.Format) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        rootCommand.Remove("format");
        var copy = FileTreeCopyPolicy.Plan(JsonSerializer.SerializeToElement(node), expected.Tree);
        var result = new FileTreePackingPlan { Command = expected.Clone(), ManifestSha256 = copy.ManifestSha256, EntryCount = copy.EntryCount, TotalBytes = copy.TotalBytes,
            TargetParent = copy.TargetParent, SourcePolicyHash = copy.SourcePolicyHash, TargetPolicyHash = copy.TargetPolicyHash, Fingerprint = copy.Fingerprint };
        _ = PlanInput(result); return result;
    }
    public static FileTreePackingReceipt Receipt(JsonElement value, string operation, FileTreePackingPlan? plan = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != ReceiptFields.Count || names.Distinct(StringComparer.Ordinal).Count() != ReceiptFields.Count || !names.All(ReceiptFields.Contains))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        string Text(string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var result = new FileTreePackingReceipt { OperationId = Text("operationId"), State = Text("state"), Fingerprint = Text("fingerprint"), Format = Text("format"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(), ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(),
            ArchiveSha256 = Text("archiveSha256"), Version = Text("version"), PartialRetained = value.GetProperty("partialRetained").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Hash(result.Fingerprint); Format(result.Format);
        var completed = result.State == "Completed";
        if (result.OperationId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") || result.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            result.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes || completed && (result.PartialRetained || result.ArchiveBytes is < 1 or > MaximumArchiveBytes) ||
            !completed && (result.Version.Length != 0 || result.ArchiveSha256.Length != 0 || result.ArchiveBytes != 0))
            throw new InvalidOperationException("FILES_PACK_RECEIPT_INVALID");
        if (completed) { FileTransferPolicy.Hash(result.Version); FileTransferPolicy.Hash(result.ArchiveSha256); }
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.EntryCount != plan.EntryCount || result.TotalBytes != plan.TotalBytes || result.Format != plan.Command.Format))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }
}
