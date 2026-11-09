namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleArchiveRetirementSourceDto(Guid OperationId, string Owner, ContainerRecycleArchivePlanDto Plan)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementSourceDto); }
public sealed record ContainerRecycleArchiveRetirementCommandDto(ContainerRecycleArchiveRetirementSourceDto Archive, uint RetainDays, string Mode)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementCommandDto); }
public sealed record ContainerRecycleArchiveRetirementPlanDto(uint SchemaVersion, ContainerRecycleArchiveRetirementCommandDto Command,
    string ArchiveRecordDigest, string SourceRecordDigest, string PurgeRecordDigest, string PurgeMarkerDigest, string ClaimDigest, string HistoryMetadataDigest,
    long HistoryCreatedAtUnixMs, long NotBeforeUnixMs, bool RetentionElapsed, bool BindingCurrent, bool RetirementEligible,
    string StorageHash, FileParentIdentity StoreIdentity, FileObjectIdentityDto ArchiveIdentity, long ArchiveChangedAtUnixNs,
    long ArchiveBytes, string ArchiveSha256, FileObjectIdentityDto PublishedIdentity, long PublishedChangedAtUnixNs, string Fingerprint)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementPlanDto); }
public sealed record ContainerRecycleArchiveRetirementOperationDto(Guid Id, string Owner, ContainerRecycleArchiveRetirementPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementOperationDto); }
public sealed record ContainerRecycleArchiveRetirementReceiptDto(Guid Id, Guid SourceOperationId, Guid ArchiveOperationId, string Fingerprint,
    string State, bool SourceFenced, bool OwnedFence, bool? RetirementApplied, long NotBeforeUnixMs);
