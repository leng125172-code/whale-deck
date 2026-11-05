namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleAttributeDto(string Name, byte[] Value)
{ public override string ToString() => nameof(ContainerRecycleAttributeDto); }
public sealed record ContainerRecycleCommandDto(ContainerFileLocationDto Location, string ExpectedVersion);
public sealed record ContainerRecyclePlanDto(uint SchemaVersion, ContainerRecycleCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, string Sha256, IReadOnlyList<ContainerRecycleAttributeDto> Attributes)
{ public override string ToString() => nameof(ContainerRecyclePlanDto); }
public sealed record ContainerRecycleOperationDto(Guid Id, string Owner, ContainerRecyclePlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecycleOperationDto); }
public sealed record ContainerRecycleReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint, string TargetProof,
    string State, bool BindingCurrent, bool Replayed, bool HistoryMayExist, bool HistoryVerified, string HistoryDigest,
    bool HistoryAvailable, bool? SourceRetained, bool? SourceRestored);
