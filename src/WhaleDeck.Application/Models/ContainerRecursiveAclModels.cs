using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveAclCommandDto(ContainerFileLocationDto Location, string AclType, [property: JsonRequired] IReadOnlyList<ContainerAclEntryDto>? FileEntries, [property: JsonRequired] IReadOnlyList<ContainerAclEntryDto> DirectoryEntries, string ExpectedVersion);
public sealed record ContainerRecursiveAclPlanDto(uint SchemaVersion, ContainerRecursiveAclCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    FileObjectIdentityDto RootIdentity, long RootChangedAtUnixNs, string ManifestDigest, uint EntryCount, uint FileCount, uint DirectoryCount, uint ChangeCount)
{ public override string ToString() => nameof(ContainerRecursiveAclPlanDto); }
public sealed record ContainerRecursiveAclOperationDto(Guid Id, string Owner, ContainerRecursiveAclPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecursiveAclOperationDto); }
public sealed record ContainerRecursiveAclReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint, string TargetProof,
    string State, bool BindingCurrent, bool Replayed, uint CompletedCount, uint ChangedCount, uint AbortedCount, uint NotStartedCount, uint UnknownCount,
    bool HistoryMayExist, bool HistoryVerified, string HistoryDigest, bool HistoryAvailable, string OutcomeDigest, string AclType);

public sealed class ContainerRecursiveAclPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerRecursiveAclPreflightRejectedException(string code) : base("CONTAINER_RECURSIVE_ACL_PREFLIGHT_REJECTED")
    {
        if (code is not ("CONTAINER_RECURSIVE_ACL_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_ACL_PROTECTED" or
            "CONTAINER_RECURSIVE_ACL_TARGET_CHANGED" or "CONTAINER_RECURSIVE_ACL_TREE_LIMIT" or "CONTAINER_RECURSIVE_ACL_MANIFEST_LIMIT" or
            "CONTAINER_RECURSIVE_ACL_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECURSIVE_ACL_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_ACL_ATTRIBUTES_LIMIT" or "CONTAINER_RECURSIVE_ACL_MAPPING_UNPROVEN" or "CONTAINER_RECURSIVE_ACL_UNSUPPORTED"))
            throw new ArgumentException("CONTAINER_RECURSIVE_ACL_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
