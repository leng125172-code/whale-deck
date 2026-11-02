namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveOwnershipRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerRecursiveOwnershipCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipRequestBinding); }
public sealed record ContainerRecursiveOwnershipPlanBinding(ContainerRecursiveOwnershipRequestBinding Request, ContainerRecursiveOwnershipPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipPlanBinding); }
public sealed record ContainerRecursiveOwnershipExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipExecutionBinding); }
public sealed record ContainerRecursiveOwnershipConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipConfirmation); }
public sealed record ConfirmContainerRecursiveOwnershipCommand(string PlanHash, bool Confirmed, long Version);
public sealed record ContainerRecursiveOwnershipConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning);
public sealed record ContainerRecursiveOwnershipIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerRecursiveOwnershipCommandDto Command,
    string State, uint? RootUid, uint? RootGid, uint? ExecutorUid, uint? ExecutorGid, uint? EntryCount, uint? FileCount, uint? DirectoryCount, uint? ChangeCount,
    string? ManifestDigest, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryMayExist, bool HistoryVerified, string? HistoryDigest, ContainerRecursiveOwnershipReceiptDto? Receipt, ContainerRecursiveOwnershipConfirmationDto? Confirmation);
public sealed record AcknowledgeContainerRecursiveOwnershipCommand(long Version, bool Confirmed);
