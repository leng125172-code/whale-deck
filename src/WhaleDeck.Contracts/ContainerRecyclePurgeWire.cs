using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class ContainerRecyclePurgeWire
{
    public static void RequireKnown(IMessage value)
    {try{ContainerRecycleRestoreWire.RequireKnown(value);}catch(ArgumentException){throw new ArgumentException("CONTAINER_RECYCLE_PURGE_SCHEMA_INVALID");}}
    private static object[] Identity(FileObjectIdentity i, long changed) =>
        [i.Device,i.Inode,i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount,i.SizeBytes,i.ModifiedAtUnixNs,changed];
    public static object Command(ContainerRecyclePurgeCommand c) => new
    { source=new {operationId=c.Source.OperationId,owner=c.Source.Owner,plan=ContainerRecycleRestoreWire.SourcePlan(c.Source.Plan)},historyDigest=c.HistoryDigest,scope=c.Scope };
    public static object Plan(ContainerRecyclePurgePlan p,bool omitFingerprint=false)
    {
        var value=new Dictionary<string,object> {
            ["schemaVersion"]=p.SchemaVersion,["command"]=Command(p.Command),["targetProof"]=p.TargetProof,
            ["parentIdentity"]=new[]{p.ParentIdentity.Device,p.ParentIdentity.Inode},["parentVersion"]=p.ParentVersion,["uid"]=p.OwnerUid,["gid"]=p.OwnerGid,
            ["privateIdentity"]=new[]{p.PrivateIdentity.Device,p.PrivateIdentity.Inode},["retainedIdentity"]=Identity(p.RetainedIdentity,p.RetainedChangedAtUnixNs),
            ["sourceRecordDigest"]=p.SourceRecordDigest,["historyMetadataDigest"]=p.HistoryMetadataDigest,["historyCreatedAtUnixMs"]=p.HistoryCreatedAtUnixMs,
            ["storageHash"]=p.StorageHash,["storeIdentity"]=new[]{p.StoreIdentity.Device,p.StoreIdentity.Inode},["archiveIdentity"]=Identity(p.ArchiveIdentity,p.ArchiveChangedAtUnixNs),
            ["archiveBytes"]=p.ArchiveBytes,["archiveSha256"]=p.ArchiveSha256 };
        if(!omitFingerprint)value["fingerprint"]=p.Fingerprint;return value;
    }
    public static string Fingerprint(ContainerRecyclePurgePlan p)
    {
        var canonical=new StringBuilder();ContainerRecycleRestoreWire.Canonical(JsonSerializer.SerializeToElement(Plan(p,true)),canonical);
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false,true).GetBytes("whaledeck-container-recycle-purge-plan-v1\n"+canonical)));
    }
    public static void Receipt(ContainerRecyclePurgeRequest request,ContainerRecyclePurgeReceipt value)
    {
        try{RequireKnown(value);}catch(ArgumentException){throw Invalid();}
        if(value.OperationId!=request.OperationId||value.SourceOperationId!=request.Plan.Command.Source.OperationId||value.Fingerprint!=request.Plan.Fingerprint||
           value.State is not ("Reserved" or "Completed" or "Aborted" or "Unknown")||value.OwnedFence&&!value.SourceFenced)throw Invalid();
        if(value.State=="Reserved"&&(!value.SourceFenced||!value.OwnedFence||!value.HasPurgeApplied||value.PurgeApplied)||
           value.State=="Completed"&&(!value.SourceFenced||!value.OwnedFence||!value.HasPurgeApplied||!value.PurgeApplied)||
           value.State=="Aborted"&&(value.OwnedFence||!value.HasPurgeApplied||value.PurgeApplied)||
           value.State=="Unknown"&&(!value.SourceFenced||value.HasPurgeApplied))throw Invalid();
    }
    public static InvalidOperationException Invalid()=>new("CONTAINER_RECYCLE_PURGE_OUTPUT_INVALID");
}
