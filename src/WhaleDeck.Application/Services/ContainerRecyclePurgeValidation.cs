using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecyclePurgeValidation
{
    public static void Command(ContainerRecyclePurgeCommandDto? value)
    {
        if(value?.Source is not {OperationId:var id} source||id==Guid.Empty||value.Scope!="Originals")throw new ArgumentException("CONTAINER_RECYCLE_PURGE_COMMAND_REQUIRED");
        ContainerFileValidation.Hash(source.Owner);ContainerRecycleValidation.Plan(source.Plan);ContainerFileValidation.Hash(value.HistoryDigest);
    }
    public static void Plan(ContainerRecyclePurgePlanDto? value)
    {
        if(value is null||value.SchemaVersion!=1)throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");
        Command(value.Command);var original=value.Command.Source.Plan;
        if(value.ParentIdentity!=original.ParentIdentity||value.TargetProof!=original.TargetProof||value.OwnerUid!=original.OwnerUid||value.OwnerGid!=original.OwnerGid||
           value.PrivateIdentity is not {Inode:>0}||value.StoreIdentity is not {Inode:>0}||value.RetainedIdentity!=original.SourceIdentity||value.RetainedChangedAtUnixNs<original.SourceChangedAtUnixNs||
           value.ArchiveIdentity is not {Inode:>0,Mode:33152,OwnerUid:0,OwnerGid:0,LinkCount:1} archive||archive.SizeBytes!=original.SourceIdentity.SizeBytes||value.ArchiveBytes!=archive.SizeBytes||value.ArchiveSha256!=original.Sha256||value.ArchiveChangedAtUnixNs<0||value.HistoryCreatedAtUnixMs is <0 or >253402300799999)
            throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");
        foreach(var hash in new[]{value.ParentVersion,value.SourceRecordDigest,value.HistoryMetadataDigest,value.StorageHash,value.ArchiveSha256,value.Fingerprint})ContainerFileValidation.Hash(hash);
    }
    public static void Operation(ContainerRecyclePurgeOperationDto? value)
    {
        if(value is null)throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONTEXT_INVALID");Plan(value.Plan);
        if(value.Id==value.Plan.Command.Source.OperationId)throw new ArgumentException("CONTAINER_RECYCLE_PURGE_NEW_OPERATION_REQUIRED");
        ContainerRecycleValidation.Operation(new(value.Id,value.Owner,value.Plan.Command.Source.Plan,value.Context));
    }
    public static void Receipt(ContainerRecyclePurgeOperationDto operation,ContainerRecyclePurgeReceiptDto value)
    {
        if(value is null||value.Id!=operation.Id||value.SourceOperationId!=operation.Plan.Command.Source.OperationId||value.Fingerprint!=operation.Plan.Fingerprint||
           value.State is not ("Reserved" or "Completed" or "Aborted" or "Unknown")||value.OwnedFence&&!value.SourceFenced||
           value.State=="Reserved"&&(!value.OwnedFence||!value.SourceFenced||value.PurgeApplied!=false)||
           value.State=="Completed"&&(!value.OwnedFence||!value.SourceFenced||value.PurgeApplied!=true)||
           value.State=="Aborted"&&(value.OwnedFence||value.PurgeApplied!=false)||
           value.State=="Unknown"&&(!value.SourceFenced||value.PurgeApplied is not null))throw Invalid();
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECYCLE_PURGE_OUTPUT_INVALID");
}
