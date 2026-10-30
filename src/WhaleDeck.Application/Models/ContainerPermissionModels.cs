namespace WhaleDeck.Application.Models;

public sealed record ContainerPermissionCommandDto(ContainerFileLocationDto Location, string Mode, string ExpectedVersion);
public sealed record ContainerPermissionPlanDto(uint SchemaVersion, ContainerPermissionCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    string Kind, FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, string AttributesDigest, bool NoChange)
{ public override string ToString() => nameof(ContainerPermissionPlanDto); }
public sealed record ContainerPermissionOperationDto(Guid Id, string Owner, ContainerPermissionPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerPermissionOperationDto); }
public sealed record ContainerPermissionReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool Replayed, string Kind, bool? ModeConfirmed,
    bool NoChange, bool HistoryMayExist, bool HistoryVerified, string HistoryDigest);
