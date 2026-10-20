using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Independent capability for private capture restoration, never arbitrary archive paths.</summary>
public sealed class FileTreeRestoringGrpcService(FileTransferRunner runner) : FileTreeRestoringService.FileTreeRestoringServiceBase
{
    public override async Task<FileTreeRestoringPlan> Plan(FileTreeRestoringPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeRestoringPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-restore-plan", input, context.CancellationToken);
        return FileTreeRestoringPolicy.Plan(response.RootElement, request.Command);
    }

    public override Task<FileTreeRestoringReceipt> Apply(FileTreeRestoringApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeRestoringReceipt> Discard(FileTreeRestoringApplyRequest request, ServerCallContext context) => Write(request, context, true);

    private async Task<FileTreeRestoringReceipt> Write(FileTreeRestoringApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreeRestoringPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-restore-discard" : "tree-restore-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeRestoringPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }

    public override async Task<FileTreeRestoringReceipt> Inspect(FileTreeRestoringLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-restore-status", input, context.CancellationToken);
        return FileTreeRestoringPolicy.Receipt(response.RootElement, request.OperationId);
    }

    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_TREE_RESTORE_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeRestoringPolicy
{
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> CommandFields = ["source", "target", "format", "captureId", "captureOwner", "captureKind", "manifestSha256"];
    private static readonly HashSet<string> PlanFields = ["command", "archiveSha256", "archiveBytes", "contentSha256", "entryCount", "totalBytes", "targetParent", "sourcePolicyHash", "targetPolicyHash", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "format", "entryCount", "totalBytes", "archiveBytes", "archiveSha256", "version", "partialRetained", "kind", "captureId"];

    public static string CommandJson(FileTreeRestoringCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_RESTORE_INVALID");
        FileTransferPolicy.Location(command.Source); FileTransferPolicy.Location(command.Target);
        FileTransferPolicy.Hash(command.ExpectedVersion); FileTransferPolicy.Identity(command.CaptureId);
        FileTransferPolicy.Hash(command.CaptureOwner); FileTransferPolicy.Hash(command.ManifestSha256);
        if (command.CaptureKind is not ("Trash" or "Overwrite")) throw new ArgumentException("FILES_TREE_RESTORE_INVALID");
        // Unlike a live directory copy, the original source path may no longer
        // exist. Restoring that exact path is valid; the helper enforces NO_REPLACE.
        return JsonSerializer.Serialize(new {
            source = new FileTreeCopyPolicy.RootSource(command.Source.RootId, command.Source.Path, command.ExpectedVersion),
            target = new FileTreeCopyPolicy.RootLocation(command.Target.RootId, command.Target.Path), format = "tar.gz",
            captureId = command.CaptureId, captureOwner = command.CaptureOwner,
            captureKind = command.CaptureKind, manifestSha256 = command.ManifestSha256 }, InputOptions);
    }

    public static JsonElement PlanInput(FileTreeRestoringPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_RESTORE_PLAN_REQUIRED");
        var command = JsonSerializer.Deserialize<JsonElement>(CommandJson(plan.Command));
        FileTransferPolicy.Hash(plan.ArchiveSha256); FileTransferPolicy.Hash(plan.ContentSha256);
        FileTransferPolicy.Hash(plan.SourcePolicyHash); FileTransferPolicy.Hash(plan.TargetPolicyHash); FileTransferPolicy.Hash(plan.Fingerprint);
        if (plan.ArchiveBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes || plan.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            plan.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes || plan.TargetParent is not { Inode: > 0 })
            throw new ArgumentException("FILES_TREE_RESTORE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new { command, archiveSha256 = plan.ArchiveSha256, archiveBytes = plan.ArchiveBytes,
            contentSha256 = plan.ContentSha256, entryCount = plan.EntryCount, totalBytes = plan.TotalBytes,
            targetParent = new[] { plan.TargetParent.Device, plan.TargetParent.Inode }, sourcePolicyHash = plan.SourcePolicyHash,
            targetPolicyHash = plan.TargetPolicyHash, fingerprint = plan.Fingerprint });
    }

    public static FileTreeRestoringPlan Plan(JsonElement value, FileTreeRestoringCommand expected)
    {
        Fields(value, PlanFields); _ = CommandJson(expected);
        var command = value.GetProperty("command"); Fields(command, CommandFields);
        var source = command.GetProperty("source"); var target = command.GetProperty("target");
        Fields(source, ["rootId", "path", "expectedVersion"]); Fields(target, ["rootId", "path"]);
        if (Text(command, "format") != "tar.gz") throw new InvalidOperationException("FILES_TREE_RESTORE_INVALID");
        var parent = value.GetProperty("targetParent");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PARENT_INVALID");
        var result = new FileTreeRestoringPlan {
            Command = new() { Source = new() { RootId = Text(source, "rootId"), Path = Text(source, "path") }, ExpectedVersion = Text(source, "expectedVersion"),
                Target = new() { RootId = Text(target, "rootId"), Path = Text(target, "path") }, CaptureId = Text(command, "captureId"),
                CaptureOwner = Text(command, "captureOwner"), CaptureKind = Text(command, "captureKind"), ManifestSha256 = Text(command, "manifestSha256") },
            ArchiveSha256 = Text(value, "archiveSha256"), ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(), ContentSha256 = Text(value, "contentSha256"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            TargetParent = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            SourcePolicyHash = Text(value, "sourcePolicyHash"), TargetPolicyHash = Text(value, "targetPolicyHash"), Fingerprint = Text(value, "fingerprint") };
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }

    public static FileTreeRestoringReceipt Receipt(JsonElement value, string operation, FileTreeRestoringPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var captureId = Text(value, "captureId"); FileTransferPolicy.Identity(captureId);
        if (Text(value, "kind") != "DirectoryRestore" || Text(value, "format") != "tar.gz")
            throw new InvalidOperationException("FILES_TREE_RESTORE_RECEIPT_INVALID");
        var node = JsonNode.Parse(value.GetRawText())!.AsObject(); node.Remove("captureId"); node.Remove("kind");
        var receipt = FileTreeUnpackingPolicy.Receipt(JsonSerializer.SerializeToElement(node), operation);
        if (plan is not null && (captureId != plan.Command.CaptureId || receipt.Fingerprint != plan.Fingerprint || receipt.EntryCount != plan.EntryCount ||
            receipt.TotalBytes != plan.TotalBytes || receipt.ArchiveBytes != plan.ArchiveBytes || receipt.ArchiveSha256 != plan.ArchiveSha256))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return new() { OperationId = receipt.OperationId, State = receipt.State, Fingerprint = receipt.Fingerprint, Format = receipt.Format,
            EntryCount = receipt.EntryCount, TotalBytes = receipt.TotalBytes, ArchiveBytes = receipt.ArchiveBytes, ArchiveSha256 = receipt.ArchiveSha256,
            Version = receipt.Version, PartialRetained = receipt.PartialRetained, Kind = "DirectoryRestore", CaptureId = captureId };
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
