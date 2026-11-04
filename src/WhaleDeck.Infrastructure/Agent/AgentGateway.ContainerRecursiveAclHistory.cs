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

public sealed partial class AgentGateway : IContainerRecursiveAclHistoryGateway
{
    public async Task RequireContainerRecursiveAclHistoryAsync(CancellationToken cancellationToken)=>_=await RecursiveAclHistoryClient(cancellationToken);
    private async Task<Rpc.ContainerRecursiveAclHistoryService.ContainerRecursiveAclHistoryServiceClient> RecursiveAclHistoryClient(CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps=await GetCapabilitiesAsync(timeout.Token);
        if(caps.ProtocolMajor!=1 || caps.ProtocolMinor<54 || !caps.Capabilities.Contains("docker.files.recursive-acl-history"))
            throw new RpcException(new(StatusCode.Unimplemented,"CONTAINER_RECURSIVE_ACL_HISTORY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecursiveAclHistoryHeaders(string action,IMessage request)
    {
        if(_capabilityKey is not {Length:>=32})throw new RpcException(new(StatusCode.PermissionDenied,"CONTAINER_RECURSIVE_ACL_HISTORY_CAPABILITY_UNAVAILABLE"));
        var method=ContainerRecursiveAclHistoryBinding.Service+action;var hash=ContainerRecursiveAclHistoryBinding.Hash(request);
        var expires=DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        var mac=Convert.ToHexString(HMACSHA256.HashData(_capabilityKey,Encoding.UTF8.GetBytes(ContainerRecursiveAclHistoryBinding.Payload(expires,nonce,method,hash))));
        return new(){{"x-whaledeck-capability",expires+"."+nonce+"."+mac},{ContainerRecursiveAclHistoryBinding.Header,hash}};
    }
    public async Task<ContainerRecursiveAclHistoryPageDto> ListContainerRecursiveAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken cancellationToken)
    {
        ContainerAclHistoryValidation.Actor(actor);ContainerRecursiveAclHistoryValidation.List(location,after,take,expectedVersion);
        var request=new Rpc.ContainerRecursiveAclHistoryListRequest{Location=ContainerLocation(location),After=after,Take=(uint)take,ExpectedVersion=expectedVersion,Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await RecursiveAclHistoryClient(cancellationToken);
        var value=await client.ListAsync(request,RecursiveAclHistoryHeaders("List",request),deadline:DateTime.UtcNow.AddSeconds(30),cancellationToken:cancellationToken);
        if(!value.HasScannedCount || !value.HasInvalidCount || !value.HasComplete || value.Location is null || value.Entries.Count>50 || value.ScannedCount>200 || value.InvalidCount>value.ScannedCount || value.CalculateSize()>128*1024)throw ContainerRecursiveAclHistoryValidation.Invalid();
        var page=new ContainerRecursiveAclHistoryPageDto(new(value.Location.ContainerId,value.Location.InstanceId,value.Location.Path),value.Version,value.Entries.Select(RecursiveAclHistoryEntry).ToArray(),value.NextAfter,(int)value.ScannedCount,(int)value.InvalidCount,value.Complete);
        ContainerRecursiveAclHistoryValidation.Page(page,location,after,take,expectedVersion);return page;
    }
    public async Task<ContainerRecursiveAclHistorySnapshotDto> ReadContainerRecursiveAclHistoryAsync(TerminalActor actor,ContainerFileLocationDto location,string captureId,string expectedHistoryDigest,int offset,int take,CancellationToken cancellationToken)
    {
        ContainerAclHistoryValidation.Actor(actor);ContainerRecursiveAclHistoryValidation.Read(location,captureId,expectedHistoryDigest,offset,take);
        var request=new Rpc.ContainerRecursiveAclHistoryReadRequest{Location=ContainerLocation(location),CaptureId=captureId,ExpectedHistoryDigest=expectedHistoryDigest,Offset=(uint)offset,Take=(uint)take,Context=new(){ActorSubject=actor.Subject,TraceId=actor.TraceId}};
        var client=await RecursiveAclHistoryClient(cancellationToken);
        var value=await client.ReadAsync(request,RecursiveAclHistoryHeaders("Read",request),deadline:DateTime.UtcNow.AddSeconds(30),cancellationToken:cancellationToken);
        if(value.Location is null || value.Summary is null || !value.HasOffset || !value.HasComplete || value.Entries.Count>50 || value.CalculateSize()>128*1024)throw ContainerRecursiveAclHistoryValidation.Invalid();
        var result=new ContainerRecursiveAclHistorySnapshotDto(new(value.Location.ContainerId,value.Location.InstanceId,value.Location.Path),RecursiveAclHistoryEntry(value.Summary),value.Offset,
            value.Entries.Select(RecursiveAclHistoryItem).ToArray(),value.Complete,value.HasNextOffset?value.NextOffset:null);
        ContainerRecursiveAclHistoryValidation.Snapshot(result,location,captureId,expectedHistoryDigest,offset,take);return result;
    }
    private static ContainerRecursiveAclHistorySummaryDto RecursiveAclHistoryEntry(Rpc.ContainerRecursiveAclHistorySummary value)
    {
        if(!value.HasCreatedAtUnixMs || !value.HasEntryCount || !value.HasFileCount || !value.HasDirectoryCount || !value.HasChangeCount)throw ContainerRecursiveAclHistoryValidation.Invalid();
        return new(value.CaptureId,value.HistoryDigest,value.CreatedAtUnixMs,value.SourceInstanceId,value.ChangedAclType,value.ManifestDigest,value.EntryCount,value.FileCount,value.DirectoryCount,value.ChangeCount);
    }
    private static ContainerRecursiveAclHistoryEntryDto RecursiveAclHistoryItem(Rpc.ContainerRecursiveAclHistoryEntry value)
    {
        if(!value.HasOriginalMode || value.AccessAcl.Count>64 || value.DefaultAcl.Count>64)throw ContainerRecursiveAclHistoryValidation.Invalid();
        return new(value.RelativePath,value.Kind,value.OriginalMode,value.AccessAcl.Select(RecursiveAclHistoryAclEntry).ToArray(),value.DefaultAcl.Select(RecursiveAclHistoryAclEntry).ToArray());
    }
    private static ContainerAclEntryDto RecursiveAclHistoryAclEntry(Rpc.ContainerAclEntry value)
    { if(!value.HasPermissions)throw ContainerRecursiveAclHistoryValidation.Invalid();return new(value.Tag,value.HasId?value.Id:null,value.Permissions); }
}
