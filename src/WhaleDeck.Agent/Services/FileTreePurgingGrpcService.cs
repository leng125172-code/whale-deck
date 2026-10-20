using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Method-bound capability for a single frozen directory payload purge.</summary>
public sealed class FileTreePurgingGrpcService(FileTransferRunner runner) : FileTreePurgingService.FileTreePurgingServiceBase
{
    public override async Task<FileTreePurgingPlan> Plan(FileTreePurgingPlanRequest request, ServerCallContext context)
    {
        var input = Input(() => { FileOperationPolicy.Actor(request.Context); return FileTreePurgingPolicy.CommandJson(request.Command); });
        using var response = await runner.OnceAsync("tree-purge-plan", input, context.CancellationToken);
        return FileTreePurgingPolicy.Plan(response.RootElement, request.Command);
    }

    public override Task<FileTreePurgingReceipt> Apply(FileTreePurgingApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileTreePurgingReceipt> Discard(FileTreePurgingApplyRequest request, ServerCallContext context) => Write(request, context, true);

    private async Task<FileTreePurgingReceipt> Write(FileTreePurgingApplyRequest request, ServerCallContext context, bool discard)
    {
        var plan = Input(() => {
            FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            var value = FileTreePurgingPolicy.PlanInput(request.Plan);
            if (request.OperationId == request.Plan.Command.CaptureId) throw new ArgumentException("FILES_TREE_PURGE_INVALID");
            return value;
        });
        using var response = await runner.OnceAsync(discard ? "tree-purge-discard" : "tree-purge-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileTreePurgingPolicy.Receipt(response.RootElement, request.OperationId, request.Plan);
    }

    public override async Task<FileTreePurgingReceipt> Inspect(FileTreePurgingLookup request, ServerCallContext context)
    {
        var input = Input(() => { FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
            return JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }); });
        using var response = await runner.OnceAsync("tree-purge-status", input, context.CancellationToken);
        return FileTreePurgingPolicy.Receipt(response.RootElement, request.OperationId);
    }

    private static T Input<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException error)
        {
            var code = error.Message;
            if (code.Length > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(character => !(char.IsAsciiLetterUpper(character) || character == '_')))
                code = "FILES_TREE_PURGE_INVALID";
            throw new RpcException(new(StatusCode.InvalidArgument, code));
        }
    }
}

internal static class FileTreePurgingPolicy
{
    private static readonly HashSet<string> CommandFields = ["source", "captureId", "captureOwner", "captureKind", "manifestSha256", "scope"];
    private static readonly HashSet<string> PlanFields = ["command", "sourcePolicyHash", "sourceParent", "storageHash", "storeIdentity", "captureMetadataHash",
        "originReceiptHash", "manifestFileSha256", "archiveIdentity", "archiveBytes", "archiveSha256", "contentSha256", "entryCount", "totalBytes",
        "retainedIdentity", "stagingIdentity", "retainedTreeSha256", "fingerprint"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "kind", "state", "fingerprint", "captureId", "scope", "entryCount", "totalBytes",
        "archiveBytes", "deletionStarted", "archiveRemoved", "originalsRemoved"];

    public static string Scope(FileTreePurgeScope scope) => scope switch {
        FileTreePurgeScope.Archive => "Archive", FileTreePurgeScope.Originals => "Originals", FileTreePurgeScope.All => "All",
        _ => throw new ArgumentException("FILES_TREE_PURGE_SCOPE_INVALID") };

    private static FileTreePurgeScope Scope(string scope) => scope switch {
        "Archive" => FileTreePurgeScope.Archive, "Originals" => FileTreePurgeScope.Originals, "All" => FileTreePurgeScope.All,
        _ => throw new InvalidOperationException("FILES_TREE_PURGE_SCOPE_INVALID") };

