namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleRestoreRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, Guid SourceIntentId, string SourceProofHash, ContainerRecycleRestoreCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleRestoreRequestBinding); }
public sealed record ContainerRecycleRestorePlanBinding(ContainerRecycleRestoreRequestBinding Request, ContainerRecycleRestorePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleRestorePlanBinding); }
public sealed record ContainerRecycleRestoreExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleRestoreExecutionBinding); }
public sealed record ContainerRecycleRestoreConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleRestoreConfirmation); }
public sealed record ConfirmContainerRecycleRestoreCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecycleRestoreCommand); }
public sealed record ContainerRecycleRestoreConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecycleRestoreConfirmationDto); }
public sealed record ContainerRecycleRestoreIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, Guid SourceIntentId, Guid SourceOperationId, string SourceHistoryDigest,
    ContainerFileLocationDto Location, string State, uint? OriginalUid, uint? OriginalGid, uint? OriginalMode, uint? ExecutorUid, uint? ExecutorGid,
    long SizeBytes, string Sha256, int AttributeCount, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryVerified, string? HistoryDigest, ContainerRecycleRestoreReceiptDto? Receipt, ContainerRecycleRestoreConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecycleRestoreIntentDto); }
public sealed record AcknowledgeContainerRecycleRestoreCommand(long Version, bool Confirmed);

public sealed record ContainerRecycleRestoreReceiptBinding(ContainerRecycleRestoreOperationDto Operation, ContainerRecycleRestoreReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecycleRestoreReceiptBinding); }

public sealed record ContainerRecycleRestoreSourceBinding(Guid IntentId, string RequestHash, string PlanHash, string ExecutionHash, string ReceiptHash,
    ContainerRecycleRestoreCommandDto Command)
{ public override string ToString() => nameof(ContainerRecycleRestoreSourceBinding); }
public sealed record ResolvedContainerRecycleRestoreSource(Guid IntentId, string ProofHash, ContainerRecycleRestoreCommandDto Command)
{ public override string ToString() => nameof(ResolvedContainerRecycleRestoreSource); }

public sealed record ContainerRecycleRestoreSourceSummary(Guid SourceIntentId, Guid SourceOperationId, ContainerFileLocationDto Location,
    uint OriginalUid, uint OriginalGid, uint OriginalMode, long SizeBytes, string Sha256, int AttributeCount, string HistoryDigest, DateTimeOffset CompletedAtUtc);
public sealed record ContainerRecycleRestoreSourcePage(IReadOnlyList<ContainerRecycleRestoreSourceSummary> Items, Guid? Next);
