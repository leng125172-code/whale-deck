using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

// Candidate boundary only; no mapped service, capability or Root runner yet.
internal static class ContainerRecycleArchivePolicy
{
    public static void Binding(IMessage request,ServerCallContext context)
    {
        var claimed=context.RequestHeaders.GetValue(ContainerRecycleArchiveBinding.Header);var actual=ContainerRecycleArchiveBinding.Hash(request);
        if(claimed is not {Length:64}||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),Encoding.ASCII.GetBytes(claimed)))throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_ARCHIVE_BINDING_INVALID"));
    }
    public static void Command(ContainerRecycleArchiveCommand? c)
    {
        if(c?.Purge?.Plan is null||c.Mode!="ArchiveCopy")throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_COMMAND_REQUIRED");
        ContainerRecycleArchiveWire.RequireKnown(c);FileTransferPolicy.Identity(c.Purge.OperationId);FileTransferPolicy.Hash(c.Purge.Owner);ContainerRecyclePurgePolicy.Plan(c.Purge.Plan);
        if(c.Purge.OperationId==c.Purge.Plan.Command.Source.OperationId||c.HistoryDigest!=c.Purge.Plan.Command.HistoryDigest)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_COMMAND_INVALID");
    }
    public static void Plan(ContainerRecycleArchivePlan? p)
    {
        if(p is null||p.SchemaVersion!=1)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");ContainerRecycleArchiveWire.RequireKnown(p);Command(p.Command);var old=p.Command.Purge.Plan;
        if(p.ParentIdentity is null||!p.ParentIdentity.Equals(old.ParentIdentity)||p.TargetProof!=old.TargetProof||p.OwnerUid!=old.OwnerUid||p.OwnerGid!=old.OwnerGid||
           p.PrivateIdentity is not null&&!p.PrivateIdentity.Equals(old.PrivateIdentity)||p.DeletedIdentity is null||!p.DeletedIdentity.Equals(old.RetainedIdentity)||p.DeletedChangedAtUnixNs!=old.RetainedChangedAtUnixNs||
           p.StoreIdentity is null||!p.StoreIdentity.Equals(old.StoreIdentity)||p.StorageHash!=old.StorageHash||p.ArchiveIdentity is null||!p.ArchiveIdentity.Equals(old.ArchiveIdentity)||p.ArchiveChangedAtUnixNs!=old.ArchiveChangedAtUnixNs||p.ArchiveBytes!=old.ArchiveBytes||p.ArchiveSha256!=old.ArchiveSha256)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");
        foreach(var hash in new[]{p.ParentVersion,p.SourceRecordDigest,p.PurgeRecordDigest,p.PurgeMarkerDigest,p.HistoryMetadataDigest,p.Fingerprint})FileTransferPolicy.Hash(hash);
        if(p.Fingerprint!=ContainerRecycleArchiveWire.Fingerprint(p))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");
    }
    public static void Request(ContainerRecycleArchiveRequest q)
    {
        ContainerRecycleArchiveWire.RequireKnown(q);FileTransferPolicy.Identity(q.OperationId);FileTransferPolicy.Hash(q.Owner);Plan(q.Plan);ContainerRecyclePolicy.Context(q.Context);
        if(q.Context.JobId!=q.OperationId||q.OperationId==q.Plan.Command.Purge.OperationId||q.OperationId==q.Plan.Command.Purge.Plan.Command.Source.OperationId)throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONTEXT_INVALID");
    }
    private static void Fields(JsonElement v,params string[] names)
    {if(v.ValueKind!=JsonValueKind.Object)throw Invalid();var keys=v.EnumerateObject().Select(p=>p.Name).ToArray();if(keys.Length!=names.Length||keys.Distinct(StringComparer.Ordinal).Count()!=names.Length||keys.Except(names,StringComparer.Ordinal).Any())throw Invalid();}
    private static string Text(JsonElement v,string name)=>v.GetProperty(name).GetString()??throw Invalid();
    private static FileDirectoryIdentity Directory(JsonElement v)
    {if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=2)throw Invalid();return new(){Device=v[0].GetUInt64(),Inode=v[1].GetUInt64()};}
    private static (FileObjectIdentity Identity,long Changed) Identity(JsonElement v)
    {if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=9)throw Invalid();return(new(){Device=v[0].GetUInt64(),Inode=v[1].GetUInt64(),Mode=v[2].GetUInt32(),OwnerUid=v[3].GetUInt32(),OwnerGid=v[4].GetUInt32(),LinkCount=v[5].GetUInt64(),SizeBytes=v[6].GetInt64(),ModifiedAtUnixNs=v[7].GetInt64()},v[8].GetInt64());}
    public static ContainerRecycleArchivePlan PlanOutput(JsonElement v,ContainerRecycleArchiveCommand expected)
    {
        try
        {
            Fields(v,"schemaVersion","command","targetProof","parentIdentity","parentVersion","uid","gid","privateIdentity","deletedIdentity","sourceRecordDigest","purgeRecordDigest","purgeMarkerDigest","historyMetadataDigest","storageHash","storeIdentity","archiveIdentity","archiveBytes","archiveSha256","fingerprint");
            var c=v.GetProperty("command");Fields(c,"purge","historyDigest","mode");var purge=c.GetProperty("purge");Fields(purge,"operationId","owner","plan");
            var command=new ContainerRecycleArchiveCommand{Mode=Text(c,"mode"),HistoryDigest=Text(c,"historyDigest"),Purge=new(){OperationId=Text(purge,"operationId"),Owner=Text(purge,"owner"),Plan=ContainerRecyclePurgePolicy.PlanOutput(purge.GetProperty("plan"),expected.Purge.Plan.Command)}};
            if(!command.Equals(expected))throw Invalid();var deleted=Identity(v.GetProperty("deletedIdentity"));var archive=Identity(v.GetProperty("archiveIdentity"));
            var p=new ContainerRecycleArchivePlan{SchemaVersion=v.GetProperty("schemaVersion").GetUInt32(),Command=command,TargetProof=Text(v,"targetProof"),ParentIdentity=Directory(v.GetProperty("parentIdentity")),ParentVersion=Text(v,"parentVersion"),OwnerUid=v.GetProperty("uid").GetUInt32(),OwnerGid=v.GetProperty("gid").GetUInt32(),DeletedIdentity=deleted.Identity,DeletedChangedAtUnixNs=deleted.Changed,
                SourceRecordDigest=Text(v,"sourceRecordDigest"),PurgeRecordDigest=Text(v,"purgeRecordDigest"),PurgeMarkerDigest=Text(v,"purgeMarkerDigest"),HistoryMetadataDigest=Text(v,"historyMetadataDigest"),StorageHash=Text(v,"storageHash"),StoreIdentity=Directory(v.GetProperty("storeIdentity")),ArchiveIdentity=archive.Identity,ArchiveChangedAtUnixNs=archive.Changed,ArchiveBytes=v.GetProperty("archiveBytes").GetInt64(),ArchiveSha256=Text(v,"archiveSha256"),Fingerprint=Text(v,"fingerprint")};
            if(v.GetProperty("privateIdentity").ValueKind!=JsonValueKind.Null)p.PrivateIdentity=Directory(v.GetProperty("privateIdentity"));Plan(p);return p;
        }
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or FormatException or OverflowException or JsonException){throw Invalid();}
    }
    public static ContainerRecycleArchiveReceipt Receipt(JsonElement v,ContainerRecycleArchiveRequest q)
    {
        try
        {
            Fields(v,"operationId","sourceOperationId","purgeOperationId","fingerprint","state","sourceFenced","ownedFence","archiveCopied","bindingCurrent","publishedIdentity");
            var r=new ContainerRecycleArchiveReceipt{OperationId=Text(v,"operationId"),SourceOperationId=Text(v,"sourceOperationId"),PurgeOperationId=Text(v,"purgeOperationId"),Fingerprint=Text(v,"fingerprint"),State=Text(v,"state"),SourceFenced=v.GetProperty("sourceFenced").GetBoolean(),OwnedFence=v.GetProperty("ownedFence").GetBoolean()};
            if(v.GetProperty("archiveCopied").ValueKind!=JsonValueKind.Null)r.ArchiveCopied=v.GetProperty("archiveCopied").GetBoolean();
            if(v.GetProperty("bindingCurrent").ValueKind!=JsonValueKind.Null)r.BindingCurrent=v.GetProperty("bindingCurrent").GetBoolean();
            if(v.GetProperty("publishedIdentity").ValueKind!=JsonValueKind.Null){var i=Identity(v.GetProperty("publishedIdentity"));r.PublishedIdentity=i.Identity;r.PublishedChangedAtUnixNs=i.Changed;}
            ContainerRecycleArchiveWire.Receipt(q,r);return r;
        }
        catch(Exception e)when(e is ArgumentException or InvalidOperationException or FormatException or OverflowException or JsonException){throw Invalid();}
    }
    private static InvalidOperationException Invalid()=>ContainerRecycleArchiveWire.Invalid();
}
