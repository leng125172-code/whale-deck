namespace WhaleDeck.Application.Models;

public sealed record ContainerRecycleArchiveSourceDto(Guid OperationId,string Owner,ContainerRecyclePurgePlanDto Plan)
{ public override string ToString()=>nameof(ContainerRecycleArchiveSourceDto); }
public sealed record ContainerRecycleArchiveCommandDto(ContainerRecycleArchiveSourceDto Purge,string HistoryDigest,string Mode)
{ public override string ToString()=>nameof(ContainerRecycleArchiveCommandDto); }
public sealed record ContainerRecycleArchivePlanDto(uint SchemaVersion,ContainerRecycleArchiveCommandDto Command,string TargetProof,
    FileParentIdentity ParentIdentity,string ParentVersion,uint OwnerUid,uint OwnerGid,FileParentIdentity? PrivateIdentity,
    FileObjectIdentityDto DeletedIdentity,long DeletedChangedAtUnixNs,string SourceRecordDigest,string PurgeRecordDigest,string PurgeMarkerDigest,string HistoryMetadataDigest,
    string StorageHash,FileParentIdentity StoreIdentity,FileObjectIdentityDto ArchiveIdentity,long ArchiveChangedAtUnixNs,long ArchiveBytes,string ArchiveSha256,string Fingerprint)
{ public override string ToString()=>nameof(ContainerRecycleArchivePlanDto); }
public sealed record ContainerRecycleArchiveOperationDto(Guid Id,string Owner,ContainerRecycleArchivePlanDto Plan,ContainerFileWriteContextDto Context)
{ public override string ToString()=>nameof(ContainerRecycleArchiveOperationDto); }
public sealed record ContainerRecycleArchiveReceiptDto(Guid Id,Guid SourceOperationId,Guid PurgeOperationId,string Fingerprint,string State,
    bool SourceFenced,bool OwnedFence,bool? ArchiveCopied,bool? BindingCurrent,FileObjectIdentityDto? PublishedIdentity,long? PublishedChangedAtUnixNs);
