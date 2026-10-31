namespace WhaleDeck.Application.Models;

public sealed record ContainerOwnershipRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerOwnershipCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerOwnershipRequestBinding); }
public sealed record ContainerOwnershipPlanBinding(ContainerOwnershipRequestBinding Request, ContainerOwnershipPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerOwnershipPlanBinding); }
public sealed record ContainerOwnershipExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerOwnershipExecutionBinding); }
public sealed record ContainerOwnershipConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerOwnershipConfirmation); }
public sealed record ConfirmContainerOwnershipCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerOwnershipConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerOwnershipIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerOwnershipCommandDto Command,
    string State, string? Kind, uint? OriginalUid, uint? OriginalGid, uint? ExecutorUid, uint? ExecutorGid, bool? NoChange, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode,
    long Version, string? PublishedVersion, bool? OwnerConfirmed, bool HistoryMayExist, bool HistoryVerified,
    string? HistoryDigest, ContainerOwnershipConfirmationDto? Confirmation);
public sealed record AcknowledgeContainerOwnershipCommand(long Version, bool Confirmed);
