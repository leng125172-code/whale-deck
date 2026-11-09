using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleArchiveRetirementValidation
{
    private static ArgumentException Bad() => new("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_PLAN_INVALID");
    public static void Command(ContainerRecycleArchiveRetirementCommandDto? c)
    {
        if (c?.Archive is null || c.Archive.OperationId == Guid.Empty || c.Mode != "ArchiveData" || c.RetainDays is < 1 or > 3650) throw Bad();
        ContainerFileValidation.Hash(c.Archive.Owner); ContainerRecycleArchiveValidation.Plan(c.Archive.Plan);
        var purge = c.Archive.Plan.Command.Purge;
        if (c.Archive.OperationId == purge.OperationId || c.Archive.OperationId == purge.Plan.Command.Source.OperationId) throw Bad();
    }
    public static void Plan(ContainerRecycleArchiveRetirementPlanDto? p)
    {
        if (p is null || p.SchemaVersion != 1) throw Bad(); Command(p.Command);
        var old = p.Command.Archive.Plan; var original = old.Command.Purge.Plan.Command.Source.Plan.SourceIdentity;
        if (p.HistoryCreatedAtUnixMs is < 0 or > 253402300799999 || p.HistoryCreatedAtUnixMs != old.Command.Purge.Plan.HistoryCreatedAtUnixMs) throw Bad();
        var due = p.HistoryCreatedAtUnixMs + (long)p.Command.RetainDays * 86400000;
        if (due > 253402300799999 || p.NotBeforeUnixMs != due || p.RetirementEligible != (p.RetentionElapsed && p.BindingCurrent) ||
            p.SourceRecordDigest != old.SourceRecordDigest || p.PurgeRecordDigest != old.PurgeRecordDigest || p.PurgeMarkerDigest != old.PurgeMarkerDigest || p.HistoryMetadataDigest != old.HistoryMetadataDigest ||
            p.StorageHash != old.StorageHash || p.StoreIdentity != old.StoreIdentity || p.ArchiveIdentity != old.ArchiveIdentity || p.ArchiveChangedAtUnixNs != old.ArchiveChangedAtUnixNs || p.ArchiveBytes != old.ArchiveBytes || p.ArchiveSha256 != old.ArchiveSha256) throw Bad();
        var published = p.PublishedIdentity;
        if (published is not { Inode: > 0, LinkCount: 1 } || published.Device != old.ParentIdentity.Device ||
            published.Device == original.Device && published.Inode == original.Inode || published.Device == old.ArchiveIdentity.Device && published.Inode == old.ArchiveIdentity.Inode ||
            published.Mode != original.Mode || published.OwnerUid != original.OwnerUid || published.OwnerGid != original.OwnerGid || published.SizeBytes != original.SizeBytes || published.ModifiedAtUnixNs != original.ModifiedAtUnixNs || p.PublishedChangedAtUnixNs < 0) throw Bad();
        foreach (var hash in new[] { p.ArchiveRecordDigest, p.SourceRecordDigest, p.PurgeRecordDigest, p.PurgeMarkerDigest, p.ClaimDigest, p.HistoryMetadataDigest, p.StorageHash, p.ArchiveSha256, p.Fingerprint }) ContainerFileValidation.Hash(hash);
        // Durable diagnostics do not authorize expiry. Native execution must
        // reassess current storage, bindings and the real clock under locks.
    }
    public static void Operation(ContainerRecycleArchiveRetirementOperationDto? op)
    {
        if (op is null) throw Bad(); Plan(op.Plan);
        var archive = op.Plan.Command.Archive; var purge = archive.Plan.Command.Purge;
        if (op.Id == archive.OperationId || op.Id == purge.OperationId || op.Id == purge.Plan.Command.Source.OperationId) throw Bad();
        ContainerRecycleValidation.Operation(new(op.Id, op.Owner, purge.Plan.Command.Source.Plan, op.Context));
    }
    public static void Receipt(ContainerRecycleArchiveRetirementOperationDto op, ContainerRecycleArchiveRetirementReceiptDto? r)
    {
        if (r is null || r.Id != op.Id || r.SourceOperationId != op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId || r.ArchiveOperationId != op.Plan.Command.Archive.OperationId ||
            r.Fingerprint != op.Plan.Fingerprint || r.NotBeforeUnixMs != op.Plan.NotBeforeUnixMs || r.State is not ("Reserved" or "Aborted" or "Unknown" or "Completed") || r.OwnedFence && !r.SourceFenced ||
            r.State == "Reserved" && (!r.SourceFenced || !r.OwnedFence || r.RetirementApplied != false) ||
            r.State == "Aborted" && (r.OwnedFence || r.RetirementApplied != false) ||
            r.State == "Unknown" && (!r.SourceFenced || r.RetirementApplied == true) ||
            r.State == "Completed" && (!r.SourceFenced || !r.OwnedFence || r.RetirementApplied != true)) throw Invalid();
    }
    public static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_OUTPUT_INVALID");
}
