namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursivePermissionCommandDto(ContainerFileLocationDto Location, string FileMode, string DirectoryMode, string ExpectedVersion);
public sealed record ContainerRecursivePermissionPlanDto(uint SchemaVersion, ContainerRecursivePermissionCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    FileObjectIdentityDto RootIdentity, long RootChangedAtUnixNs, string ManifestDigest, uint EntryCount, uint FileCount, uint DirectoryCount, uint ChangeCount)
{ public override string ToString() => nameof(ContainerRecursivePermissionPlanDto); }
public sealed record ContainerRecursivePermissionOperationDto(Guid Id, string Owner, ContainerRecursivePermissionPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecursivePermissionOperationDto); }
public sealed record ContainerRecursivePermissionReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint, string TargetProof,
    string State, bool BindingCurrent, bool Replayed, uint CompletedCount, uint ChangedCount, uint AbortedCount, uint NotStartedCount, uint UnknownCount,
    bool HistoryMayExist, bool HistoryVerified, string HistoryDigest, bool HistoryAvailable, string OutcomeDigest);
