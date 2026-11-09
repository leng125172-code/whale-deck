using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

/// <summary>Native-compatible projection. A valid fingerprint is not execution authority.</summary>
public static class ContainerRecycleArchiveRetirementWire
{
    public static void RequireKnown(IMessage value)
    {
        try { ContainerRecycleRestoreWire.RequireKnown(value); }
        catch (ArgumentException) { throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SCHEMA_INVALID"); }
    }

    public static object Command(ContainerRecycleArchiveRetirementCommand c)
    {
        RequireKnown(c);
        if (c.Archive?.Plan is null) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");
        return new { archive = new { operationId = c.Archive.OperationId, owner = c.Archive.Owner, plan = ContainerRecycleArchiveWire.Plan(c.Archive.Plan) }, retainDays = c.RetainDays, mode = c.Mode };
    }

    public static object Plan(ContainerRecycleArchiveRetirementPlan p, bool omitFingerprint = false)
    {
        RequireKnown(p);
        var c = p.Command;
        if (c?.Archive?.Plan is null || p.StoreIdentity is null || p.ArchiveIdentity is null || p.PublishedIdentity is null)
            throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");
        var value = new Dictionary<string, object?>
        {
            ["schemaVersion"] = p.SchemaVersion,
            ["command"] = Command(c),
            ["archiveRecordDigest"] = p.ArchiveRecordDigest, ["sourceRecordDigest"] = p.SourceRecordDigest,
            ["purgeRecordDigest"] = p.PurgeRecordDigest, ["purgeMarkerDigest"] = p.PurgeMarkerDigest,
            ["claimDigest"] = p.ClaimDigest, ["historyMetadataDigest"] = p.HistoryMetadataDigest,
            ["historyCreatedAtUnixMs"] = p.HistoryCreatedAtUnixMs, ["notBeforeUnixMs"] = p.NotBeforeUnixMs,
            ["retentionElapsed"] = p.RetentionElapsed, ["bindingCurrent"] = p.BindingCurrent, ["retirementEligible"] = p.RetirementEligible,
            ["storageHash"] = p.StorageHash, ["storeIdentity"] = new[] { p.StoreIdentity.Device, p.StoreIdentity.Inode },
            ["archiveIdentity"] = ContainerRecycleArchiveWire.Identity(p.ArchiveIdentity, p.ArchiveChangedAtUnixNs),
            ["archiveBytes"] = p.ArchiveBytes, ["archiveSha256"] = p.ArchiveSha256,
            ["publishedIdentity"] = ContainerRecycleArchiveWire.Identity(p.PublishedIdentity, p.PublishedChangedAtUnixNs)
        };
        if (!omitFingerprint) value["fingerprint"] = p.Fingerprint;
        return value;
    }

    public static string Fingerprint(ContainerRecycleArchiveRetirementPlan p)
    {
        var canonical = new StringBuilder();
        ContainerRecycleRestoreWire.Canonical(JsonSerializer.SerializeToElement(Plan(p, true)), canonical);
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes("whaledeck-container-recycle-archive-retention-plan-v1\n" + canonical)));
    }

    public static void Receipt(ContainerRecycleArchiveRetirementRequest request, ContainerRecycleArchiveRetirementReceipt value)
    {
        if (request?.Plan?.Command?.Archive?.Plan?.Command?.Purge?.Plan?.Command?.Source is null || value is null) throw Invalid();
        try { RequireKnown(value); } catch (ArgumentException) { throw Invalid(); }
        var q = request; var archive = q.Plan.Command.Archive;
        if (value.OperationId != q.OperationId || value.SourceOperationId != archive.Plan.Command.Purge.Plan.Command.Source.OperationId ||
            value.ArchiveOperationId != archive.OperationId || value.Fingerprint != q.Plan.Fingerprint || value.NotBeforeUnixMs != q.Plan.NotBeforeUnixMs ||
            value.State is not ("Reserved" or "Aborted" or "Unknown" or "Completed") || value.OwnedFence && !value.SourceFenced ||
            value.State == "Reserved" && (!value.SourceFenced || !value.OwnedFence || !value.HasRetirementApplied || value.RetirementApplied) ||
            value.State == "Aborted" && (value.OwnedFence || !value.HasRetirementApplied || value.RetirementApplied) ||
            value.State == "Unknown" && (!value.SourceFenced || value.HasRetirementApplied && value.RetirementApplied) ||
            value.State == "Completed" && (!value.SourceFenced || !value.OwnedFence || !value.HasRetirementApplied || !value.RetirementApplied)) throw Invalid();
    }

    public static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_OUTPUT_INVALID");
}
