namespace WhaleDeck.Application.Models;

public sealed record ContainerOwnershipCommandDto(ContainerFileLocationDto Location, uint Uid, uint Gid, string ExpectedVersion);
public sealed record ContainerOwnershipPlanDto(uint SchemaVersion, ContainerOwnershipCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, string Fingerprint,
    string Kind, FileObjectIdentityDto SourceIdentity, long SourceChangedAtUnixNs, string AttributesDigest, bool NoChange)
{ public override string ToString() => nameof(ContainerOwnershipPlanDto); }
public sealed record ContainerOwnershipOperationDto(Guid Id, string Owner, ContainerOwnershipPlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerOwnershipOperationDto); }
public sealed record ContainerOwnershipReceiptDto(Guid Id, string ContainerId, string InstanceId, string Fingerprint,
    string TargetProof, string State, string Version, bool BindingCurrent, bool Replayed, string Kind, bool? OwnerConfirmed,
    bool NoChange, bool HistoryMayExist, bool HistoryVerified, string HistoryDigest);

/// <summary>Only explicit non-transient preflight refusals. Never used to infer execution outcome.</summary>
public sealed class ContainerOwnershipPreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerOwnershipPreflightRejectedException(string code) : base("Container ownership preflight was refused.")
    {
        if (code is not ("CONTAINER_OWNERSHIP_VERSION_CONFLICT" or "CONTAINER_OWNERSHIP_SOURCE_UNSUPPORTED" or
            "CONTAINER_OWNERSHIP_EXTENDED_METADATA_REQUIRED" or "CONTAINER_OWNERSHIP_PROTECTED" or
            "CONTAINER_OWNERSHIP_TARGET_CHANGED" or "CONTAINER_OWNERSHIP_ATTRIBUTES_LIMIT")) throw new ArgumentException("CONTAINER_OWNERSHIP_PREFLIGHT_CODE_INVALID");
        Code = code;
    }
}
