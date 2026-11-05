using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerRecycleRestoreGateway
{
    public async Task RequireContainerRecycleRestoreAsync(CancellationToken cancellationToken)=>_ = await RecycleRestoreClient(cancellationToken);
    private async Task<Rpc.ContainerRecycleRestoreService.ContainerRecycleRestoreServiceClient> RecycleRestoreClient(CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));var caps=await GetCapabilitiesAsync(timeout.Token);
        if(caps.ProtocolMajor!=1||caps.ProtocolMinor<57||!caps.Capabilities.Contains("docker.files.recycle.restore"))throw new RpcException(new(StatusCode.Unimplemented,"CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecycleRestoreHeaders(string action,IMessage request)
    {
        if(_capabilityKey is not {Length:>=32})throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE"));
        var hash=ContainerRecycleRestoreBinding.Hash(request);var expires=DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        var sig=HMACSHA256.HashData(_capabilityKey,Encoding.UTF8.GetBytes(ContainerRecycleRestoreBinding.Payload(expires,nonce,ContainerRecycleRestoreBinding.Service+action,hash)));
        return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(sig)},{ContainerRecycleRestoreBinding.Header,hash}};
    }
    public async Task<ContainerRecycleRestorePlanDto> PlanContainerRecycleRestoreAsync(TerminalActor actor,ContainerRecycleRestoreCommandDto command,CancellationToken cancellationToken)
    {
        ContainerRecycleRestoreValidation.Command(command);
        if(string.IsNullOrWhiteSpace(actor.Subject)||actor.Subject.Length>256||actor.Subject.Any(char.IsControl)||string.IsNullOrWhiteSpace(actor.TraceId)||actor.TraceId.Length>128||actor.TraceId.Any(char.IsControl))throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONTEXT_INVALID");
        // Freeze every nested byte array before the first await.
        var request=new Rpc.ContainerRecycleRestorePlanRequest{Command=RestoreCommand(command),Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await RecycleRestoreClient(cancellationToken);Rpc.ContainerRecycleRestorePlan result;
        try{result=await client.PlanAsync(request,RecycleRestoreHeaders("Plan",request),deadline:DateTime.UtcNow.AddSeconds(120),cancellationToken:cancellationToken);}
        catch(RpcException error)when(RestorePreflightCode(error) is {} code){throw new ContainerRecycleRestorePreflightRejectedException(code);}
        try
        {
            ContainerRecycleRestoreWire.RequireKnown(result);if(result.Command?.Source?.Plan is null||result.ParentIdentity is null||result.PrivateIdentity is null||result.RetainedIdentity is null)throw ContainerRecycleRestoreValidation.Invalid();if(!result.Command.Equals(request.Command)||result.Fingerprint!=ContainerRecycleRestoreWire.Fingerprint(result))throw ContainerRecycleRestoreValidation.Invalid();
            return RestorePlan(result);
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or FormatException or NullReferenceException){throw ContainerRecycleRestoreValidation.Invalid();}
    }
    private static string? RestorePreflightCode(RpcException error)=>(error.StatusCode,error.Status.Detail) switch
    {
        (StatusCode.Aborted,"CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED" or "CONTAINER_RECYCLE_RESTORE_PLAN_CHANGED") or (StatusCode.FailedPrecondition,"CONTAINER_RECYCLE_SOURCE_CHANGED")=>"CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED",
        (StatusCode.ResourceExhausted,"CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY" or "CONTAINER_RECYCLE_RESTORE_BUSY" or "CONTAINER_RECYCLE_BUSY")=>"CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY",
        (StatusCode.Aborted,"CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS" or "CONTAINER_RECYCLE_TARGET_EXISTS")=>"CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS",
        (StatusCode.FailedPrecondition,"CONTAINER_FILES_TARGET_MISSING" or "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING")=>"CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED",
        (StatusCode.PermissionDenied,"CONTAINER_RECYCLE_PROTECTED" or "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or "CONTAINER_FILES_SHARED_CORE_MOUNT")=>"CONTAINER_RECYCLE_RESTORE_PROTECTED",
        (StatusCode.FailedPrecondition,"CONTAINER_RECYCLE_HISTORY_CHANGED" or "CONTAINER_RECYCLE_HISTORY_UNSAFE" or "FILES_BACKUP_NOT_HDD")=>"CONTAINER_RECYCLE_RESTORE_HISTORY_UNAVAILABLE",
        _=>null
    };
    public Task<ContainerRecycleRestoreReceiptDto> ApplyContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation,CancellationToken cancellationToken)=>RestoreAction("Apply",operation,cancellationToken);
    public Task<ContainerRecycleRestoreReceiptDto> DiscardContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation,CancellationToken cancellationToken)=>RestoreAction("Discard",operation,cancellationToken);
    public async Task<ContainerRecycleRestoreReceiptDto?> InspectContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto operation,CancellationToken cancellationToken)
    {try{return await RestoreAction("Inspect",operation,cancellationToken);}catch(RpcException e)when(e.StatusCode==StatusCode.NotFound&&e.Status.Detail=="CONTAINER_RECYCLE_RESTORE_NOT_FOUND"){return null;}}
    private async Task<ContainerRecycleRestoreReceiptDto> RestoreAction(string action,ContainerRecycleRestoreOperationDto operation,CancellationToken cancellationToken)
    {
        ContainerRecycleRestoreValidation.Operation(operation);
        var request=new Rpc.ContainerRecycleRestoreRequest{OperationId=operation.Id.ToString("N"),Owner=operation.Owner,Plan=RestorePlan(operation.Plan),Context=UploadContext(operation.Context)};
        if(request.Plan.Fingerprint!=ContainerRecycleRestoreWire.Fingerprint(request.Plan))throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PLAN_INVALID");
        _=RestorePlan(request.Plan);
        var client=await RecycleRestoreClient(cancellationToken);var headers=RecycleRestoreHeaders(action,request);var deadline=DateTime.UtcNow.AddSeconds(120);
        var result=action switch {"Apply"=>await client.ApplyAsync(request,headers,deadline,cancellationToken),"Inspect"=>await client.InspectAsync(request,headers,deadline,cancellationToken),"Discard"=>await client.DiscardAsync(request,headers,deadline,cancellationToken),_=>throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_ACTION_INVALID")};
        ContainerRecycleRestoreWire.Receipt(request,result);
        var value=new ContainerRecycleRestoreReceiptDto(operation.Id,Guid.ParseExact(result.SourceOperationId,"N"),result.Fingerprint,result.TargetProof,result.State,result.BindingCurrent,result.Replayed,result.HistoryVerified,result.HistoryDigest,result.HistoryAvailable,
            result.HasSourceState?result.SourceState:null,result.HasSourceRetained?result.SourceRetained:null,result.HasSourceRestored?result.SourceRestored:null,result.HasRestoredByThisOperation?result.RestoredByThisOperation:null);
        ContainerRecycleRestoreValidation.Receipt(operation,value);return value;
    }
    private static Rpc.ContainerRecycleRestoreCommand RestoreCommand(ContainerRecycleRestoreCommandDto c)=>new(){HistoryDigest=c.HistoryDigest,Source=new(){OperationId=c.Source.OperationId.ToString("N"),Owner=c.Source.Owner,Plan=RecyclePlan(c.Source.Plan)}};
    private static ContainerRecycleRestoreCommandDto RestoreCommand(Rpc.ContainerRecycleRestoreCommand c)=>new(new(Guid.ParseExact(c.Source.OperationId,"N"),c.Source.Owner,RecyclePlan(c.Source.Plan)),c.HistoryDigest);
    private static Rpc.ContainerRecycleRestorePlan RestorePlan(ContainerRecycleRestorePlanDto p)=>new(){SchemaVersion=p.SchemaVersion,Command=RestoreCommand(p.Command),TargetProof=p.TargetProof,ParentIdentity=new(){Device=p.ParentIdentity.Device,Inode=p.ParentIdentity.Inode},ParentVersion=p.ParentVersion,OwnerUid=p.OwnerUid,OwnerGid=p.OwnerGid,
        PrivateIdentity=new(){Device=p.PrivateIdentity.Device,Inode=p.PrivateIdentity.Inode},RetainedIdentity=new(){Device=p.RetainedIdentity.Device,Inode=p.RetainedIdentity.Inode,Mode=p.RetainedIdentity.Mode,OwnerUid=p.RetainedIdentity.OwnerUid,OwnerGid=p.RetainedIdentity.OwnerGid,LinkCount=p.RetainedIdentity.LinkCount,SizeBytes=p.RetainedIdentity.SizeBytes,ModifiedAtUnixNs=p.RetainedIdentity.ModifiedAtUnixNs},RetainedChangedAtUnixNs=p.RetainedChangedAtUnixNs,Fingerprint=p.Fingerprint};
    private static ContainerRecycleRestorePlanDto RestorePlan(Rpc.ContainerRecycleRestorePlan p)
    {
        if(p.Command?.Source?.Plan is null||p.ParentIdentity is null||p.PrivateIdentity is null||p.RetainedIdentity is null)throw ContainerRecycleRestoreValidation.Invalid();
        var i=p.RetainedIdentity;var value=new ContainerRecycleRestorePlanDto(p.SchemaVersion,RestoreCommand(p.Command),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,new(p.PrivateIdentity.Device,p.PrivateIdentity.Inode),new(i.Device,i.Inode,i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount,i.SizeBytes,i.ModifiedAtUnixNs),p.RetainedChangedAtUnixNs,p.Fingerprint);
        try{ContainerRecycleRestoreValidation.Plan(value);}catch(ArgumentException){throw ContainerRecycleRestoreValidation.Invalid();}return value;
    }
}
