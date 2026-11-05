namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerRecycleCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleRequestBinding); }
public sealed record ContainerRecyclePlanBinding(ContainerRecycleRequestBinding Request, ContainerRecyclePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecyclePlanBinding); }
public sealed record ContainerRecycleExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleExecutionBinding); }
public sealed record ContainerRecycleConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecycleConfirmation); }
public sealed record ConfirmContainerRecycleCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecycleCommand); }
public sealed record ContainerRecycleConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecycleConfirmationDto); }
public sealed record ContainerRecycleIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerRecycleCommandDto Command,
    string State, uint? OriginalUid, uint? OriginalGid, uint? OriginalMode, uint? ExecutorUid, uint? ExecutorGid,
    long? SizeBytes, string? Sha256, int? AttributeCount, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryMayExist, bool HistoryVerified, string? HistoryDigest, ContainerRecycleReceiptDto? Receipt, ContainerRecycleConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecycleIntentDto); }
public sealed record AcknowledgeContainerRecycleCommand(long Version, bool Confirmed);

public sealed record ContainerRecycleReceiptBinding(ContainerRecycleOperationDto Operation, ContainerRecycleReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecycleReceiptBinding); }
