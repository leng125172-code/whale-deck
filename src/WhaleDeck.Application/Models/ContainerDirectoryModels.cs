namespace WhaleDeck.Application.Models;

public sealed record ContainerDirectoryCommandDto(ContainerFileLocationDto Location, string Mode);
public sealed record ContainerDirectoryPlanDto(uint SchemaVersion, ContainerDirectoryCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint)
{ public override string ToString() => nameof(ContainerDirectoryPlanDto); }
public sealed record ContainerDirectoryOperationDto(Guid Id, string Owner, ContainerDirectoryPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerDirectoryOperationDto); }
public sealed record ContainerDirectoryReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool Replayed);
