namespace WhaleDeck.Application.Models;

public sealed record ContainerDirectoryCommandDto(ContainerFileLocationDto Location, string Mode);
public sealed record ContainerDirectoryPlanDto(uint SchemaVersion, ContainerDirectoryCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint)
{ public override string ToString() => nameof(ContainerDirectoryPlanDto); }
public sealed record ContainerDirectoryOperationDto(Guid Id, string Owner, ContainerDirectoryPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerDirectoryOperationDto); }
public sealed record ContainerDirectoryReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool Replayed);
public sealed record ContainerDirectoryPlanClaims(string Actor, string LoginBinding, ContainerDirectoryPlanDto Plan, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerDirectoryPlanClaims); }
public sealed record ContainerDirectoryBindingDto(Guid Id, Guid IdempotencyKey, string Actor, string LoginBinding, string TraceId,
    string Owner, ContainerDirectoryPlanDto Plan, string PlanHash, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{ public override string ToString() => nameof(ContainerDirectoryBindingDto); }
public sealed record ContainerDirectoryPlanResponse(string PlanHash, DateTimeOffset ExpiresAtUtc, ContainerDirectoryCommandDto Command, string Warning);
public sealed record SubmitContainerDirectoryCommand(string PlanHash, bool Confirmed, string IdempotencyKey);
public sealed record ContainerDirectoryIntentDto(Guid Id, Guid JobId, ContainerDirectoryCommandDto Command, string State,
    DateTimeOffset ExpiresAtUtc, string? ResultCode, long Version, string? PublishedVersion);
public sealed record AcknowledgeContainerDirectoryCommand(long Version, bool Confirmed);