    public static string CommandJson(FileTreePurgingCommand? command)
    {
        if (command is null) throw new ArgumentException("FILES_TREE_PURGE_INVALID");
        FileTransferPolicy.Location(command.Source); FileTransferPolicy.Hash(command.ExpectedVersion);
        FileTransferPolicy.Identity(command.CaptureId); FileTransferPolicy.Hash(command.CaptureOwner); FileTransferPolicy.Hash(command.ManifestSha256);
        if (command.CaptureKind is not ("Trash" or "Overwrite")) throw new ArgumentException("FILES_TREE_PURGE_INVALID");
        return JsonSerializer.Serialize(new {
            source = new { rootId = command.Source.RootId, path = command.Source.Path, expectedVersion = command.ExpectedVersion },
            captureId = command.CaptureId, captureOwner = command.CaptureOwner, captureKind = command.CaptureKind,
            manifestSha256 = command.ManifestSha256, scope = Scope(command.Scope) });
    }

    private static ulong[] Identity(FileDirectoryIdentity? value)
    {
        if (value is not { Inode: > 0 }) throw new ArgumentException("FILES_TREE_PURGE_IDENTITY_INVALID");
        return [value.Device, value.Inode];
    }

    public static JsonElement PlanInput(FileTreePurgingPlan? plan)
    {
        if (plan is null) throw new ArgumentException("FILES_TREE_PURGE_PLAN_REQUIRED");
        var command = JsonSerializer.Deserialize<JsonElement>(CommandJson(plan.Command));
        foreach (var hash in new[] { plan.SourcePolicyHash, plan.StorageHash, plan.CaptureMetadataHash, plan.OriginReceiptHash,
            plan.ManifestFileSha256, plan.ArchiveSha256, plan.ContentSha256, plan.Fingerprint }) FileTransferPolicy.Hash(hash);
        if (plan.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries || plan.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes ||
            plan.ArchiveBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes) throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
        var originals = plan.Command.Scope is FileTreePurgeScope.Originals or FileTreePurgeScope.All;
        ulong[]? parent = null, retained = null, staging = null; string? tree = null;
        if (originals)
        {
            parent = Identity(plan.SourceParent); retained = Identity(plan.RetainedIdentity); staging = Identity(plan.StagingIdentity);
            if (!plan.HasRetainedTreeSha256) throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
            FileTransferPolicy.Hash(plan.RetainedTreeSha256); tree = plan.RetainedTreeSha256;
        }
        else if (plan.SourceParent is not null || plan.RetainedIdentity is not null || plan.StagingIdentity is not null || plan.HasRetainedTreeSha256)
            throw new ArgumentException("FILES_TREE_PURGE_PLAN_INVALID");
        return JsonSerializer.SerializeToElement(new {
            command, sourcePolicyHash = plan.SourcePolicyHash, sourceParent = parent, storageHash = plan.StorageHash,
            storeIdentity = Identity(plan.StoreIdentity), captureMetadataHash = plan.CaptureMetadataHash, originReceiptHash = plan.OriginReceiptHash,
            manifestFileSha256 = plan.ManifestFileSha256, archiveIdentity = Identity(plan.ArchiveIdentity), archiveBytes = plan.ArchiveBytes,
            archiveSha256 = plan.ArchiveSha256, contentSha256 = plan.ContentSha256, entryCount = plan.EntryCount, totalBytes = plan.TotalBytes,
            retainedIdentity = retained, stagingIdentity = staging, retainedTreeSha256 = tree, fingerprint = plan.Fingerprint });
    }

    public static FileTreePurgingPlan Plan(JsonElement value, FileTreePurgingCommand expected)
    {
        Fields(value, PlanFields); _ = CommandJson(expected);
        var command = value.GetProperty("command"); Fields(command, CommandFields);
        var source = command.GetProperty("source"); Fields(source, ["rootId", "path", "expectedVersion"]);
        var result = new FileTreePurgingPlan {
            Command = new() { Source = new() { RootId = Text(source, "rootId"), Path = Text(source, "path") }, ExpectedVersion = Text(source, "expectedVersion"),
                CaptureId = Text(command, "captureId"), CaptureOwner = Text(command, "captureOwner"), CaptureKind = Text(command, "captureKind"),
                ManifestSha256 = Text(command, "manifestSha256"), Scope = Scope(Text(command, "scope")) },
            SourcePolicyHash = Text(value, "sourcePolicyHash"), SourceParent = ReadIdentity(value, "sourceParent", true),
            StorageHash = Text(value, "storageHash"), StoreIdentity = ReadIdentity(value, "storeIdentity"),
            CaptureMetadataHash = Text(value, "captureMetadataHash"), OriginReceiptHash = Text(value, "originReceiptHash"),
            ManifestFileSha256 = Text(value, "manifestFileSha256"), ArchiveIdentity = ReadIdentity(value, "archiveIdentity"),
            ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(), ArchiveSha256 = Text(value, "archiveSha256"), ContentSha256 = Text(value, "contentSha256"),
            EntryCount = value.GetProperty("entryCount").GetUInt32(), TotalBytes = value.GetProperty("totalBytes").GetInt64(),
            RetainedIdentity = ReadIdentity(value, "retainedIdentity", true), StagingIdentity = ReadIdentity(value, "stagingIdentity", true), Fingerprint = Text(value, "fingerprint") };
        if (value.GetProperty("retainedTreeSha256").ValueKind != JsonValueKind.Null) result.RetainedTreeSha256 = Text(value, "retainedTreeSha256");
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_PLAN_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }

