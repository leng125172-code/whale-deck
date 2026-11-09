using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

// Semantic policy preparation only. No service registration or native grant.
internal static class ContainerRecycleArchiveRetirementPolicy
{
    private const long MaximumTime = 253402300799999;
    private static ArgumentException Bad() => new("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");

    public static void Command(ContainerRecycleArchiveRetirementCommand? c)
    {
        if (c?.Archive?.Plan is null || c.Mode != "ArchiveData" || c.RetainDays is < 1 or > 3650) throw Bad();
        ContainerRecycleArchiveRetirementWire.RequireKnown(c); FileTransferPolicy.Identity(c.Archive.OperationId); FileTransferPolicy.Hash(c.Archive.Owner);
        ContainerRecycleArchivePolicy.Plan(c.Archive.Plan);
        var purge = c.Archive.Plan.Command.Purge;
        if (c.Archive.OperationId == purge.OperationId || c.Archive.OperationId == purge.Plan.Command.Source.OperationId) throw Bad();
    }

    public static void Plan(ContainerRecycleArchiveRetirementPlan? p)
    {
        if (p is null || p.SchemaVersion != 1) throw Bad();
        ContainerRecycleArchiveRetirementWire.RequireKnown(p); Command(p.Command);
        var old = p.Command.Archive.Plan; var original = old.Command.Purge.Plan.Command.Source.Plan.SourceIdentity;
        var due = p.HistoryCreatedAtUnixMs + (long)p.Command.RetainDays * 86400000;
        if (p.HistoryCreatedAtUnixMs is < 0 or > MaximumTime || p.HistoryCreatedAtUnixMs != old.Command.Purge.Plan.HistoryCreatedAtUnixMs ||
            due > MaximumTime || p.NotBeforeUnixMs != due || p.RetirementEligible != (p.RetentionElapsed && p.BindingCurrent) ||
            p.SourceRecordDigest != old.SourceRecordDigest || p.PurgeRecordDigest != old.PurgeRecordDigest || p.PurgeMarkerDigest != old.PurgeMarkerDigest || p.HistoryMetadataDigest != old.HistoryMetadataDigest ||
            p.StorageHash != old.StorageHash || p.StoreIdentity is null || !p.StoreIdentity.Equals(old.StoreIdentity) ||
            p.ArchiveIdentity is null || !p.ArchiveIdentity.Equals(old.ArchiveIdentity) || p.ArchiveChangedAtUnixNs != old.ArchiveChangedAtUnixNs ||
            p.ArchiveBytes != old.ArchiveBytes || p.ArchiveSha256 != old.ArchiveSha256) throw Bad();
        var published = p.PublishedIdentity;
        if (published is not { Inode: > 0, LinkCount: 1 } || published.Device != old.ParentIdentity.Device ||
            published.Device == original.Device && published.Inode == original.Inode || published.Device == old.ArchiveIdentity.Device && published.Inode == old.ArchiveIdentity.Inode ||
            published.Mode != original.Mode || published.OwnerUid != original.OwnerUid || published.OwnerGid != original.OwnerGid || published.SizeBytes != original.SizeBytes || published.ModifiedAtUnixNs != original.ModifiedAtUnixNs || p.PublishedChangedAtUnixNs < 0) throw Bad();
        foreach (var hash in new[] { p.ArchiveRecordDigest, p.SourceRecordDigest, p.PurgeRecordDigest, p.PurgeMarkerDigest, p.ClaimDigest, p.HistoryMetadataDigest, p.StorageHash, p.ArchiveSha256, p.Fingerprint }) FileTransferPolicy.Hash(hash);
        if (p.Fingerprint != ContainerRecycleArchiveRetirementWire.Fingerprint(p)) throw Bad();
        // Diagnostics may be stale in a durable reservation. Only the native
        // executor's fresh, locked wall-clock assessment authorizes expiry.
    }

