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


/// <summary>Finite Agent refusal; a source fence may have fired after partial changes.</summary>
public sealed class ContainerRecursiveAclRestorePreflightRejectedException : Exception
{
    public string Code { get; }
    public bool IsSourceRejection => Code is "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED" or
        "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_TREE_MISMATCH";
    public ContainerRecursiveAclRestorePreflightRejectedException(string code) : base("CONTAINER_RECURSIVE_ACL_RESTORE_PREFLIGHT_REJECTED")
    {
        if (code is not ("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID" or "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_TREE_MISMATCH" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_ACL_RESTORE_PROTECTED" or "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_TREE_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_MANIFEST_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_EXTENDED_METADATA_REQUIRED" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_ACL_RESTORE_ATTRIBUTES_LIMIT" or "CONTAINER_RECURSIVE_ACL_RESTORE_MAPPING_UNPROVEN" or
            "CONTAINER_RECURSIVE_ACL_RESTORE_UNSUPPORTED" or "CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_INVALID"))
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
