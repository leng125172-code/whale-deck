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

public sealed record ContainerRecycleArchiveRetirementSourceBinding(Guid ArchiveIntentId, string RequestHash, string PlanHash, string ExecutionHash,
    string ReservationHash, string ReceiptHash, string ArchiveSourceProofHash, ContainerRecycleArchiveRetirementCommandDto Command,
    FileObjectIdentityDto PublishedIdentity, long PublishedChangedAtUnixNs)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementSourceBinding); }

public sealed record ResolvedContainerRecycleArchiveRetirementSource(ContainerRecycleArchiveRetirementSourceBinding Binding, string ProofHash)
{ public override string ToString() => nameof(ResolvedContainerRecycleArchiveRetirementSource); }

public sealed record RetirementConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(RetirementConfirmation); }

public sealed record ContainerRecycleArchiveRetirementConfirmationDto(string Token, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementConfirmationDto); }

public sealed record ContainerRecycleArchiveRetirementIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, Guid ArchiveIntentId, int RetainDays,
    string State, long Version, DateTimeOffset? ReadyExpiresAtUtc, long? NotBeforeUnixMs, long? ArchiveBytes, bool? RetirementEligible,
    bool ReservationRecorded, bool ApplyStarted, bool PublishBarrier, bool? RetirementApplied, string? RootState, string? ResultCode,
    ContainerRecycleArchiveRetirementConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecycleArchiveRetirementIntentDto); }
