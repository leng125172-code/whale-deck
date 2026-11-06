using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class ContainerRecyclePurgeGrpcService(FileTransferRunner runner) : ContainerRecyclePurgeService.ContainerRecyclePurgeServiceBase
{
    public override async Task<ContainerRecyclePurgePlan> Plan(ContainerRecyclePurgePlanRequest request,ServerCallContext context)
    {
        ContainerRecyclePurgeWire.RequireKnown(request);ContainerRecyclePurgePolicy.Command(request.Command);ContainerRecyclePolicy.Context(request.Context,planning:true);ContainerRecyclePurgePolicy.Binding(request,context);
        using var result=await runner.ContainerRecyclePurgeOnceAsync("plan",JsonSerializer.Serialize(ContainerRecyclePurgeWire.Command(request.Command)),context.CancellationToken);
        return ContainerRecyclePurgePolicy.PlanOutput(result.RootElement,request.Command);
    }
    public override Task<ContainerRecyclePurgeReceipt> Reserve(ContainerRecyclePurgeRequest request,ServerCallContext context)=>Run("reserve",request,context);
    public override Task<ContainerRecyclePurgeReceipt> Apply(ContainerRecyclePurgeRequest request,ServerCallContext context)=>Run("apply",request,context);
    public override Task<ContainerRecyclePurgeReceipt> Inspect(ContainerRecyclePurgeRequest request,ServerCallContext context)=>Run("inspect",request,context);
    public override Task<ContainerRecyclePurgeReceipt> Discard(ContainerRecyclePurgeRequest request,ServerCallContext context)=>Run("discard",request,context);
    private async Task<ContainerRecyclePurgeReceipt> Run(string action,ContainerRecyclePurgeRequest request,ServerCallContext context)
    {
        ContainerRecyclePurgePolicy.Request(request);ContainerRecyclePurgePolicy.Binding(request,context);
        using var result=await runner.ContainerRecyclePurgeOnceAsync(action,JsonSerializer.Serialize(new {operationId=request.OperationId,owner=request.Owner,plan=ContainerRecyclePurgeWire.Plan(request.Plan)}),context.CancellationToken);
        return ContainerRecyclePurgePolicy.Receipt(result.RootElement,request);
    }
}

