using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Method-capability authorized, registered-root directory recycling.</summary>
public sealed class FileTreeRecyclingGrpcService(FileTransferRunner runner) : FileTreeRecyclingService.FileTreeRecyclingServiceBase
{
    public override async Task<FileTreeRecyclingPlan> Plan(FileTreeRecyclingPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreeRecyclingPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-trash-plan", input, context.CancellationToken);
        return FileTreeRecyclingPolicy.Plan(response.RootElement, request.Command);
    }

    public override Task<FileTreeRecyclingReceipt> Apply(FileTreeRecyclingApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreeRecyclingReceipt> Discard(FileTreeRecyclingApplyRequest request, ServerCallContext context) => Write(request, context, true);

    private async Task<FileTreeRecyclingReceipt> Write(FileTreeRecyclingApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => { FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId);
            FileTransferPolicy.Hash(request.Owner); return FileTreeRecyclingPolicy.PlanInput(request.Plan); });
        using var response = await runner.OnceAsync(discard ? "tree-trash-discard" : "tree-trash-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreeRecyclingPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }

    public override async Task<FileTreeRecyclingReceipt> Inspect(FileTreeRecyclingLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-trash-status", input, context.CancellationToken);
        return FileTreeRecyclingPolicy.Receipt(response.RootElement, request.OperationId);
    }

    private static T Input<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_RECYCLE_INPUT_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreeRecyclingPolicy
{
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> SourceFields = ["rootId", "path", "expectedVersion"];
    private static readonly HashSet<string> PlanFields = ["source", "sourceParent", "sourcePolicyHash", "manifestSha256", "entryCount", "totalBytes", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "state", "fingerprint", "captureId", "manifestSha256", "entryCount", "totalBytes",
        "captureVerified", "captureMayBeRetained", "archiveBytes", "archiveSha256", "sourceMayHaveChanged", "sourceRemoved", "originalsRetained"];
    private static FileTreeCopyPolicy.RootSource Command(FileTreeRecyclingCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_RECYCLE_COMMAND_REQUIRED");
        FileTransferPolicy.Location(command.Source); FileTransferPolicy.Hash(command.ExpectedVersion);
        return new(command.Source.RootId, command.Source.Path, command.ExpectedVersion);
    }
    public static string CommandJson(FileTreeRecyclingCommand? command) => JsonSerializer.Serialize(Command(command), InputOptions);
    public static JsonElement PlanInput(FileTreeRecyclingPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_RECYCLE_PLAN_REQUIRED");
        var source = Command(plan.Command);
        FileTransferPolicy.Hash(plan.SourcePolicyHash); FileTransferPolicy.Hash(plan.ManifestSha256); FileTransferPolicy.Hash(plan.Fingerprint);
        if (plan.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries || plan.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes || plan.SourceParent is not { Inode: > 0 })
            throw new ArgumentException("FILES_RECYCLE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new { source, sourceParent = new[] { plan.SourceParent.Device, plan.SourceParent.Inode },
            sourcePolicyHash = plan.SourcePolicyHash, manifestSha256 = plan.ManifestSha256, entryCount = plan.EntryCount,
            totalBytes = plan.TotalBytes, fingerprint = plan.Fingerprint }, InputOptions);
    }
    public static FileTreeRecyclingPlan Plan(JsonElement value, FileTreeRecyclingCommand expected)
    {
        Fields(value, PlanFields); _ = Command(expected);
        var source = value.GetProperty("source"); Fields(source, SourceFields);
        var parent = value.GetProperty("sourceParent");
        if (parent.ValueKind != JsonValueKind.Array || parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PARENT_INVALID");
        var result = new FileTreeRecyclingPlan { Command = new() { Source = new() { RootId = Text(source, "rootId"), Path = Text(source, "path") }, ExpectedVersion = Text(source, "expectedVersion") },
            SourceParent = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() }, SourcePolicyHash = Text(value, "sourcePolicyHash"),
            ManifestSha256 = Text(value, "manifestSha256"), EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            Fingerprint = Text(value, "fingerprint") };
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }
    public static FileTreeRecyclingReceipt Receipt(JsonElement value, string operation, FileTreeRecyclingPlan? plan = null)
    {
        Fields(value, ReceiptFields);
        var result = new FileTreeRecyclingReceipt { OperationId = Text(value, "operationId"), State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"),
            CaptureId = Text(value, "captureId"), ManifestSha256 = Text(value, "manifestSha256"), EntryCount = value.GetProperty("entryCount").GetUInt32(),
            TotalBytes = value.GetProperty("totalBytes").GetInt64(), CaptureVerified = value.GetProperty("captureVerified").GetBoolean(),
            CaptureMayBeRetained = value.GetProperty("captureMayBeRetained").GetBoolean(), ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(),
            ArchiveSha256 = Text(value, "archiveSha256"), SourceMayHaveChanged = value.GetProperty("sourceMayHaveChanged").GetBoolean(),
            SourceRemoved = value.GetProperty("sourceRemoved").GetBoolean(), OriginalsRetained = value.GetProperty("originalsRetained").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Identity(result.CaptureId);
        FileTransferPolicy.Hash(result.Fingerprint); FileTransferPolicy.Hash(result.ManifestSha256);
        if (result.CaptureVerified) FileTransferPolicy.Hash(result.ArchiveSha256);
        if (result.OperationId != operation || result.CaptureId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries || result.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes ||
            result.CaptureVerified && (!result.CaptureMayBeRetained || result.ArchiveBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes) ||
            !result.CaptureVerified && (result.ArchiveBytes != 0 || result.ArchiveSha256.Length != 0) ||
            result.SourceMayHaveChanged && !result.CaptureVerified || result.State == "Aborted" && result.SourceMayHaveChanged ||
            result.State == "Completed" && !(result.CaptureVerified && result.SourceMayHaveChanged && result.SourceRemoved && result.OriginalsRetained) ||
            result.State != "Completed" && (result.SourceRemoved || result.OriginalsRetained))
            throw new InvalidOperationException("FILES_RECYCLE_RECEIPT_INVALID");
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.ManifestSha256 != plan.ManifestSha256 || result.EntryCount != plan.EntryCount || result.TotalBytes != plan.TotalBytes))
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
