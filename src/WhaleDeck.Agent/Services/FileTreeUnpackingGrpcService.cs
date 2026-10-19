using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Independent registered-archive unpacking and observation capabilities.</summary>
public sealed class FileTreeUnpackingGrpcService(FileTransferRunner runner) : FileTreeUnpackingService.FileTreeUnpackingServiceBase
{
    public override async Task<FileTreeUnpackingPlan> Plan(FileTreeUnpackingPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeUnpackingPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-unpack-plan", input, context.CancellationToken);
        return FileTreeUnpackingPolicy.Plan(response.RootElement, request.Command);
    }
    public override Task<FileTreeUnpackingReceipt> Apply(FileTreeUnpackingApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeUnpackingReceipt> Discard(FileTreeUnpackingApplyRequest request, ServerCallContext context) => Write(request, context, true);
    private async Task<FileTreeUnpackingReceipt> Write(FileTreeUnpackingApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreeUnpackingPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-unpack-discard" : "tree-unpack-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeUnpackingPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileTreeUnpackingReceipt> Inspect(FileTreeUnpackingLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-unpack-status", input, context.CancellationToken);
        return FileTreeUnpackingPolicy.Receipt(response.RootElement, request.OperationId);
    }
    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_UNPACK_INPUT_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeUnpackingPolicy
{
    public const long MaximumArchiveBytes = 17L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> PlanFields = ["command", "archiveSha256", "archiveBytes", "contentSha256", "entryCount", "totalBytes", "targetParent", "sourcePolicyHash", "targetPolicyHash", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "format", "entryCount", "totalBytes", "archiveBytes", "archiveSha256", "version", "partialRetained"];
    private static void Format(string value)
    {
        if (value is not ("tar" or "tar.gz")) throw new ArgumentException("FILES_ARCHIVE_FORMAT_INVALID");
    }
    public static string CommandJson(FileTreeUnpackingCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_UNPACK_COMMAND_REQUIRED");
        Format(command.Format);
        var value = JsonNode.Parse(FileTreeCopyPolicy.CommandJson(command.Tree))!.AsObject();
        value.Add("format", command.Format); return value.ToJsonString();
    }
    private static FileTreeCopyPlan CopyFields(FileTreeUnpackingPlan plan) => new()
    {
        Command = plan.Command.Tree, ManifestSha256 = plan.ContentSha256, EntryCount = plan.EntryCount, TotalBytes = plan.TotalBytes,
        TargetParent = plan.TargetParent, SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint
    };
    public static JsonElement PlanInput(FileTreeUnpackingPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_UNPACK_PLAN_REQUIRED");
        var command = JsonNode.Parse(CommandJson(plan.Command));
        FileTransferPolicy.Hash(plan.ArchiveSha256);
        if (plan.ArchiveBytes is < 1 or > MaximumArchiveBytes) throw new ArgumentException("FILES_UNPACK_PLAN_INVALID");
        var value = JsonNode.Parse(FileTreeCopyPolicy.PlanInput(CopyFields(plan)).GetRawText())!.AsObject();
        value.Remove("manifestSha256"); value["contentSha256"] = plan.ContentSha256;
        value["archiveSha256"] = plan.ArchiveSha256; value["archiveBytes"] = plan.ArchiveBytes; value["command"] = command;
        return JsonSerializer.SerializeToElement(value);
    }
    public static FileTreeUnpackingPlan Plan(JsonElement value, FileTreeUnpackingCommand expected)
    {
        Fields(value, PlanFields); _ = CommandJson(expected);
        var node = JsonNode.Parse(value.GetRawText())!.AsObject();
        var rootCommand = value.GetProperty("command"); Fields(rootCommand, ["source", "target", "format"]);
        var format = Text(rootCommand, "format"); Format(format);
        if (format != expected.Format) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        var content = Text(value, "contentSha256"); var checksum = Text(value, "archiveSha256"); var size = value.GetProperty("archiveBytes").GetInt64();
        node["command"]!.AsObject().Remove("format");
        node.Remove("archiveBytes"); node.Remove("archiveSha256"); node.Remove("contentSha256"); node["manifestSha256"] = content;
        var copy = FileTreeCopyPolicy.Plan(JsonSerializer.SerializeToElement(node), expected.Tree);
        var result = new FileTreeUnpackingPlan { Command = expected.Clone(), ContentSha256 = content, ArchiveSha256 = checksum, ArchiveBytes = size,
            EntryCount = copy.EntryCount, TotalBytes = copy.TotalBytes, TargetParent = copy.TargetParent,
            SourcePolicyHash = copy.SourcePolicyHash, TargetPolicyHash = copy.TargetPolicyHash, Fingerprint = copy.Fingerprint };
        _ = PlanInput(result); return result;
    }
    public static FileTreeUnpackingReceipt Receipt(JsonElement value, string operation, FileTreeUnpackingPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var result = new FileTreeUnpackingReceipt { OperationId = Text(value, "operationId"), State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"),
            Format = Text(value, "format"), EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(), ArchiveSha256 = Text(value, "archiveSha256"), Version = Text(value, "version"), PartialRetained = value.GetProperty("partialRetained").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Hash(result.Fingerprint); FileTransferPolicy.Hash(result.ArchiveSha256); Format(result.Format);
        var completed = result.State == "Completed";
        if (result.OperationId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") || result.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            result.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes || result.ArchiveBytes is < 1 or > MaximumArchiveBytes ||
            completed && result.PartialRetained || !completed && result.Version.Length != 0)
            throw new InvalidOperationException("FILES_UNPACK_RECEIPT_INVALID");
        if (completed) FileTransferPolicy.Hash(result.Version);
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.EntryCount != plan.EntryCount || result.TotalBytes != plan.TotalBytes ||
            result.Format != plan.Command.Format || result.ArchiveBytes != plan.ArchiveBytes || result.ArchiveSha256 != plan.ArchiveSha256))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
}
