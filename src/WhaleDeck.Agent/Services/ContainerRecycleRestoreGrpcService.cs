using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class ContainerRecycleRestoreGrpcService(FileTransferRunner runner) : ContainerRecycleRestoreService.ContainerRecycleRestoreServiceBase
{
    public override async Task<ContainerRecycleRestorePlan> Plan(ContainerRecycleRestorePlanRequest request, ServerCallContext context)
    {
        ContainerRecycleRestoreWire.RequireKnown(request); ContainerRecycleRestorePolicy.Command(request.Command); ContainerRecyclePolicy.Context(request.Context, planning:true);
        ContainerRecycleRestorePolicy.Binding(request,context);
        using var result = await runner.ContainerRecycleRestoreOnceAsync("plan",JsonSerializer.Serialize(ContainerRecycleRestoreWire.Command(request.Command)),context.CancellationToken);
        return ContainerRecycleRestorePolicy.PlanOutput(result.RootElement,request.Command);
    }
    public override Task<ContainerRecycleRestoreReceipt> Apply(ContainerRecycleRestoreRequest request,ServerCallContext context)=>Run("apply",request,context);
    public override Task<ContainerRecycleRestoreReceipt> Inspect(ContainerRecycleRestoreRequest request,ServerCallContext context)=>Run("inspect",request,context);
    public override Task<ContainerRecycleRestoreReceipt> Discard(ContainerRecycleRestoreRequest request,ServerCallContext context)=>Run("discard",request,context);
    private async Task<ContainerRecycleRestoreReceipt> Run(string action,ContainerRecycleRestoreRequest request,ServerCallContext context)
    {
        ContainerRecycleRestorePolicy.Request(request);ContainerRecycleRestorePolicy.Binding(request,context);
        using var result=await runner.ContainerRecycleRestoreOnceAsync(action,JsonSerializer.Serialize(new {operationId=request.OperationId,owner=request.Owner,plan=ContainerRecycleRestoreWire.Plan(request.Plan)}),context.CancellationToken);
        return ContainerRecycleRestorePolicy.Receipt(result.RootElement,request);
    }
}

