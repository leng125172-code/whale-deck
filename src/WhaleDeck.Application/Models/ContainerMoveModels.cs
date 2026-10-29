namespace WhaleDeck.Application.Models;

public sealed record ContainerMoveCommandDto(ContainerFileLocationDto Source, string TargetPath, string ExpectedVersion);
public sealed record ContainerMovePlanDto(uint SchemaVersion, ContainerMoveCommandDto Command, string OperationKind, string Kind, string TargetProof,
    FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, FileParentIdentity SourceParent, FileParentIdentity TargetParent,
    string TargetParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint, FileMutationPlan? FilePlan, FileTreeMovePlan? TreePlan)
{ public override string ToString() => nameof(ContainerMovePlanDto); }
public sealed record ContainerMoveOperationDto(Guid Id, string Owner, ContainerMovePlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerMoveOperationDto); }
public sealed record ContainerMoveReceiptDto(Guid Id, string ContainerId, string InstanceId, string OperationKind, string Kind, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool PrivateMayExist, bool Replayed, bool? DestinationPublished, bool? SourceRemoved, bool? SourceRetained);
