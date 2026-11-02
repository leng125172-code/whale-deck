using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Models;

public sealed record ContainerRecursiveOwnershipCommandDto(ContainerFileLocationDto Location, [property: JsonRequired] uint Uid, [property: JsonRequired] uint Gid, string ExpectedVersion);
public sealed record ContainerRecursiveOwnershipPlanDto(uint SchemaVersion, ContainerRecursiveOwnershipCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    FileObjectIdentityDto RootIdentity, long RootChangedAtUnixNs, string ManifestDigest, uint EntryCount, uint FileCount, uint DirectoryCount, uint ChangeCount)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipPlanDto); }
public sealed record ContainerRecursiveOwnershipOperationDto(Guid Id, string Owner, ContainerRecursiveOwnershipPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecursiveOwnershipOperationDto); }
public sealed record ContainerRecursiveOwnershipReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint, string TargetProof,
    string State, bool BindingCurrent, bool Replayed, uint CompletedCount, uint ChangedCount, uint AbortedCount, uint NotStartedCount, uint UnknownCount,
    bool HistoryMayExist, bool HistoryVerified, string HistoryDigest, bool HistoryAvailable, string OutcomeDigest);

public sealed class ContainerRecursiveOwnershipPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerRecursiveOwnershipPreflightRejectedException(string code) : base("CONTAINER_RECURSIVE_OWNERSHIP_PREFLIGHT_REJECTED")
    {
        if (code is not ("CONTAINER_RECURSIVE_OWNERSHIP_VERSION_CONFLICT" or "CONTAINER_RECURSIVE_OWNERSHIP_PROTECTED" or
            "CONTAINER_RECURSIVE_OWNERSHIP_TARGET_CHANGED" or "CONTAINER_RECURSIVE_OWNERSHIP_TREE_LIMIT" or "CONTAINER_RECURSIVE_OWNERSHIP_MANIFEST_LIMIT" or
            "CONTAINER_RECURSIVE_OWNERSHIP_EXTENDED_METADATA_REQUIRED" or "CONTAINER_RECURSIVE_OWNERSHIP_SOURCE_UNSUPPORTED" or "CONTAINER_RECURSIVE_OWNERSHIP_ATTRIBUTES_LIMIT"))
            throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
