namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveAclRestoreRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerRecursiveAclRestoreCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreRequestBinding); }
public sealed record ContainerRecursiveAclRestorePlanBinding(ContainerRecursiveAclRestoreRequestBinding Request, ContainerRecursiveAclRestorePlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclRestorePlanBinding); }
public sealed record ContainerRecursiveAclRestoreExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreExecutionBinding); }
public sealed record ContainerRecursiveAclRestoreConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreConfirmation); }
public sealed record ConfirmContainerRecursiveAclRestoreCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerRecursiveAclRestoreCommand); }
public sealed record ContainerRecursiveAclRestoreConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreConfirmationDto); }
public sealed record ContainerRecursiveAclRestoreIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerRecursiveAclRestoreCommandDto Command,
    string State, uint? RootUid, uint? RootGid, uint? RootMode, uint? ExecutorUid, uint? ExecutorGid, uint? EntryCount, uint? FileCount, uint? DirectoryCount, uint? ChangeCount,
    string? ManifestDigest, ContainerRecursiveAclHistorySummaryDto? RestoreSource, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode, long Version,
    bool HistoryMayExist, bool HistoryVerified, string? HistoryDigest, ContainerRecursiveAclRestoreReceiptDto? Receipt, ContainerRecursiveAclRestoreConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreIntentDto); }
public sealed record AcknowledgeContainerRecursiveAclRestoreCommand(long Version, bool Confirmed);

public sealed record ContainerRecursiveAclRestoreReceiptBinding(ContainerRecursiveAclRestoreOperationDto Operation, ContainerRecursiveAclRestoreReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreReceiptBinding); }
