namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveAclRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerRecursiveAclCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclRequestBinding); }
public sealed record ContainerRecursiveAclPlanBinding(ContainerRecursiveAclRequestBinding Request, ContainerRecursiveAclPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclPlanBinding); }
public sealed record ContainerRecursiveAclExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclExecutionBinding); }
public sealed record ContainerRecursiveAclConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclConfirmation); }
public sealed record ConfirmContainerRecursiveAclCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecursiveAclCommand); }
public sealed record ContainerRecursiveAclConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecursiveAclConfirmationDto); }
public sealed record ContainerRecursiveAclIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerRecursiveAclCommandDto Command,
    string State, uint? RootUid, uint? RootGid, uint? RootMode, uint? ExecutorUid, uint? ExecutorGid, uint? EntryCount, uint? FileCount, uint? DirectoryCount, uint? ChangeCount,
    string? ManifestDigest, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryMayExist, bool HistoryVerified, string? HistoryDigest, ContainerRecursiveAclReceiptDto? Receipt, ContainerRecursiveAclConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecursiveAclIntentDto); }
public sealed record AcknowledgeContainerRecursiveAclCommand(long Version, bool Confirmed);

public sealed record ContainerRecursiveAclReceiptBinding(ContainerRecursiveAclOperationDto Operation, ContainerRecursiveAclReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecursiveAclReceiptBinding); }
