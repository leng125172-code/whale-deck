namespace WhaleDeck.Application.Models;

public sealed record ContainerCopyCommandDto(ContainerFileLocationDto Source, string TargetPath, string ExpectedVersion);
public sealed record ContainerCopyPlanDto(uint SchemaVersion, ContainerCopyCommandDto Command, string Kind, string TargetProof,
    FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, FileParentIdentity SourceParent, FileParentIdentity TargetParent,
    string TargetParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint, FileMutationPlan? FilePlan, FileTreeCopyPlan? TreePlan)
{ public override string ToString() => nameof(ContainerCopyPlanDto); }
public sealed record ContainerCopyOperationDto(Guid Id, string Owner, ContainerCopyPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerCopyOperationDto); }
public sealed record ContainerCopyReceiptDto(Guid Id, string ContainerId, string InstanceId, string Kind, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool PrivateMayExist, bool Replayed);
