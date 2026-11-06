namespace WhaleDeck.Application.Models;

public sealed record ContainerRecyclePurgeSourceDto(Guid OperationId,string Owner,ContainerRecyclePlanDto Plan)
{ public override string ToString()=>nameof(ContainerRecyclePurgeSourceDto); }
public sealed record ContainerRecyclePurgeCommandDto(ContainerRecyclePurgeSourceDto Source,string HistoryDigest,string Scope)
{ public override string ToString()=>nameof(ContainerRecyclePurgeCommandDto); }
public sealed record ContainerRecyclePurgePlanDto(uint SchemaVersion,ContainerRecyclePurgeCommandDto Command,string TargetProof,
    FileParentIdentity ParentIdentity,string ParentVersion,uint OwnerUid,uint OwnerGid,FileParentIdentity PrivateIdentity,
    FileObjectIdentityDto RetainedIdentity,long RetainedChangedAtUnixNs,string SourceRecordDigest,string HistoryMetadataDigest,long HistoryCreatedAtUnixMs,
    string StorageHash,FileParentIdentity StoreIdentity,FileObjectIdentityDto ArchiveIdentity,long ArchiveChangedAtUnixNs,long ArchiveBytes,string ArchiveSha256,string Fingerprint)
{ public override string ToString()=>nameof(ContainerRecyclePurgePlanDto); }
public sealed record ContainerRecyclePurgeOperationDto(Guid Id,string Owner,ContainerRecyclePurgePlanDto Plan,ContainerFileWriteContextDto Context)
{ public override string ToString()=>nameof(ContainerRecyclePurgeOperationDto); }
public sealed record ContainerRecyclePurgeReceiptDto(Guid Id,Guid SourceOperationId,string Fingerprint,string State,bool SourceFenced,bool OwnedFence,bool? PurgeApplied);
