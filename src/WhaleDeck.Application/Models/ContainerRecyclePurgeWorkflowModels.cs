namespace WhaleDeck.Application.Models;

public sealed record ContainerRecyclePurgeRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, Guid SourceIntentId, string SourceProofHash, ContainerRecyclePurgeCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecyclePurgeRequestBinding); }
public sealed record ContainerRecyclePurgePlanBinding(ContainerRecyclePurgeRequestBinding Request, ContainerRecyclePurgePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecyclePurgePlanBinding); }
public sealed record ContainerRecyclePurgeExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecyclePurgeExecutionBinding); }
public sealed record ContainerRecyclePurgeConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecyclePurgeConfirmation); }
public sealed record ConfirmContainerRecyclePurgeCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecyclePurgeCommand); }
public sealed record ContainerRecyclePurgeConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecyclePurgeConfirmationDto); }
public sealed record ContainerRecyclePurgeIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, Guid SourceIntentId, Guid SourceOperationId, string SourceHistoryDigest,
    ContainerFileLocationDto Location, string State, uint? OriginalUid, uint? OriginalGid, uint? OriginalMode, uint? ExecutorUid, uint? ExecutorGid,
    long SizeBytes, string Sha256, int AttributeCount, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool ReservationRecorded, bool? PurgeApplied, ContainerRecyclePurgeReceiptDto? Receipt, ContainerRecyclePurgeConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecyclePurgeIntentDto); }
public sealed record AcknowledgeContainerRecyclePurgeCommand(long Version, bool Confirmed);

public sealed record ContainerRecyclePurgeReceiptBinding(ContainerRecyclePurgeOperationDto Operation, ContainerRecyclePurgeReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecyclePurgeReceiptBinding); }

public sealed record ContainerRecyclePurgeSourceBinding(Guid IntentId, string RequestHash, string PlanHash, string ExecutionHash, string ReceiptHash,
    ContainerRecyclePurgeCommandDto Command)
{ public override string ToString() => nameof(ContainerRecyclePurgeSourceBinding); }
public sealed record ResolvedContainerRecyclePurgeSource(Guid IntentId, string ProofHash, ContainerRecyclePurgeCommandDto Command)
{ public override string ToString() => nameof(ResolvedContainerRecyclePurgeSource); }

public sealed record ContainerRecyclePurgeSourceSummary(Guid SourceIntentId, Guid SourceOperationId, ContainerFileLocationDto Location,
    uint OriginalUid, uint OriginalGid, uint OriginalMode, long SizeBytes, string Sha256, int AttributeCount, string HistoryDigest, DateTimeOffset CompletedAtUtc);
public sealed record ContainerRecyclePurgeSourcePage(IReadOnlyList<ContainerRecyclePurgeSourceSummary> Items, Guid? Next);
