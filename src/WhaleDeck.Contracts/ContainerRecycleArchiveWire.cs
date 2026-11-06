using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class ContainerRecycleArchiveWire
{
    public static void RequireKnown(IMessage value)
    { try { ContainerRecycleRestoreWire.RequireKnown(value); } catch (ArgumentException) { throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_SCHEMA_INVALID"); } }
    public static object[] Identity(FileObjectIdentity i,long changed)=>[i.Device,i.Inode,i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount,i.SizeBytes,i.ModifiedAtUnixNs,changed];
    public static object Command(ContainerRecycleArchiveCommand c)=>new {purge=new {operationId=c.Purge.OperationId,owner=c.Purge.Owner,plan=ContainerRecyclePurgeWire.Plan(c.Purge.Plan)},historyDigest=c.HistoryDigest,mode=c.Mode};
    public static object Plan(ContainerRecycleArchivePlan p,bool omitFingerprint=false)
    {
        var v=new Dictionary<string,object?> {
            ["schemaVersion"]=p.SchemaVersion,["command"]=Command(p.Command),["targetProof"]=p.TargetProof,
            ["parentIdentity"]=new[]{p.ParentIdentity.Device,p.ParentIdentity.Inode},["parentVersion"]=p.ParentVersion,["uid"]=p.OwnerUid,["gid"]=p.OwnerGid,
            ["privateIdentity"]=p.PrivateIdentity is null?null:new[]{p.PrivateIdentity.Device,p.PrivateIdentity.Inode},["deletedIdentity"]=Identity(p.DeletedIdentity,p.DeletedChangedAtUnixNs),
            ["sourceRecordDigest"]=p.SourceRecordDigest,["purgeRecordDigest"]=p.PurgeRecordDigest,["purgeMarkerDigest"]=p.PurgeMarkerDigest,["historyMetadataDigest"]=p.HistoryMetadataDigest,
            ["storageHash"]=p.StorageHash,["storeIdentity"]=new[]{p.StoreIdentity.Device,p.StoreIdentity.Inode},["archiveIdentity"]=Identity(p.ArchiveIdentity,p.ArchiveChangedAtUnixNs),
            ["archiveBytes"]=p.ArchiveBytes,["archiveSha256"]=p.ArchiveSha256 };
        if(!omitFingerprint)v["fingerprint"]=p.Fingerprint;return v;
    }
    public static string Fingerprint(ContainerRecycleArchivePlan p)
    {
        var canonical=new StringBuilder();ContainerRecycleRestoreWire.Canonical(JsonSerializer.SerializeToElement(Plan(p,true)),canonical);
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false,true).GetBytes("whaledeck-container-recycle-archive-plan-v1\n"+canonical)));
    }
    public static void Receipt(ContainerRecycleArchiveRequest request,ContainerRecycleArchiveReceipt v)
    {
        if(request?.Plan?.Command?.Purge?.Plan?.Command?.Source?.Plan?.SourceIdentity is null||v is null)throw Invalid();
        try { RequireKnown(v); } catch(ArgumentException) { throw Invalid(); }
        var p=request.Plan;var purge=p.Command.Purge;var original=purge.Plan.Command.Source.Plan.SourceIdentity;
        if(v.OperationId!=request.OperationId||v.SourceOperationId!=purge.Plan.Command.Source.OperationId||v.PurgeOperationId!=purge.OperationId||v.Fingerprint!=p.Fingerprint||
           v.State is not ("Reserved" or "Completed" or "Aborted" or "Unknown")||v.OwnedFence&&!v.SourceFenced||
           v.State=="Reserved"&&(!v.SourceFenced||!v.OwnedFence||!v.HasArchiveCopied||v.ArchiveCopied)||
           v.State=="Aborted"&&(v.OwnedFence||!v.HasArchiveCopied||v.ArchiveCopied)||
           v.State=="Unknown"&&(!v.SourceFenced||v.HasArchiveCopied)||
           // Completed proves historical publication from its own durable
           // journal. A later foreign claim must not erase that result; it
           // must prevent any claim of a current verified binding instead.
           v.State=="Completed"&&(!v.SourceFenced||!v.HasArchiveCopied||!v.ArchiveCopied)||
           v.BindingCurrent&&(v.State!="Completed"||!v.OwnedFence)||
           (v.PublishedIdentity is not null)!=(v.State=="Completed")||v.HasPublishedChangedAtUnixNs!=(v.State=="Completed"))throw Invalid();
        if(v.State=="Completed")
        {
            var i=v.PublishedIdentity;
            if(i is not {Inode:>0,LinkCount:1}||i.Device!=p.ParentIdentity.Device||i.Device==original.Device&&i.Inode==original.Inode||i.Device==p.ArchiveIdentity.Device&&i.Inode==p.ArchiveIdentity.Inode||
               i.Mode!=original.Mode||i.OwnerUid!=original.OwnerUid||i.OwnerGid!=original.OwnerGid||i.SizeBytes!=original.SizeBytes||i.ModifiedAtUnixNs!=original.ModifiedAtUnixNs||v.PublishedChangedAtUnixNs<0)throw Invalid();
        }
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECYCLE_ARCHIVE_OUTPUT_INVALID");
}