internal static class ContainerRecyclePurgePolicy
{
    public static void Binding(IMessage request,ServerCallContext context)
    {
        var claimed=context.RequestHeaders.GetValue(ContainerRecyclePurgeBinding.Header);var actual=ContainerRecyclePurgeBinding.Hash(request);
        if(claimed is not {Length:64}||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),Encoding.ASCII.GetBytes(claimed)))throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_PURGE_BINDING_INVALID"));
    }
    public static void Command(ContainerRecyclePurgeCommand? c)
    {
        if(c?.Source is null||c.Scope!="Originals")throw new ArgumentException("CONTAINER_RECYCLE_PURGE_COMMAND_REQUIRED");
        ContainerRecyclePurgeWire.RequireKnown(c);FileTransferPolicy.Identity(c.Source.OperationId);FileTransferPolicy.Hash(c.Source.Owner);ContainerRecyclePolicy.Plan(c.Source.Plan);FileTransferPolicy.Hash(c.HistoryDigest);
    }
    public static void Plan(ContainerRecyclePurgePlan? p)
    {
        if(p is null||p.SchemaVersion!=1)throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");
        ContainerRecyclePurgeWire.RequireKnown(p);Command(p.Command);var old=p.Command.Source.Plan;
        if(p.ParentIdentity is not {Inode:>0}||!p.ParentIdentity.Equals(old.ParentIdentity)||p.PrivateIdentity is not {Inode:>0}||p.StoreIdentity is not {Inode:>0}||
           p.OwnerUid!=old.OwnerUid||p.OwnerGid!=old.OwnerGid||p.TargetProof!=old.TargetProof||p.RetainedIdentity is null||!p.RetainedIdentity.Equals(old.SourceIdentity)||p.RetainedChangedAtUnixNs<old.SourceChangedAtUnixNs||
           p.ArchiveIdentity is not {Inode:>0,Mode:33152,OwnerUid:0,OwnerGid:0,LinkCount:1} a||a.SizeBytes!=old.SourceIdentity.SizeBytes||p.ArchiveBytes!=a.SizeBytes||p.ArchiveSha256!=old.Sha256||p.ArchiveChangedAtUnixNs<0||p.HistoryCreatedAtUnixMs is <0 or >253402300799999)
            throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");
        foreach(var hash in new[]{p.ParentVersion,p.SourceRecordDigest,p.HistoryMetadataDigest,p.StorageHash,p.ArchiveSha256,p.Fingerprint})FileTransferPolicy.Hash(hash);
        if(p.Fingerprint!=ContainerRecyclePurgeWire.Fingerprint(p))throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");
    }
    public static void Request(ContainerRecyclePurgeRequest r)
    {
        ContainerRecyclePurgeWire.RequireKnown(r);FileTransferPolicy.Identity(r.OperationId);FileTransferPolicy.Hash(r.Owner);Plan(r.Plan);ContainerRecyclePolicy.Context(r.Context);
        if(r.Context.JobId!=r.OperationId||r.OperationId==r.Plan.Command.Source.OperationId)throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONTEXT_INVALID");
    }
    private static FileDirectoryIdentity Directory(JsonElement value)
    {if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Invalid();return new(){Device=value[0].GetUInt64(),Inode=value[1].GetUInt64()};}
    private static (FileObjectIdentity Identity,long Changed) Identity(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=9)throw Invalid();
        return(new(){Device=value[0].GetUInt64(),Inode=value[1].GetUInt64(),Mode=value[2].GetUInt32(),OwnerUid=value[3].GetUInt32(),OwnerGid=value[4].GetUInt32(),LinkCount=value[5].GetUInt64(),SizeBytes=value[6].GetInt64(),ModifiedAtUnixNs=value[7].GetInt64()},value[8].GetInt64());
    }
    public static ContainerRecyclePurgePlan PlanOutput(JsonElement v,ContainerRecyclePurgeCommand expected)
    {
        Fields(v,"schemaVersion","command","targetProof","parentIdentity","parentVersion","uid","gid","privateIdentity","retainedIdentity","sourceRecordDigest","historyMetadataDigest","historyCreatedAtUnixMs","storageHash","storeIdentity","archiveIdentity","archiveBytes","archiveSha256","fingerprint");
        var c=v.GetProperty("command");Fields(c,"source","historyDigest","scope");var s=c.GetProperty("source");Fields(s,"operationId","owner","plan");
        var command=new ContainerRecyclePurgeCommand{Scope=Text(c,"scope"),HistoryDigest=Text(c,"historyDigest"),Source=new(){OperationId=Text(s,"operationId"),Owner=Text(s,"owner"),Plan=ContainerRecyclePolicy.PlanOutput(s.GetProperty("plan"),expected.Source.Plan.Command)}};
        if(!command.Equals(expected))throw Invalid();var retained=Identity(v.GetProperty("retainedIdentity"));var archive=Identity(v.GetProperty("archiveIdentity"));
        var p=new ContainerRecyclePurgePlan{SchemaVersion=v.GetProperty("schemaVersion").GetUInt32(),Command=command,TargetProof=Text(v,"targetProof"),ParentIdentity=Directory(v.GetProperty("parentIdentity")),ParentVersion=Text(v,"parentVersion"),OwnerUid=v.GetProperty("uid").GetUInt32(),OwnerGid=v.GetProperty("gid").GetUInt32(),PrivateIdentity=Directory(v.GetProperty("privateIdentity")),RetainedIdentity=retained.Identity,RetainedChangedAtUnixNs=retained.Changed,
            SourceRecordDigest=Text(v,"sourceRecordDigest"),HistoryMetadataDigest=Text(v,"historyMetadataDigest"),HistoryCreatedAtUnixMs=v.GetProperty("historyCreatedAtUnixMs").GetInt64(),StorageHash=Text(v,"storageHash"),StoreIdentity=Directory(v.GetProperty("storeIdentity")),ArchiveIdentity=archive.Identity,ArchiveChangedAtUnixNs=archive.Changed,ArchiveBytes=v.GetProperty("archiveBytes").GetInt64(),ArchiveSha256=Text(v,"archiveSha256"),Fingerprint=Text(v,"fingerprint")};
        try{Plan(p);}catch(ArgumentException){throw Invalid();}return p;
    }
    public static ContainerRecyclePurgeReceipt Receipt(JsonElement v,ContainerRecyclePurgeRequest request)
    {
        Fields(v,"operationId","sourceOperationId","fingerprint","state","sourceFenced","ownedFence","purgeApplied");
        var result=new ContainerRecyclePurgeReceipt{OperationId=Text(v,"operationId"),SourceOperationId=Text(v,"sourceOperationId"),Fingerprint=Text(v,"fingerprint"),State=Text(v,"state"),SourceFenced=v.GetProperty("sourceFenced").GetBoolean(),OwnedFence=v.GetProperty("ownedFence").GetBoolean()};
        if(v.GetProperty("purgeApplied").ValueKind!=JsonValueKind.Null)result.PurgeApplied=v.GetProperty("purgeApplied").GetBoolean();ContainerRecyclePurgeWire.Receipt(request,result);return result;
    }
    private static void Fields(JsonElement value,params string[] names)
    {if(value.ValueKind!=JsonValueKind.Object)throw Invalid();var keys=value.EnumerateObject().Select(p=>p.Name).ToArray();if(keys.Length!=names.Length||keys.Distinct(StringComparer.Ordinal).Count()!=names.Length||keys.Except(names,StringComparer.Ordinal).Any())throw Invalid();}
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).GetString()??throw Invalid();
    private static InvalidOperationException Invalid()=>ContainerRecyclePurgeWire.Invalid();
    public static RpcException Failure(string? code)
    {
        var status=code switch {
            "CONTAINER_RECYCLE_PURGE_NOT_FOUND"=>StatusCode.NotFound,
            "CONTAINER_RECYCLE_PURGE_REQUEST_INVALID" or "CONTAINER_RECYCLE_PURGE_PLAN_INVALID" or "CONTAINER_RECYCLE_PURGE_NEW_OPERATION_REQUIRED"=>StatusCode.InvalidArgument,
            "CONTAINER_RECYCLE_PURGE_SOURCE_CHANGED" or "CONTAINER_RECYCLE_PURGE_PLAN_CHANGED" or "CONTAINER_RECYCLE_PURGE_IDEMPOTENCY_CONFLICT"=>StatusCode.Aborted,
            "CONTAINER_RECYCLE_PURGE_BUSY" or "CONTAINER_RECYCLE_PURGE_SOURCE_BUSY" or "CONTAINER_RECYCLE_PURGE_RECEIPT_LIMIT"=>StatusCode.ResourceExhausted,
            "CONTAINER_RECYCLE_PURGE_TIMEOUT"=>StatusCode.DeadlineExceeded,
            "CONTAINER_RECYCLE_PURGE_STATE_UNSAFE" or "CONTAINER_RECYCLE_PURGE_NOT_RESERVED" or "CONTAINER_RECYCLE_PURGE_FENCE_CHANGED" or "CONTAINER_RECYCLE_PURGE_HISTORY_CHANGED" or "CONTAINER_RECYCLE_PURGE_SESSION_REQUIRED" or "CONTAINER_RECYCLE_PURGE_RECEIPT_INVALID" or "CONTAINER_RECYCLE_PURGE_MODULE_UNSAFE" or "CONTAINER_RECYCLE_PURGE_ACTION_INVALID" or "CONTAINER_RECYCLE_PURGE_INVOCATION_INVALID"=>StatusCode.FailedPrecondition,
            _=>StatusCode.Unavailable};
        if(status==StatusCode.Unavailable){var old=ContainerRecycleRestorePolicy.Failure(code);if(old.StatusCode!=StatusCode.Unavailable)return old;}
        return new(new(status,status==StatusCode.Unavailable?"CONTAINER_RECYCLE_PURGE_UNAVAILABLE":code!));
    }
}
