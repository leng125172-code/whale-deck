namespace WhaleDeck.Application.Models;

public sealed record ContainerCopyRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerCopyCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerCopyRequestBinding); }
public sealed record ContainerCopyPlanBinding(ContainerCopyRequestBinding Request, ContainerCopyPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerCopyPlanBinding); }
public sealed record ContainerCopyExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerCopyExecutionBinding); }
public sealed record ContainerCopyConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerCopyConfirmation); }
public sealed record ConfirmContainerCopyCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerCopyConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerCopyIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerCopyCommandDto Command,
    string State, string? Kind, int? EntryCount, long? TotalBytes, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode,
    long Version, string? PublishedVersion, bool PrivateMayExist, ContainerCopyConfirmationDto? Confirmation);
public sealed record AcknowledgeContainerCopyCommand(long Version, bool Confirmed);
