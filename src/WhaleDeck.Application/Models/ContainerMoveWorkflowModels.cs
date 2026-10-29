namespace WhaleDeck.Application.Models;

public sealed record ContainerMoveRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerMoveCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerMoveRequestBinding); }
public sealed record ContainerMovePlanBinding(ContainerMoveRequestBinding Request, ContainerMovePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerMovePlanBinding); }
public sealed record ContainerMoveExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerMoveExecutionBinding); }
public sealed record ContainerMoveConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerMoveConfirmation); }
public sealed record ConfirmContainerMoveCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerMoveConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerMoveIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerMoveCommandDto Command, string OperationKind,
    string State, string? Kind, int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode,
    long Version, string? PublishedVersion, bool PrivateMayExist, ContainerMoveConfirmationDto? Confirmation,
    bool? DestinationPublished, bool? SourceRemoved, bool? SourceRetained);
public sealed record AcknowledgeContainerMoveCommand(long Version, bool Confirmed);
