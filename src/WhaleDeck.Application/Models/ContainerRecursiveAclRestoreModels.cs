namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveAclRestoreCommandDto(ContainerFileLocationDto Location, string CaptureId, string ExpectedHistoryDigest, string ExpectedVersion);
public sealed record ContainerRecursiveAclRestoreFrameDto(ulong Device, ulong Inode, uint Mode, uint OwnerUid, uint OwnerGid, ulong LinkCount, long SizeBytes, long ModifiedAtUnixNs, long ChangedAtUnixNs);
public sealed record ContainerRecursiveAclRestorePlanDto(uint SchemaVersion, ContainerRecursiveAclRestoreCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    FileObjectIdentityDto RootIdentity, long RootChangedAtUnixNs, string ManifestDigest, uint EntryCount, uint FileCount, uint DirectoryCount, uint ChangeCount,
    ContainerRecursiveAclHistorySummaryDto RestoreSource, ContainerRecursiveAclRestoreFrameDto RestoreSourceFrame, string RestoreTargetsDigest)
{ public override string ToString() => nameof(ContainerRecursiveAclRestorePlanDto); }
public sealed record ContainerRecursiveAclRestoreOperationDto(Guid Id, string Owner, ContainerRecursiveAclRestorePlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecursiveAclRestoreOperationDto); }
public sealed record ContainerRecursiveAclRestoreReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint, string TargetProof,
    string State, bool BindingCurrent, bool Replayed, uint CompletedCount, uint ChangedCount, uint AbortedCount, uint NotStartedCount, uint UnknownCount,
    bool HistoryMayExist, bool HistoryVerified, string HistoryDigest, bool HistoryAvailable, string OutcomeDigest, string AclType);

