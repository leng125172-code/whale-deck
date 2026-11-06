using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerRecycleArchiveValidation
{
    public static void Command(ContainerRecycleArchiveCommandDto? c)
    {
        if(c?.Purge is null||c.Purge.OperationId==Guid.Empty||c.Mode!="ArchiveCopy")throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_COMMAND_REQUIRED");
        ContainerFileValidation.Hash(c.Purge.Owner);ContainerRecyclePurgeValidation.Plan(c.Purge.Plan);
        if(c.Purge.OperationId==c.Purge.Plan.Command.Source.OperationId||c.HistoryDigest!=c.Purge.Plan.Command.HistoryDigest)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_COMMAND_INVALID");
    }
    public static void Plan(ContainerRecycleArchivePlanDto? p)
    {
        if(p is null||p.SchemaVersion!=1)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");Command(p.Command);var old=p.Command.Purge.Plan;
        if(p.TargetProof!=old.TargetProof||p.ParentIdentity!=old.ParentIdentity||p.OwnerUid!=old.OwnerUid||p.OwnerGid!=old.OwnerGid||
           p.PrivateIdentity is not null&&p.PrivateIdentity!=old.PrivateIdentity||p.DeletedIdentity!=old.RetainedIdentity||p.DeletedChangedAtUnixNs!=old.RetainedChangedAtUnixNs||
           p.StoreIdentity!=old.StoreIdentity||p.StorageHash!=old.StorageHash||p.ArchiveIdentity!=old.ArchiveIdentity||p.ArchiveChangedAtUnixNs!=old.ArchiveChangedAtUnixNs||p.ArchiveBytes!=old.ArchiveBytes||p.ArchiveSha256!=old.ArchiveSha256)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");
        foreach(var hash in new[]{p.ParentVersion,p.SourceRecordDigest,p.PurgeRecordDigest,p.PurgeMarkerDigest,p.HistoryMetadataDigest,p.Fingerprint})ContainerFileValidation.Hash(hash);
    }
    public static void Operation(ContainerRecycleArchiveOperationDto? value)
    {
        if(value is null)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONTEXT_INVALID");Plan(value.Plan);
        if(value.Id==value.Plan.Command.Purge.OperationId||value.Id==value.Plan.Command.Purge.Plan.Command.Source.OperationId)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_NEW_OPERATION_REQUIRED");
        ContainerRecycleValidation.Operation(new(value.Id,value.Owner,value.Plan.Command.Purge.Plan.Command.Source.Plan,value.Context));
    }
    public static void Receipt(ContainerRecycleArchiveOperationDto op,ContainerRecycleArchiveReceiptDto v)
    {
        // Completed is historical. A conflicting current claim disallows
        // BindingCurrent=true, not the old durable copy result itself.
        if(v is null||v.Id!=op.Id||v.SourceOperationId!=op.Plan.Command.Purge.Plan.Command.Source.OperationId||v.PurgeOperationId!=op.Plan.Command.Purge.OperationId||v.Fingerprint!=op.Plan.Fingerprint||
           v.State is not ("Reserved" or "Completed" or "Aborted" or "Unknown")||v.OwnedFence&&!v.SourceFenced||
           v.State=="Reserved"&&(!v.OwnedFence||!v.SourceFenced||v.ArchiveCopied!=false)||v.State=="Aborted"&&(v.OwnedFence||v.ArchiveCopied!=false)||
           v.State=="Unknown"&&(!v.SourceFenced||v.ArchiveCopied is not null)||v.State=="Completed"&&(!v.SourceFenced||v.ArchiveCopied!=true)||
           v.BindingCurrent==true&&(v.State!="Completed"||!v.OwnedFence)||
           (v.PublishedIdentity is not null)!=(v.State=="Completed")||v.PublishedChangedAtUnixNs.HasValue!=(v.State=="Completed"))throw Invalid();
        if(v.State=="Completed")
        {
            var i=v.PublishedIdentity!;var original=op.Plan.Command.Purge.Plan.Command.Source.Plan.SourceIdentity;var archive=op.Plan.ArchiveIdentity;
            if(i.Inode==0||i.Device!=op.Plan.ParentIdentity.Device||i.Device==original.Device&&i.Inode==original.Inode||i.Device==archive.Device&&i.Inode==archive.Inode||
               i.Mode!=original.Mode||i.OwnerUid!=original.OwnerUid||i.OwnerGid!=original.OwnerGid||i.LinkCount!=1||i.SizeBytes!=original.SizeBytes||i.ModifiedAtUnixNs!=original.ModifiedAtUnixNs||v.PublishedChangedAtUnixNs<0)throw Invalid();
        }
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECYCLE_ARCHIVE_OUTPUT_INVALID");
}
