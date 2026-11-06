namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleArchiveRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, Guid SourceIntentId, string SourceProofHash, ContainerRecycleArchiveCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleArchiveRequestBinding); }
public sealed record ContainerRecycleArchivePlanBinding(ContainerRecycleArchiveRequestBinding Request, ContainerRecycleArchivePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleArchivePlanBinding); }
public sealed record ContainerRecycleArchiveExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleArchiveExecutionBinding); }
public sealed record ContainerRecycleArchiveConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleArchiveConfirmation); }
public sealed record ConfirmContainerRecycleArchiveCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecycleArchiveCommand); }
public sealed record ContainerRecycleArchiveConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecycleArchiveConfirmationDto); }
public sealed record ContainerRecycleArchiveIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, Guid SourceIntentId, Guid PurgeOperationId, Guid SourceOperationId, string SourceHistoryDigest,
    ContainerFileLocationDto Location, string State, uint? OriginalUid, uint? OriginalGid, uint? OriginalMode, uint? ExecutorUid, uint? ExecutorGid,
    long SizeBytes, string Sha256, int AttributeCount, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool ReservationRecorded, bool? ArchiveCopied, ContainerRecycleArchiveReceiptDto? Receipt, ContainerRecycleArchiveConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecycleArchiveIntentDto); }
public sealed record AcknowledgeContainerRecycleArchiveCommand(long Version, bool Confirmed);

public sealed record ContainerRecycleArchiveReceiptBinding(ContainerRecycleArchiveOperationDto Operation, ContainerRecycleArchiveReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecycleArchiveReceiptBinding); }

public sealed record ContainerRecycleArchiveSourceBinding(Guid IntentId, string RequestHash, string PlanHash, string ExecutionHash, string ReceiptHash, string OriginalProofHash,
    ContainerRecycleArchiveCommandDto Command)
{ public override string ToString() => nameof(ContainerRecycleArchiveSourceBinding); }
public sealed record ResolvedContainerRecycleArchiveSource(Guid IntentId, string ProofHash, ContainerRecycleArchiveCommandDto Command)
{ public override string ToString() => nameof(ResolvedContainerRecycleArchiveSource); }
