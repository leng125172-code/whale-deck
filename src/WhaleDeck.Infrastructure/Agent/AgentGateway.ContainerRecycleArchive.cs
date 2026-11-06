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

public sealed partial class AgentGateway : IContainerRecycleArchiveGateway
{
    public async Task RequireContainerRecycleArchiveAsync(CancellationToken cancellationToken)=>_ = await RecycleArchiveClient(cancellationToken);
    private async Task<Rpc.ContainerRecycleArchiveService.ContainerRecycleArchiveServiceClient> RecycleArchiveClient(CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));var caps=await GetCapabilitiesAsync(timeout.Token);
        if(caps.ProtocolMajor!=1||caps.ProtocolMinor<59||!caps.Capabilities.Contains("docker.files.recycle.archive"))throw new RpcException(new(StatusCode.Unimplemented,"CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE"));return new(_channel);
    }
    private Metadata RecycleArchiveHeaders(string action,IMessage request)
    {
        if(_capabilityKey is not {Length:>=32})throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE"));
        var hash=ContainerRecycleArchiveBinding.Hash(request);var expires=DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        var signature=HMACSHA256.HashData(_capabilityKey,Encoding.UTF8.GetBytes(ContainerRecycleArchiveBinding.Payload(expires,nonce,ContainerRecycleArchiveBinding.Service+action,hash)));
        return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+Convert.ToHexString(signature)},{ContainerRecycleArchiveBinding.Header,hash}};
    }
    public async Task<ContainerRecycleArchivePlanDto> PlanContainerRecycleArchiveAsync(TerminalActor actor,ContainerRecycleArchiveCommandDto command,CancellationToken cancellationToken)
    {
        ContainerRecycleArchiveValidation.Command(command);
        if(string.IsNullOrWhiteSpace(actor.Subject)||actor.Subject.Length>256||actor.Subject.Any(char.IsControl)||string.IsNullOrWhiteSpace(actor.TraceId)||actor.TraceId.Length>128||actor.TraceId.Any(char.IsControl))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONTEXT_INVALID");
        var request=new Rpc.ContainerRecycleArchivePlanRequest{Command=ArchiveCommand(command),Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        VerifyArchiveCommand(request.Command);
        var client=await RecycleArchiveClient(cancellationToken);var result=await client.PlanAsync(request,RecycleArchiveHeaders("Plan",request),deadline:DateTime.UtcNow.AddSeconds(120),cancellationToken:cancellationToken);
        try{ContainerRecycleArchiveWire.RequireKnown(result);if(!result.Command.Equals(request.Command)||result.Fingerprint!=ContainerRecycleArchiveWire.Fingerprint(result))throw ContainerRecycleArchiveValidation.Invalid();return ArchivePlan(result);}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or FormatException or NullReferenceException){throw ContainerRecycleArchiveValidation.Invalid();}
    }
    public Task<ContainerRecycleArchiveReceiptDto> ReserveContainerRecycleArchiveAsync(ContainerRecycleArchiveOperationDto operation,CancellationToken cancellationToken)=>ArchiveAction("Reserve",operation,cancellationToken);
    public Task<ContainerRecycleArchiveReceiptDto> ApplyContainerRecycleArchiveAsync(ContainerRecycleArchiveOperationDto operation,CancellationToken cancellationToken)=>ArchiveAction("Apply",operation,cancellationToken);
    public Task<ContainerRecycleArchiveReceiptDto> DiscardContainerRecycleArchiveAsync(ContainerRecycleArchiveOperationDto operation,CancellationToken cancellationToken)=>ArchiveAction("Discard",operation,cancellationToken);
    public async Task<ContainerRecycleArchiveReceiptDto?> InspectContainerRecycleArchiveAsync(ContainerRecycleArchiveOperationDto operation,CancellationToken cancellationToken)
    {try{return await ArchiveAction("Inspect",operation,cancellationToken);}catch(RpcException e)when(e.StatusCode==StatusCode.NotFound&&e.Status.Detail=="CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND"){return null;}}
    private async Task<ContainerRecycleArchiveReceiptDto> ArchiveAction(string action,ContainerRecycleArchiveOperationDto operation,CancellationToken cancellationToken)
    {
        ContainerRecycleArchiveValidation.Operation(operation);var request=new Rpc.ContainerRecycleArchiveRequest{OperationId=operation.Id.ToString("N"),Owner=operation.Owner,Plan=ArchivePlan(operation.Plan),Context=UploadContext(operation.Context)};
        VerifyArchiveCommand(request.Plan.Command);
        if(request.Plan.Fingerprint!=ContainerRecycleArchiveWire.Fingerprint(request.Plan))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");_ = ArchivePlan(request.Plan);
        var client=await RecycleArchiveClient(cancellationToken);var headers=RecycleArchiveHeaders(action,request);var deadline=DateTime.UtcNow.AddSeconds(120);
        var result=action switch{"Reserve"=>await client.ReserveAsync(request,headers,deadline,cancellationToken),"Apply"=>await client.ApplyAsync(request,headers,deadline,cancellationToken),"Inspect"=>await client.InspectAsync(request,headers,deadline,cancellationToken),"Discard"=>await client.DiscardAsync(request,headers,deadline,cancellationToken),_=>throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_ACTION_INVALID")};
        ContainerRecycleArchiveWire.Receipt(request,result);var value=new ContainerRecycleArchiveReceiptDto(operation.Id,Guid.ParseExact(result.SourceOperationId,"N"),Guid.ParseExact(result.PurgeOperationId,"N"),result.Fingerprint,result.State,result.SourceFenced,result.OwnedFence,result.HasArchiveCopied?result.ArchiveCopied:null,result.HasBindingCurrent?result.BindingCurrent:null,result.PublishedIdentity is null?null:PurgeIdentity(result.PublishedIdentity),result.HasPublishedChangedAtUnixNs?result.PublishedChangedAtUnixNs:null);ContainerRecycleArchiveValidation.Receipt(operation,value);return value;
    }
    private static void VerifyArchiveCommand(Rpc.ContainerRecycleArchiveCommand c)
    {ContainerRecycleArchiveWire.RequireKnown(c);if(c.Purge.Plan.Fingerprint!=ContainerRecyclePurgeWire.Fingerprint(c.Purge.Plan))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PLAN_INVALID");}
    private static Rpc.ContainerRecycleArchiveCommand ArchiveCommand(ContainerRecycleArchiveCommandDto c)=>new(){HistoryDigest=c.HistoryDigest,Mode=c.Mode,Purge=new(){OperationId=c.Purge.OperationId.ToString("N"),Owner=c.Purge.Owner,Plan=PurgePlan(c.Purge.Plan)}};
    private static ContainerRecycleArchiveCommandDto ArchiveCommand(Rpc.ContainerRecycleArchiveCommand c)=>new(new(Guid.ParseExact(c.Purge.OperationId,"N"),c.Purge.Owner,PurgePlan(c.Purge.Plan)),c.HistoryDigest,c.Mode);
    private static Rpc.ContainerRecycleArchivePlan ArchivePlan(ContainerRecycleArchivePlanDto p)=>new(){SchemaVersion=p.SchemaVersion,Command=ArchiveCommand(p.Command),TargetProof=p.TargetProof,ParentIdentity=new(){Device=p.ParentIdentity.Device,Inode=p.ParentIdentity.Inode},ParentVersion=p.ParentVersion,OwnerUid=p.OwnerUid,OwnerGid=p.OwnerGid,PrivateIdentity=p.PrivateIdentity is null?null:new(){Device=p.PrivateIdentity.Device,Inode=p.PrivateIdentity.Inode},DeletedIdentity=PurgeIdentity(p.DeletedIdentity),DeletedChangedAtUnixNs=p.DeletedChangedAtUnixNs,SourceRecordDigest=p.SourceRecordDigest,PurgeRecordDigest=p.PurgeRecordDigest,PurgeMarkerDigest=p.PurgeMarkerDigest,HistoryMetadataDigest=p.HistoryMetadataDigest,StorageHash=p.StorageHash,StoreIdentity=new(){Device=p.StoreIdentity.Device,Inode=p.StoreIdentity.Inode},ArchiveIdentity=PurgeIdentity(p.ArchiveIdentity),ArchiveChangedAtUnixNs=p.ArchiveChangedAtUnixNs,ArchiveBytes=p.ArchiveBytes,ArchiveSha256=p.ArchiveSha256,Fingerprint=p.Fingerprint};
    private static ContainerRecycleArchivePlanDto ArchivePlan(Rpc.ContainerRecycleArchivePlan p)
    {
        if(p.Command?.Purge?.Plan is null||p.ParentIdentity is null||p.DeletedIdentity is null||p.StoreIdentity is null||p.ArchiveIdentity is null)throw ContainerRecycleArchiveValidation.Invalid();
        var v=new ContainerRecycleArchivePlanDto(p.SchemaVersion,ArchiveCommand(p.Command),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,p.PrivateIdentity is null?null:new(p.PrivateIdentity.Device,p.PrivateIdentity.Inode),PurgeIdentity(p.DeletedIdentity),p.DeletedChangedAtUnixNs,p.SourceRecordDigest,p.PurgeRecordDigest,p.PurgeMarkerDigest,p.HistoryMetadataDigest,p.StorageHash,new(p.StoreIdentity.Device,p.StoreIdentity.Inode),PurgeIdentity(p.ArchiveIdentity),p.ArchiveChangedAtUnixNs,p.ArchiveBytes,p.ArchiveSha256,p.Fingerprint);
        try{ContainerRecycleArchiveValidation.Plan(v);}catch(ArgumentException){throw ContainerRecycleArchiveValidation.Invalid();}return v;
    }
}
