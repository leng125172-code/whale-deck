using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Distinct capability for verified displaced-tree retention and atomic replacement.</summary>
public sealed class FileTreeOverwriteRestoringGrpcService(FileTransferRunner runner) : FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceBase
{
    public override async Task<FileTreeOverwriteRestoringPlan> Plan(FileTreeOverwriteRestoringPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeOverwriteRestoringPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-overwrite-restore-plan", input, context.CancellationToken);
        return FileTreeOverwriteRestoringPolicy.Plan(response.RootElement, request.Command);
    }

    public override Task<FileTreeOverwriteRestoringReceipt> Apply(FileTreeOverwriteRestoringApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeOverwriteRestoringReceipt> Discard(FileTreeOverwriteRestoringApplyRequest request, ServerCallContext context) => Write(request, context, true);

    private async Task<FileTreeOverwriteRestoringReceipt> Write(FileTreeOverwriteRestoringApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return FileTreeOverwriteRestoringPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-overwrite-restore-discard" : "tree-overwrite-restore-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeOverwriteRestoringPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }

    public override async Task<FileTreeOverwriteRestoringReceipt> Inspect(FileTreeOverwriteRestoringLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-overwrite-restore-status", input, context.CancellationToken);
        return FileTreeOverwriteRestoringPolicy.Receipt(response.RootElement, request.OperationId);
    }

    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_TREE_OVERWRITE_RESTORE_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeOverwriteRestoringPolicy
{
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> CommandFields = ["source", "target", "format", "captureId", "captureOwner", "captureKind", "manifestSha256"];
    private static readonly HashSet<string> PlanFields = ["command", "archiveSha256", "archiveBytes", "contentSha256", "entryCount", "totalBytes", "targetParent", "sourcePolicyHash", "targetPolicyHash", "fingerprint", "targetManifestSha256", "targetTreeSha256", "targetEntryCount", "targetTotalBytes", "targetIdentity", "targetContentSha256"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "format", "entryCount", "totalBytes", "archiveBytes", "archiveSha256", "version", "partialRetained", "kind", "captureId", "overwrittenCaptureId", "backupMayBeRetained", "backupVerified", "originalsRetained", "targetMayHaveChanged", "backupArchiveBytes", "backupArchiveSha256"];

    public static string CommandJson(FileTreeOverwriteRestoringCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_INVALID");
        FileTransferPolicy.Location(command.Source); FileTransferPolicy.Location(command.Target);
        FileTransferPolicy.Hash(command.ExpectedVersion); FileTransferPolicy.Hash(command.TargetExpectedVersion); FileTransferPolicy.Identity(command.CaptureId);
        FileTransferPolicy.Hash(command.CaptureOwner); FileTransferPolicy.Hash(command.ManifestSha256);
        if (command.CaptureKind is not ("Trash" or "Overwrite")) throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_INVALID");
        // Historical source and live target versions are distinct facts. No caller
        // controls HDD paths, ownership, member lists or the exchange primitive.
        return JsonSerializer.Serialize(new {
            source = new FileTreeCopyPolicy.RootSource(command.Source.RootId, command.Source.Path, command.ExpectedVersion),
            target = new FileTreeCopyPolicy.RootSource(command.Target.RootId, command.Target.Path, command.TargetExpectedVersion), format = "tar.gz",
            captureId = command.CaptureId, captureOwner = command.CaptureOwner,
            captureKind = command.CaptureKind, manifestSha256 = command.ManifestSha256 }, InputOptions);
    }

    public static JsonElement PlanInput(FileTreeOverwriteRestoringPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_PLAN_REQUIRED");
        var command = JsonSerializer.Deserialize<JsonElement>(CommandJson(plan.Command));
        FileTransferPolicy.Hash(plan.ArchiveSha256); FileTransferPolicy.Hash(plan.ContentSha256);
        FileTransferPolicy.Hash(plan.TargetManifestSha256); FileTransferPolicy.Hash(plan.TargetTreeSha256);
        FileTransferPolicy.Hash(plan.TargetContentSha256);
        FileTransferPolicy.Hash(plan.SourcePolicyHash); FileTransferPolicy.Hash(plan.TargetPolicyHash); FileTransferPolicy.Hash(plan.Fingerprint);
        if (plan.ArchiveBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes || plan.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            plan.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes || plan.TargetParent is not { Inode: > 0 } ||
            plan.TargetIdentity is not { Inode: > 0 } || plan.TargetEntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries ||
            plan.TargetTotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes)
            throw new ArgumentException("FILES_TREE_OVERWRITE_RESTORE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new { command, archiveSha256 = plan.ArchiveSha256, archiveBytes = plan.ArchiveBytes,
            contentSha256 = plan.ContentSha256, entryCount = plan.EntryCount, totalBytes = plan.TotalBytes,
            targetParent = new[] { plan.TargetParent.Device, plan.TargetParent.Inode }, sourcePolicyHash = plan.SourcePolicyHash,
            targetPolicyHash = plan.TargetPolicyHash, fingerprint = plan.Fingerprint,
            targetManifestSha256 = plan.TargetManifestSha256, targetTreeSha256 = plan.TargetTreeSha256, targetContentSha256 = plan.TargetContentSha256,
            targetEntryCount = plan.TargetEntryCount, targetTotalBytes = plan.TargetTotalBytes,
            targetIdentity = new[] { plan.TargetIdentity.Device, plan.TargetIdentity.Inode } });
    }

    public static FileTreeOverwriteRestoringPlan Plan(JsonElement value, FileTreeOverwriteRestoringCommand expected)
    {
        Fields(value, PlanFields); _ = CommandJson(expected);
        var command = value.GetProperty("command"); Fields(command, CommandFields);
        var source = command.GetProperty("source"); var target = command.GetProperty("target");
        Fields(source, ["rootId", "path", "expectedVersion"]); Fields(target, ["rootId", "path", "expectedVersion"]);
        if (Text(command, "format") != "tar.gz") throw new InvalidOperationException("FILES_TREE_OVERWRITE_RESTORE_INVALID");
        var parent = value.GetProperty("targetParent");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PARENT_INVALID");
        var targetIdentity = value.GetProperty("targetIdentity");
        if (targetIdentity.ValueKind != JsonValueKind.Array || targetIdentity.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_TARGET_INVALID");
        var result = new FileTreeOverwriteRestoringPlan {
            Command = new() { Source = new() { RootId = Text(source, "rootId"), Path = Text(source, "path") }, ExpectedVersion = Text(source, "expectedVersion"),
                Target = new() { RootId = Text(target, "rootId"), Path = Text(target, "path") }, CaptureId = Text(command, "captureId"),
                CaptureOwner = Text(command, "captureOwner"), CaptureKind = Text(command, "captureKind"), ManifestSha256 = Text(command, "manifestSha256"),
                TargetExpectedVersion = Text(target, "expectedVersion") },
            ArchiveSha256 = Text(value, "archiveSha256"), ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(), ContentSha256 = Text(value, "contentSha256"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            TargetParent = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() },
            SourcePolicyHash = Text(value, "sourcePolicyHash"), TargetPolicyHash = Text(value, "targetPolicyHash"), Fingerprint = Text(value, "fingerprint"),
            TargetManifestSha256 = Text(value, "targetManifestSha256"), TargetTreeSha256 = Text(value, "targetTreeSha256"), TargetContentSha256 = Text(value, "targetContentSha256"),
            TargetEntryCount = value.GetProperty("targetEntryCount").GetUInt32(), TargetTotalBytes = value.GetProperty("targetTotalBytes").GetInt64(),
            TargetIdentity = new() { Device = targetIdentity[0].GetUInt64(), Inode = targetIdentity[1].GetUInt64() } };
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }

    public static FileTreeOverwriteRestoringReceipt Receipt(JsonElement value, string operation, FileTreeOverwriteRestoringPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var captureId = Text(value, "captureId"); FileTransferPolicy.Identity(captureId);
        if (Text(value, "kind") != "DirectoryOverwriteRestore" || Text(value, "format") != "tar.gz")
            throw new InvalidOperationException("FILES_TREE_OVERWRITE_RESTORE_RECEIPT_INVALID");
        var node = JsonNode.Parse(value.GetRawText())!.AsObject(); node.Remove("captureId"); node.Remove("kind");
        foreach (var field in new[] { "overwrittenCaptureId", "backupMayBeRetained", "backupVerified", "originalsRetained", "targetMayHaveChanged", "backupArchiveBytes", "backupArchiveSha256" }) node.Remove(field);
        var receipt = FileTreeUnpackingPolicy.Receipt(JsonSerializer.SerializeToElement(node), operation);
        if (plan is not null && (captureId != plan.Command.CaptureId || receipt.Fingerprint != plan.Fingerprint || receipt.EntryCount != plan.EntryCount ||
            receipt.TotalBytes != plan.TotalBytes || receipt.ArchiveBytes != plan.ArchiveBytes || receipt.ArchiveSha256 != plan.ArchiveSha256))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        var overwrittenCaptureId = Text(value, "overwrittenCaptureId");
        var mayHaveBackup = value.GetProperty("backupMayBeRetained").GetBoolean();
        var verified = value.GetProperty("backupVerified").GetBoolean();
        var retained = value.GetProperty("originalsRetained").GetBoolean();
        var changed = value.GetProperty("targetMayHaveChanged").GetBoolean();
        var backupBytes = value.GetProperty("backupArchiveBytes").GetInt64();
        var backupHash = Text(value, "backupArchiveSha256");
        if (verified)
        {
            FileTransferPolicy.Hash(backupHash);
            if (!mayHaveBackup || overwrittenCaptureId != operation || backupBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes)
                throw new InvalidOperationException("FILES_TREE_OVERWRITE_RESTORE_RECEIPT_INVALID");
        }
        else if (overwrittenCaptureId.Length != 0 || backupBytes != 0 || backupHash.Length != 0)
            throw new InvalidOperationException("FILES_TREE_OVERWRITE_RESTORE_RECEIPT_INVALID");
        if (changed && !verified || retained && !changed || receipt.State == "Completed" && !(verified && retained && changed) || receipt.State == "Aborted" && changed)
            throw new InvalidOperationException("FILES_TREE_OVERWRITE_RESTORE_RECEIPT_INVALID");
        return new() { OperationId = receipt.OperationId, State = receipt.State, Fingerprint = receipt.Fingerprint, Format = receipt.Format,
            EntryCount = receipt.EntryCount, TotalBytes = receipt.TotalBytes, ArchiveBytes = receipt.ArchiveBytes, ArchiveSha256 = receipt.ArchiveSha256,
            Version = receipt.Version, PartialRetained = receipt.PartialRetained, Kind = "DirectoryOverwriteRestore", CaptureId = captureId,
            OverwrittenCaptureId = overwrittenCaptureId, BackupMayBeRetained = mayHaveBackup, BackupVerified = verified,
            OriginalsRetained = retained, TargetMayHaveChanged = changed, BackupArchiveBytes = backupBytes, BackupArchiveSha256 = backupHash };
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
