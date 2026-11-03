namespace WhaleDeck.Application.Models;

public sealed record ContainerAclRequestBinding(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerAclCommandDto Command, DateTimeOffset CreatedAtUtc, DateTimeOffset PlanningExpiresAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ContainerAclHistorySnapshotDto? RestoreSource = null)
{ public override string ToString() => nameof(ContainerAclRequestBinding); }
public sealed record ContainerAclPlanBinding(ContainerAclRequestBinding Request, ContainerAclPlanDto Plan, DateTimeOffset ReadyExpiresAtUtc)
{ public override string ToString() => nameof(ContainerAclPlanBinding); }
public sealed record ContainerAclExecutionBinding(Guid Id, string Actor, string LoginBinding, string PlanHash, Guid ExecutionJobId,
    DateTimeOffset ConfirmedAtUtc, DateTimeOffset ExecutionExpiresAtUtc)
{ public override string ToString() => nameof(ContainerAclExecutionBinding); }
public sealed record ContainerAclConfirmation(Guid Id, string Actor, string LoginBinding, string PlanHash, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerAclConfirmation); }
public sealed record ConfirmContainerAclCommand(string PlanHash, bool Confirmed, long Version)
{ public override string ToString() => nameof(ConfirmContainerAclCommand); }
public sealed record ContainerAclConfirmationDto(string PlanHash, DateTimeOffset ExpiresAtUtc, string Warning)
{ public override string ToString() => nameof(ContainerAclConfirmationDto); }
public sealed record ContainerAclIntentDto(Guid Id, Guid PlanningJobId, Guid? ExecutionJobId, ContainerAclCommandDto Command,
    string State, string? Kind, uint? OriginalUid, uint? OriginalGid, uint? ExecutorUid, uint? ExecutorGid, IReadOnlyList<ContainerAclEntryDto>? OriginalAccessAcl, IReadOnlyList<ContainerAclEntryDto>? OriginalDefaultAcl, uint? TargetMode, bool? NoChange, DateTimeOffset? ReadyExpiresAtUtc, string? ResultCode,
    long Version, string? PublishedVersion, bool? AclConfirmed, bool HistoryMayExist, bool HistoryVerified,
    string? HistoryDigest, ContainerAclReceiptDto? Receipt, ContainerAclConfirmationDto? Confirmation)
{ public override string ToString() => nameof(ContainerAclIntentDto); }
public sealed record AcknowledgeContainerAclCommand(long Version, bool Confirmed);

public sealed record ContainerAclReceiptBinding(ContainerAclOperationDto Operation, ContainerAclReceiptDto Receipt)
{ public override string ToString() => nameof(ContainerAclReceiptBinding); }
