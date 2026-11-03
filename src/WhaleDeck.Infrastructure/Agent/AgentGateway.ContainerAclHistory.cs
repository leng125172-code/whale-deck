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

public sealed partial class AgentGateway : IContainerAclHistoryGateway
{
    public async Task RequireContainerAclHistoryAsync(CancellationToken cancellationToken)=>_=await AclHistoryClient(cancellationToken);
    private async Task<Rpc.ContainerAclHistoryService.ContainerAclHistoryServiceClient> AclHistoryClient(CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps=await GetCapabilitiesAsync(timeout.Token);
        if(caps.ProtocolMajor!=1 || caps.ProtocolMinor<52 || !caps.Capabilities.Contains("docker.files.acl-history"))
            throw new RpcException(new(StatusCode.Unimplemented,"CONTAINER_ACL_HISTORY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata AclHistoryHeaders(string action,IMessage request)
    {
        if(_capabilityKey is not {Length:>=32})throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_ACL_HISTORY_CAPABILITY_UNAVAILABLE"));
        var method=ContainerAclHistoryBinding.Service+action;var hash=ContainerAclHistoryBinding.Hash(request);
        var expires=DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        var mac=Convert.ToHexString(HMACSHA256.HashData(_capabilityKey,Encoding.UTF8.GetBytes(ContainerAclHistoryBinding.Payload(expires,nonce,method,hash))));
        return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+mac},{ContainerAclHistoryBinding.Header,hash}};
    }
    public async Task<ContainerAclHistoryPageDto> ListContainerAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken cancellationToken)
    {
        ContainerAclHistoryValidation.Actor(actor);ContainerAclHistoryValidation.List(location,after,take,expectedVersion);
        var request=new Rpc.ContainerAclHistoryListRequest{Location=ContainerLocation(location),After=after,Take=(uint)take,ExpectedVersion=expectedVersion,Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await AclHistoryClient(cancellationToken);
        var value=await client.ListAsync(request,AclHistoryHeaders("List",request),deadline:DateTime.UtcNow.AddSeconds(30),cancellationToken:cancellationToken);
        if(!value.HasScannedCount || !value.HasInvalidCount || !value.HasComplete || value.Location is null || value.Entries.Count>50 || value.ScannedCount>200 || value.InvalidCount>value.ScannedCount || value.CalculateSize()>60*1024)throw ContainerAclHistoryValidation.Invalid();
        var page=new ContainerAclHistoryPageDto(new(value.Location.ContainerId,value.Location.InstanceId,value.Location.Path),value.Version,value.Entries.Select(AclHistoryEntry).ToArray(),value.NextAfter,(int)value.ScannedCount,(int)value.InvalidCount,value.Complete);
        ContainerAclHistoryValidation.Page(page,location,after,take,expectedVersion);return page;
    }
    public async Task<ContainerAclHistorySnapshotDto> ReadContainerAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string expectedHistoryDigest,CancellationToken cancellationToken)
    {
        ContainerAclHistoryValidation.Actor(actor);ContainerAclHistoryValidation.Read(location,captureId,expectedHistoryDigest);
        var request=new Rpc.ContainerAclHistoryReadRequest{Location=ContainerLocation(location),CaptureId=captureId,ExpectedHistoryDigest=expectedHistoryDigest,Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await AclHistoryClient(cancellationToken);
        var value=await client.ReadAsync(request,AclHistoryHeaders("Read",request),deadline:DateTime.UtcNow.AddSeconds(30),cancellationToken:cancellationToken);
        if(value.Location is null || value.Entry is null || value.AccessAcl.Count>64 || value.DefaultAcl.Count>64 || value.CalculateSize()>24*1024)throw ContainerAclHistoryValidation.Invalid();
        var result=new ContainerAclHistorySnapshotDto(new(value.Location.ContainerId,value.Location.InstanceId,value.Location.Path),AclHistoryEntry(value.Entry),value.AccessAcl.Select(AclHistoryAclEntry).ToArray(),value.DefaultAcl.Select(AclHistoryAclEntry).ToArray());
        ContainerAclHistoryValidation.Snapshot(result,location,captureId,expectedHistoryDigest);return result;
    }
    private static ContainerAclHistoryEntryDto AclHistoryEntry(Rpc.ContainerAclHistoryEntry value)
    {
        if(!value.HasOriginalMode || !value.HasCreatedAtUnixMs || !value.HasAccessEntryCount || !value.HasDefaultEntryCount)throw ContainerAclHistoryValidation.Invalid();
        return new(value.CaptureId,value.HistoryDigest,value.CreatedAtUnixMs,value.SourceInstanceId,value.ChangedAclType,value.Kind,value.OriginalMode,value.AccessEntryCount,value.DefaultEntryCount);
    }
    private static ContainerAclEntryDto AclHistoryAclEntry(Rpc.ContainerAclEntry value)
    { if(!value.HasPermissions)throw ContainerAclHistoryValidation.Invalid();return new(value.Tag,value.HasId?value.Id:null,value.Permissions); }
}
