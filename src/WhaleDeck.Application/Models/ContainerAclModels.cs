namespace WhaleDeck.Application.Models;

public sealed record ContainerAclEntryDto(string Tag, uint? Id, uint Permissions);
public sealed record ContainerAclCommandDto(ContainerFileLocationDto Location, string AclType, IReadOnlyList<ContainerAclEntryDto> Entries, string ExpectedVersion);
public sealed record ContainerAclPlanDto(uint SchemaVersion, ContainerAclCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    string Kind, FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, string AttributesDigest, bool NoChange,
    IReadOnlyList<ContainerAclEntryDto> AccessAcl, IReadOnlyList<ContainerAclEntryDto> DefaultAcl, uint TargetMode)
{ public override string ToString() => nameof(ContainerAclPlanDto); }
public sealed record ContainerAclOperationDto(Guid Id, string Owner, ContainerAclPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerAclOperationDto); }
public sealed record ContainerAclReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool Replayed, string Kind, bool? AclConfirmed,
    bool NoChange, bool HistoryMayExist, bool HistoryVerified, string HistoryDigest, string AclType, uint TargetMode);

public sealed class ContainerAclPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerAclPreflightRejectedException(string code) : base("Container ACL preflight was refused.")
    {
        if (code is not ("CONTAINER_ACL_VERSION_CONFLICT" or "CONTAINER_ACL_SOURCE_UNSUPPORTED" or "CONTAINER_ACL_EXTENDED_METADATA_REQUIRED" or
            "CONTAINER_ACL_PROTECTED" or "CONTAINER_ACL_TARGET_CHANGED" or "CONTAINER_ACL_ATTRIBUTES_LIMIT" or
            "CONTAINER_ACL_UNSUPPORTED" or "CONTAINER_ACL_MAPPING_UNPROVEN" or "CONTAINER_ACL_DEFAULT_REQUIRES_DIRECTORY")) throw new ArgumentException("CONTAINER_ACL_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