    public static FileTreePurgingReceipt Receipt(JsonElement value, string operation, FileTreePurgingPlan? plan = null)
    {
        Fields(value, ReceiptFields); FileTransferPolicy.Identity(operation);
        var result = new FileTreePurgingReceipt {
            OperationId = Text(value, "operationId"), Kind = Text(value, "kind"), State = Text(value, "state"), Fingerprint = Text(value, "fingerprint"),
            CaptureId = Text(value, "captureId"), Scope = Scope(Text(value, "scope")), EntryCount = value.GetProperty("entryCount").GetUInt32(),
            TotalBytes = value.GetProperty("totalBytes").GetInt64(), ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(),
            DeletionStarted = value.GetProperty("deletionStarted").GetBoolean(), ArchiveRemoved = value.GetProperty("archiveRemoved").GetBoolean(),
            OriginalsRemoved = value.GetProperty("originalsRemoved").GetBoolean() };
        FileTransferPolicy.Identity(result.OperationId); FileTransferPolicy.Identity(result.CaptureId); FileTransferPolicy.Hash(result.Fingerprint);
        var archive = result.Scope is FileTreePurgeScope.Archive or FileTreePurgeScope.All;
        var originals = result.Scope is FileTreePurgeScope.Originals or FileTreePurgeScope.All;
        if (result.Kind != "DirectoryPurge" || result.OperationId != operation || result.OperationId == result.CaptureId || result.State is not ("Completed" or "Aborted" or "Unknown") ||
            result.EntryCount is < 1 or > FileTreeCopyPolicy.MaximumEntries || result.TotalBytes is < 0 or > FileTreeCopyPolicy.MaximumBytes ||
            result.ArchiveBytes is < 1 or > FileTreeUnpackingPolicy.MaximumArchiveBytes || result.ArchiveRemoved && !archive || result.OriginalsRemoved && !originals ||
            (result.ArchiveRemoved || result.OriginalsRemoved) && !result.DeletionStarted ||
            result.State == "Aborted" && result.DeletionStarted || result.State != "Aborted" && !result.DeletionStarted ||
            result.State == "Completed" && (archive && !result.ArchiveRemoved || originals && !result.OriginalsRemoved))
            throw new InvalidOperationException("FILES_TREE_PURGE_RECEIPT_INVALID");
        if (plan is not null && (result.Fingerprint != plan.Fingerprint || result.CaptureId != plan.Command.CaptureId || result.Scope != plan.Command.Scope ||
            result.EntryCount != plan.EntryCount || result.TotalBytes != plan.TotalBytes || result.ArchiveBytes != plan.ArchiveBytes))
            throw new InvalidOperationException("FILES_RECEIPT_TARGET_MISMATCH");
        return result;
    }

    private static FileDirectoryIdentity? ReadIdentity(JsonElement value, string name, bool nullable = false)
    {
        var array = value.GetProperty(name);
        if (nullable && array.ValueKind == JsonValueKind.Null) return null;
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 2) throw new InvalidOperationException("FILES_TREE_PURGE_IDENTITY_INVALID");
        return new() { Device = array[0].GetUInt64(), Inode = array[1].GetUInt64() };
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var fields = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (fields.Length != expected.Count || fields.Distinct(StringComparer.Ordinal).Count() != expected.Count || !fields.All(expected.Contains))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
}