internal static class ContainerRecycleRestorePolicy
{
    public static void Binding(IMessage request,ServerCallContext context)
    {
        var claimed=context.RequestHeaders.GetValue(ContainerRecycleRestoreBinding.Header);var actual=ContainerRecycleRestoreBinding.Hash(request);
        if(claimed is not {Length:64}||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),Encoding.ASCII.GetBytes(claimed)))
            throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_RESTORE_BINDING_INVALID"));
    }
    public static void Command(ContainerRecycleRestoreCommand? c)
    {
        if(c?.Source is null)throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_COMMAND_REQUIRED");
        ContainerRecycleRestoreWire.RequireKnown(c);FileTransferPolicy.Identity(c.Source.OperationId);FileTransferPolicy.Hash(c.Source.Owner);ContainerRecyclePolicy.Plan(c.Source.Plan);FileTransferPolicy.Hash(c.HistoryDigest);
    }
    public static void Plan(ContainerRecycleRestorePlan? p)
    {
        if(p is null||p.SchemaVersion!=1)throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
        ContainerRecycleRestoreWire.RequireKnown(p);Command(p.Command);var old=p.Command.Source.Plan;
        if(p.ParentIdentity is not {Inode:>0}||!p.ParentIdentity.Equals(old.ParentIdentity)||p.PrivateIdentity is not {Inode:>0}||p.OwnerUid!=old.OwnerUid||p.OwnerGid!=old.OwnerGid||
           p.TargetProof!=old.TargetProof||p.RetainedIdentity is null||!p.RetainedIdentity.Equals(old.SourceIdentity)||p.RetainedChangedAtUnixNs<old.SourceChangedAtUnixNs)
            throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
        FileTransferPolicy.Hash(p.ParentVersion);FileTransferPolicy.Hash(p.Fingerprint);
        if(p.Fingerprint!=ContainerRecycleRestoreWire.Fingerprint(p))throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
    }
    public static void Request(ContainerRecycleRestoreRequest r)
    {
        ContainerRecycleRestoreWire.RequireKnown(r);FileTransferPolicy.Identity(r.OperationId);FileTransferPolicy.Hash(r.Owner);Plan(r.Plan);ContainerRecyclePolicy.Context(r.Context);
        if(r.Context.JobId!=r.OperationId||r.OperationId==r.Plan.Command.Source.OperationId)throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONTEXT_INVALID");
    }
    public static ContainerRecycleRestorePlan PlanOutput(JsonElement v,ContainerRecycleRestoreCommand expected)
    {
        Fields(v,"schemaVersion","command","targetProof","parentIdentity","parentVersion","uid","gid","privateIdentity","retainedIdentity","fingerprint");
        var c=v.GetProperty("command");Fields(c,"source","historyDigest");var s=c.GetProperty("source");Fields(s,"operationId","owner","plan");
        var cmd=new ContainerRecycleRestoreCommand {HistoryDigest=Text(c,"historyDigest"),Source=new() {OperationId=Text(s,"operationId"),Owner=Text(s,"owner"),Plan=ContainerRecyclePolicy.PlanOutput(s.GetProperty("plan"),expected.Source.Plan.Command)}};
        if(!cmd.Equals(expected))throw Invalid();
        var parent=v.GetProperty("parentIdentity");var priv=v.GetProperty("privateIdentity");var i=v.GetProperty("retainedIdentity");
        if(parent.ValueKind!=JsonValueKind.Array||parent.GetArrayLength()!=2||priv.ValueKind!=JsonValueKind.Array||priv.GetArrayLength()!=2||i.ValueKind!=JsonValueKind.Array||i.GetArrayLength()!=9)throw Invalid();
        var p=new ContainerRecycleRestorePlan {SchemaVersion=v.GetProperty("schemaVersion").GetUInt32(),Command=cmd,TargetProof=Text(v,"targetProof"),ParentIdentity=new(){Device=parent[0].GetUInt64(),Inode=parent[1].GetUInt64()},PrivateIdentity=new(){Device=priv[0].GetUInt64(),Inode=priv[1].GetUInt64()},ParentVersion=Text(v,"parentVersion"),OwnerUid=v.GetProperty("uid").GetUInt32(),OwnerGid=v.GetProperty("gid").GetUInt32(),Fingerprint=Text(v,"fingerprint"),
            RetainedIdentity=new(){Device=i[0].GetUInt64(),Inode=i[1].GetUInt64(),Mode=i[2].GetUInt32(),OwnerUid=i[3].GetUInt32(),OwnerGid=i[4].GetUInt32(),LinkCount=i[5].GetUInt64(),SizeBytes=i[6].GetInt64(),ModifiedAtUnixNs=i[7].GetInt64()},RetainedChangedAtUnixNs=i[8].GetInt64()};
        try{Plan(p);}catch(ArgumentException){throw Invalid();}return p;
    }
    public static ContainerRecycleRestoreReceipt Receipt(JsonElement v,ContainerRecycleRestoreRequest expected)
    {
        Fields(v,"operationId","sourceOperationId","fingerprint","targetProof","state","bindingCurrent","replayed","historyVerified","historyDigest","historyAvailable","sourceState","sourceRetained","sourceRestored","restoredByThisOperation");
        var r=new ContainerRecycleRestoreReceipt{OperationId=Text(v,"operationId"),SourceOperationId=Text(v,"sourceOperationId"),Fingerprint=Text(v,"fingerprint"),TargetProof=Text(v,"targetProof"),State=Text(v,"state"),BindingCurrent=v.GetProperty("bindingCurrent").GetBoolean(),Replayed=v.GetProperty("replayed").GetBoolean(),HistoryVerified=v.GetProperty("historyVerified").GetBoolean(),HistoryDigest=Text(v,"historyDigest"),HistoryAvailable=v.GetProperty("historyAvailable").GetBoolean()};
        if(v.GetProperty("sourceState").ValueKind!=JsonValueKind.Null)r.SourceState=Text(v,"sourceState");
        if(v.GetProperty("sourceRetained").ValueKind!=JsonValueKind.Null)r.SourceRetained=v.GetProperty("sourceRetained").GetBoolean();
        if(v.GetProperty("sourceRestored").ValueKind!=JsonValueKind.Null)r.SourceRestored=v.GetProperty("sourceRestored").GetBoolean();
        if(v.GetProperty("restoredByThisOperation").ValueKind!=JsonValueKind.Null)r.RestoredByThisOperation=v.GetProperty("restoredByThisOperation").GetBoolean();
        ContainerRecycleRestoreWire.Receipt(expected,r);return r;
    }
    private static void Fields(JsonElement v,params string[] names)
    {if(v.ValueKind!=JsonValueKind.Object)throw Invalid();var keys=v.EnumerateObject().Select(p=>p.Name).ToArray();if(keys.Length!=names.Length||keys.Distinct(StringComparer.Ordinal).Count()!=names.Length||keys.Except(names,StringComparer.Ordinal).Any())throw Invalid();}
    private static string Text(JsonElement v,string name)=>v.GetProperty(name).GetString()??throw Invalid();
    private static InvalidOperationException Invalid()=>new("CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID");
    public static RpcException Failure(string? code)
    {
        var status=code switch {
            "CONTAINER_RECYCLE_RESTORE_NOT_FOUND"=>StatusCode.NotFound,
            "CONTAINER_RECYCLE_RESTORE_REQUEST_INVALID" or "CONTAINER_RECYCLE_RESTORE_PLAN_INVALID" or "CONTAINER_RECYCLE_RESTORE_NEW_OPERATION_REQUIRED" or "CONTAINER_RECYCLE_RESTORE_SOURCE_INVALID"=>StatusCode.InvalidArgument,
            "CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS" or "CONTAINER_RECYCLE_RESTORE_PLAN_CHANGED" or "CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED" or "CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_CONFLICT"=>StatusCode.Aborted,
            "CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY" or "CONTAINER_RECYCLE_RESTORE_BUSY" or "CONTAINER_RECYCLE_RESTORE_RECEIPT_LIMIT" or "CONTAINER_RECYCLE_BUSY" or "FILES_BACKUP_CAPACITY_REQUIRED"=>StatusCode.ResourceExhausted,
            "CONTAINER_RECYCLE_RESTORE_TIMEOUT" or "FILES_INPUT_TIMEOUT"=>StatusCode.DeadlineExceeded,
            "CONTAINER_RECYCLE_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or "CONTAINER_FILES_SHARED_CORE_MOUNT"=>StatusCode.PermissionDenied,
            "CONTAINER_RECYCLE_RESTORE_RECEIPT_INVALID" or "CONTAINER_RECYCLE_RESTORE_STATE_UNSAFE" or "CONTAINER_RECYCLE_RESTORE_SESSION_REQUIRED" or "CONTAINER_RECYCLE_RESTORE_MODULE_UNSAFE" or "CONTAINER_RECYCLE_RESTORE_INVOCATION_INVALID" or "CONTAINER_RECYCLE_RESTORE_ACTION_INVALID" or "CONTAINER_RECYCLE_RESTORE_SCHEMA_INVALID" or
            "CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING" or "CONTAINER_RECYCLE_RESTORE_CLAIMED" or "CONTAINER_RECYCLE_HISTORY_CHANGED" or "CONTAINER_RECYCLE_HISTORY_UNSAFE" or "CONTAINER_RECYCLE_SOURCE_CHANGED" or "FILES_BACKUP_NOT_HDD"=>StatusCode.FailedPrecondition,
            _=>StatusCode.Unavailable};
        if(status==StatusCode.Unavailable){var legacy=ContainerRecyclePolicy.Failure(code);if(legacy.StatusCode!=StatusCode.Unavailable)return legacy;}
        return new(new(status,status==StatusCode.Unavailable?"CONTAINER_RECYCLE_RESTORE_UNAVAILABLE":code!));
    }
}
