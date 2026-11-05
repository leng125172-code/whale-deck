namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleRestoreSourceDto(Guid OperationId, string Owner, ContainerRecyclePlanDto Plan)
{ public override string ToString() => nameof(ContainerRecycleRestoreSourceDto); }
public sealed record ContainerRecycleRestoreCommandDto(ContainerRecycleRestoreSourceDto Source, string HistoryDigest)
{ public override string ToString() => nameof(ContainerRecycleRestoreCommandDto); }
public sealed record ContainerRecycleRestorePlanDto(uint SchemaVersion, ContainerRecycleRestoreCommandDto Command, string TargetProof,
    FileParentIdentity ParentIdentity, string ParentVersion, uint OwnerUid, uint OwnerGid, FileParentIdentity PrivateIdentity,
    FileObjectIdentityDto RetainedIdentity, long RetainedChangedAtUnixNs, string Fingerprint)
{ public override string ToString() => nameof(ContainerRecycleRestorePlanDto); }
public sealed record ContainerRecycleRestoreOperationDto(Guid Id, string Owner, ContainerRecycleRestorePlanDto Plan, ContainerFileWriteContextDto Context)
{ public override string ToString() => nameof(ContainerRecycleRestoreOperationDto); }
public sealed record ContainerRecycleRestoreReceiptDto(Guid Id, Guid SourceOperationId, string Fingerprint, string TargetProof, string State,
    bool BindingCurrent, bool Replayed, bool HistoryVerified, string HistoryDigest, bool HistoryAvailable,
    string? SourceState, bool? SourceRetained, bool? SourceRestored, bool? RestoredByThisOperation);

/// <summary>Read-only planning refusals; never proof that an executing restore made no change.</summary>
public sealed class ContainerRecycleRestorePreflightRejectedException : Exception
{
    public string Code { get; }
    public ContainerRecycleRestorePreflightRejectedException(string code) : base("Container recycle restore preflight was refused.")
    {
        if(code is not ("CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED" or "CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY" or "CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS" or
            "CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED" or "CONTAINER_RECYCLE_RESTORE_PROTECTED" or "CONTAINER_RECYCLE_RESTORE_HISTORY_UNAVAILABLE"))
            throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PREFLIGHT_CODE_INVALID");
        Code=code;
    }
}
