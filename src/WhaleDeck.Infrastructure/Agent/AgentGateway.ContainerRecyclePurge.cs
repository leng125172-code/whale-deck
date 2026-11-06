using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using Rpc=WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerRecyclePurgeGateway
{
    public async Task RequireContainerRecyclePurgeAsync(CancellationToken cancellationToken)=>_ = await RecyclePurgeClient(cancellationToken);
    private async Task<Rpc.ContainerRecyclePurgeService.ContainerRecyclePurgeServiceClient> RecyclePurgeClient(CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));var caps=await GetCapabilitiesAsync(timeout.Token);
        if(caps.ProtocolMajor!=1||caps.ProtocolMinor<58||!caps.Capabilities.Contains("docker.files.recycle.purge"))throw new RpcException(new(StatusCode.Unimplemented,"CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE"));return new(_channel);
    }
    private Metadata RecyclePurgeHeaders(string action,IMessage request)
    {
        if(_capabilityKey is not {Length:>=32})throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE"));
        var hash=ContainerRecyclePurgeBinding.Hash(request);var expires=DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        var signature=HMACSHA256.HashData(_capabilityKey,Encoding.UTF8.GetBytes(ContainerRecyclePurgeBinding.Payload(expires,nonce,ContainerRecyclePurgeBinding.Service+action,hash)));
        return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(signature)},{ContainerRecyclePurgeBinding.Header,hash}};
    }
    public async Task<ContainerRecyclePurgePlanDto> PlanContainerRecyclePurgeAsync(TerminalActor actor,ContainerRecyclePurgeCommandDto command,CancellationToken cancellationToken)
    {
        ContainerRecyclePurgeValidation.Command(command);
        if(string.IsNullOrWhiteSpace(actor.Subject)||actor.Subject.Length>256||actor.Subject.Any(char.IsControl)||string.IsNullOrWhiteSpace(actor.TraceId)||actor.TraceId.Length>128||actor.TraceId.Any(char.IsControl))throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONTEXT_INVALID");
        var request=new Rpc.ContainerRecyclePurgePlanRequest{Command=PurgeCommand(command),Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await RecyclePurgeClient(cancellationToken);var result=await client.PlanAsync(request,RecyclePurgeHeaders("Plan",request),deadline:DateTime.UtcNow.AddSeconds(120),cancellationToken:cancellationToken);
        try{ContainerRecyclePurgeWire.RequireKnown(result);if(!result.Command.Equals(request.Command)||result.Fingerprint!=ContainerRecyclePurgeWire.Fingerprint(result))throw ContainerRecyclePurgeValidation.Invalid();return PurgePlan(result);}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or FormatException or NullReferenceException){throw ContainerRecyclePurgeValidation.Invalid();}
    }
    public Task<ContainerRecyclePurgeReceiptDto> ReserveContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken)=>PurgeAction("Reserve",operation,cancellationToken);
    public Task<ContainerRecyclePurgeReceiptDto> ApplyContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken)=>PurgeAction("Apply",operation,cancellationToken);
    public Task<ContainerRecyclePurgeReceiptDto> DiscardContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken)=>PurgeAction("Discard",operation,cancellationToken);
    public async Task<ContainerRecyclePurgeReceiptDto?> InspectContainerRecyclePurgeAsync(ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken)
    {try{return await PurgeAction("Inspect",operation,cancellationToken);}catch(RpcException e)when(e.StatusCode==StatusCode.NotFound&&e.Status.Detail=="CONTAINER_RECYCLE_PURGE_NOT_FOUND"){return null;}}
    private async Task<ContainerRecyclePurgeReceiptDto> PurgeAction(string action,ContainerRecyclePurgeOperationDto operation,CancellationToken cancellationToken)
    {
        ContainerRecyclePurgeValidation.Operation(operation);var request=new Rpc.ContainerRecyclePurgeRequest{OperationId=operation.Id.ToString("N"),Owner=operation.Owner,Plan=PurgePlan(operation.Plan),Context=UploadContext(operation.Context)};
        if(request.Plan.Fingerprint!=ContainerRecyclePurgeWire.Fingerprint(request.Plan))throw new ArgumentException("CONTAINER_RECYCLE_PURGE_PLAN_INVALID");_ = PurgePlan(request.Plan);
        var client=await RecyclePurgeClient(cancellationToken);var headers=RecyclePurgeHeaders(action,request);var deadline=DateTime.UtcNow.AddSeconds(120);
        var result=action switch{"Reserve"=>await client.ReserveAsync(request,headers,deadline,cancellationToken),"Apply"=>await client.ApplyAsync(request,headers,deadline,cancellationToken),"Inspect"=>await client.InspectAsync(request,headers,deadline,cancellationToken),"Discard"=>await client.DiscardAsync(request,headers,deadline,cancellationToken),_=>throw new ArgumentException("CONTAINER_RECYCLE_PURGE_ACTION_INVALID")};
        ContainerRecyclePurgeWire.Receipt(request,result);var value=new ContainerRecyclePurgeReceiptDto(operation.Id,Guid.ParseExact(result.SourceOperationId,"N"),result.Fingerprint,result.State,result.SourceFenced,result.OwnedFence,result.HasPurgeApplied?result.PurgeApplied:null);ContainerRecyclePurgeValidation.Receipt(operation,value);return value;
    }
    private static Rpc.ContainerRecyclePurgeCommand PurgeCommand(ContainerRecyclePurgeCommandDto c)=>new(){HistoryDigest=c.HistoryDigest,Scope=c.Scope,Source=new(){OperationId=c.Source.OperationId.ToString("N"),Owner=c.Source.Owner,Plan=RecyclePlan(c.Source.Plan)}};
    private static ContainerRecyclePurgeCommandDto PurgeCommand(Rpc.ContainerRecyclePurgeCommand c)=>new(new(Guid.ParseExact(c.Source.OperationId,"N"),c.Source.Owner,RecyclePlan(c.Source.Plan)),c.HistoryDigest,c.Scope);
    private static Rpc.FileObjectIdentity PurgeIdentity(FileObjectIdentityDto i)=>new(){Device=i.Device,Inode=i.Inode,Mode=i.Mode,OwnerUid=i.OwnerUid,OwnerGid=i.OwnerGid,LinkCount=i.LinkCount,SizeBytes=i.SizeBytes,ModifiedAtUnixNs=i.ModifiedAtUnixNs};
    private static FileObjectIdentityDto PurgeIdentity(Rpc.FileObjectIdentity i)=>new(i.Device,i.Inode,i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount,i.SizeBytes,i.ModifiedAtUnixNs);
    private static Rpc.ContainerRecyclePurgePlan PurgePlan(ContainerRecyclePurgePlanDto p)=>new(){SchemaVersion=p.SchemaVersion,Command=PurgeCommand(p.Command),TargetProof=p.TargetProof,ParentIdentity=new(){Device=p.ParentIdentity.Device,Inode=p.ParentIdentity.Inode},ParentVersion=p.ParentVersion,OwnerUid=p.OwnerUid,OwnerGid=p.OwnerGid,PrivateIdentity=new(){Device=p.PrivateIdentity.Device,Inode=p.PrivateIdentity.Inode},RetainedIdentity=PurgeIdentity(p.RetainedIdentity),RetainedChangedAtUnixNs=p.RetainedChangedAtUnixNs,SourceRecordDigest=p.SourceRecordDigest,HistoryMetadataDigest=p.HistoryMetadataDigest,HistoryCreatedAtUnixMs=p.HistoryCreatedAtUnixMs,StorageHash=p.StorageHash,StoreIdentity=new(){Device=p.StoreIdentity.Device,Inode=p.StoreIdentity.Inode},ArchiveIdentity=PurgeIdentity(p.ArchiveIdentity),ArchiveChangedAtUnixNs=p.ArchiveChangedAtUnixNs,ArchiveBytes=p.ArchiveBytes,ArchiveSha256=p.ArchiveSha256,Fingerprint=p.Fingerprint};
    private static ContainerRecyclePurgePlanDto PurgePlan(Rpc.ContainerRecyclePurgePlan p)
    {
        if(p.Command?.Source?.Plan is null||p.ParentIdentity is null||p.PrivateIdentity is null||p.RetainedIdentity is null||p.StoreIdentity is null||p.ArchiveIdentity is null)throw ContainerRecyclePurgeValidation.Invalid();
        var value=new ContainerRecyclePurgePlanDto(p.SchemaVersion,PurgeCommand(p.Command),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,new(p.PrivateIdentity.Device,p.PrivateIdentity.Inode),PurgeIdentity(p.RetainedIdentity),p.RetainedChangedAtUnixNs,p.SourceRecordDigest,p.HistoryMetadataDigest,p.HistoryCreatedAtUnixMs,p.StorageHash,new(p.StoreIdentity.Device,p.StoreIdentity.Inode),PurgeIdentity(p.ArchiveIdentity),p.ArchiveChangedAtUnixNs,p.ArchiveBytes,p.ArchiveSha256,p.Fingerprint);
        try{ContainerRecyclePurgeValidation.Plan(value);}catch(ArgumentException){throw ContainerRecyclePurgeValidation.Invalid();}return value;
    }
}