    public static void PlanRequest(ContainerRecycleArchiveRetirementPlanRequest? request)
    {
        if (request is null) throw Bad();
        ContainerRecycleArchiveRetirementWire.RequireKnown(request); Command(request.Command); ContainerRecyclePolicy.Context(request.Context, planning: true);
    }

    public static void Request(ContainerRecycleArchiveRetirementRequest? request)
    {
        if (request is null) throw Bad();
        ContainerRecycleArchiveRetirementWire.RequireKnown(request); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner); Plan(request.Plan); ContainerRecyclePolicy.Context(request.Context);
        var archive = request.Plan.Command.Archive; var purge = archive.Plan.Command.Purge;
        if (request.Context.JobId != request.OperationId || request.OperationId == archive.OperationId || request.OperationId == purge.OperationId || request.OperationId == purge.Plan.Command.Source.OperationId) throw Bad();
    }

    public static void Binding(IMessage request, ServerCallContext context)
    {
        ContainerRecycleArchiveRetirementWire.RequireKnown(request);
        var claimed = context.RequestHeaders.GetValue(ContainerRecycleArchiveRetirementBinding.Header); var actual = ContainerRecycleArchiveRetirementBinding.Hash(request);
        if (claimed is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(claimed)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_BINDING_INVALID"));
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var keys = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != names.Length || keys.Distinct(StringComparer.Ordinal).Count() != names.Length || keys.Except(names, StringComparer.Ordinal).Any()) throw Invalid();
    }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw Invalid();
    private static (FileObjectIdentity Identity, long Changed) Identity(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 9) throw Invalid();
        return (new() { Device = value[0].GetUInt64(), Inode = value[1].GetUInt64(), Mode = value[2].GetUInt32(), OwnerUid = value[3].GetUInt32(), OwnerGid = value[4].GetUInt32(), LinkCount = value[5].GetUInt64(), SizeBytes = value[6].GetInt64(), ModifiedAtUnixNs = value[7].GetInt64() }, value[8].GetInt64());
    }

    public static ContainerRecycleArchiveRetirementPlan PlanOutput(JsonElement value, ContainerRecycleArchiveRetirementCommand expected)
    {
        try
        {
            Command(expected);
            Fields(value, "schemaVersion", "command", "archiveRecordDigest", "sourceRecordDigest", "purgeRecordDigest", "purgeMarkerDigest", "claimDigest", "historyMetadataDigest", "historyCreatedAtUnixMs", "notBeforeUnixMs", "retentionElapsed", "bindingCurrent", "retirementEligible", "storageHash", "storeIdentity", "archiveIdentity", "archiveBytes", "archiveSha256", "publishedIdentity", "fingerprint");
            var c = value.GetProperty("command"); Fields(c, "archive", "retainDays", "mode"); var a = c.GetProperty("archive"); Fields(a, "operationId", "owner", "plan");
            var command = new ContainerRecycleArchiveRetirementCommand { Mode = Text(c, "mode"), RetainDays = c.GetProperty("retainDays").GetUInt32(), Archive = new() { OperationId = Text(a, "operationId"), Owner = Text(a, "owner"), Plan = ContainerRecycleArchivePolicy.PlanOutput(a.GetProperty("plan"), expected.Archive.Plan.Command) } };
            if (!command.Equals(expected)) throw Invalid();
            var store = value.GetProperty("storeIdentity"); if (store.ValueKind != JsonValueKind.Array || store.GetArrayLength() != 2) throw Invalid();
            var archive = Identity(value.GetProperty("archiveIdentity")); var published = Identity(value.GetProperty("publishedIdentity"));
            var p = new ContainerRecycleArchiveRetirementPlan { SchemaVersion = value.GetProperty("schemaVersion").GetUInt32(), Command = command,
                ArchiveRecordDigest = Text(value, "archiveRecordDigest"), SourceRecordDigest = Text(value, "sourceRecordDigest"), PurgeRecordDigest = Text(value, "purgeRecordDigest"), PurgeMarkerDigest = Text(value, "purgeMarkerDigest"), ClaimDigest = Text(value, "claimDigest"), HistoryMetadataDigest = Text(value, "historyMetadataDigest"),
                HistoryCreatedAtUnixMs = value.GetProperty("historyCreatedAtUnixMs").GetInt64(), NotBeforeUnixMs = value.GetProperty("notBeforeUnixMs").GetInt64(), RetentionElapsed = value.GetProperty("retentionElapsed").GetBoolean(), BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), RetirementEligible = value.GetProperty("retirementEligible").GetBoolean(),
                StorageHash = Text(value, "storageHash"), StoreIdentity = new() { Device = store[0].GetUInt64(), Inode = store[1].GetUInt64() }, ArchiveIdentity = archive.Identity, ArchiveChangedAtUnixNs = archive.Changed, ArchiveBytes = value.GetProperty("archiveBytes").GetInt64(), ArchiveSha256 = Text(value, "archiveSha256"), PublishedIdentity = published.Identity, PublishedChangedAtUnixNs = published.Changed, Fingerprint = Text(value, "fingerprint") };
            Plan(p); return p;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException or JsonException or KeyNotFoundException) { throw Invalid(); }
    }

    public static ContainerRecycleArchiveRetirementReceipt Receipt(JsonElement value, ContainerRecycleArchiveRetirementRequest request)
    {
        try
        {
            Fields(value, "operationId", "sourceOperationId", "archiveOperationId", "fingerprint", "state", "sourceFenced", "ownedFence", "retirementApplied", "notBeforeUnixMs");
            var receipt = new ContainerRecycleArchiveRetirementReceipt { OperationId = Text(value, "operationId"), SourceOperationId = Text(value, "sourceOperationId"), ArchiveOperationId = Text(value, "archiveOperationId"), Fingerprint = Text(value, "fingerprint"), State = Text(value, "state"), SourceFenced = value.GetProperty("sourceFenced").GetBoolean(), OwnedFence = value.GetProperty("ownedFence").GetBoolean(), NotBeforeUnixMs = value.GetProperty("notBeforeUnixMs").GetInt64() };
            if (value.GetProperty("retirementApplied").ValueKind != JsonValueKind.Null) receipt.RetirementApplied = value.GetProperty("retirementApplied").GetBoolean();
            ContainerRecycleArchiveRetirementWire.Receipt(request, receipt); return receipt;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException or JsonException or KeyNotFoundException) { throw Invalid(); }
    }

    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TIMEOUT" => StatusCode.DeadlineExceeded,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID" => StatusCode.InvalidArgument,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_MODULE_UNSAFE" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INVOCATION_INVALID" => StatusCode.FailedPrecondition,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NEW_OPERATION_REQUIRED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_REQUEST_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_PLAN_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_TIME_RANGE" => StatusCode.InvalidArgument,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_COPY_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_IDEMPOTENCY_CONFLICT" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_PLAN_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_SOURCE_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_COPY_CHANGED" => StatusCode.Aborted,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_RECEIPT_LIMIT" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_BUSY" => StatusCode.ResourceExhausted,
            "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_DUE" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_ELIGIBLE" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_RESERVED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_STATE_UNSAFE" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_FENCE_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_HISTORY_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_COPY_UNAVAILABLE" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_COMPLETION_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_MARKER_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_RECEIPT_INVALID" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SESSION_REQUIRED" or "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_RESERVATION_ONLY" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_SESSION_REQUIRED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_COPY_UNCONFIRMED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_PURGE_UNCONFIRMED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_FENCE_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_HISTORY_CHANGED" or "CONTAINER_RECYCLE_ARCHIVE_RETENTION_CLOCK_BEFORE_CAPTURE" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        if (status == StatusCode.Unavailable)
        {
            var ancestor = ContainerRecycleArchivePolicy.Failure(code);
            if (ancestor.StatusCode != StatusCode.Unavailable && ancestor.StatusCode != StatusCode.NotFound) return ancestor;
        }
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_UNAVAILABLE" : code!));
    }

    private static InvalidOperationException Invalid() => ContainerRecycleArchiveRetirementWire.Invalid();
}
