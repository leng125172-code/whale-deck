using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Agent;
using App=WhaleDeck.Application.Models;
using Rpc=WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveGatewayTests
{
    [Fact]
    public async Task RehashedOuterPlanCannotHideAnInvalidNestedPurgeFingerprint()
    {
        using var h=new Harness();var p=ContainerRecycleArchivePolicyTests.Plan();p.Command.Purge.Plan.Fingerprint=new('F',64);p.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(p);var old=Operation();
        await Assert.ThrowsAsync<ArgumentException>(()=>h.Gateway.ApplyContainerRecycleArchiveAsync(old with{Plan=Plan(p)},default));Assert.Null(h.Request);
    }
    internal static App.ContainerRecycleArchivePlanDto Plan(Rpc.ContainerRecycleArchivePlan p)=>new(p.SchemaVersion,new(new(Guid.ParseExact(p.Command.Purge.OperationId,"N"),p.Command.Purge.Owner,ContainerRecyclePurgeGatewayTests.Plan(p.Command.Purge.Plan)),p.Command.HistoryDigest,p.Command.Mode),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,p.PrivateIdentity is null?null:new(p.PrivateIdentity.Device,p.PrivateIdentity.Inode),Identity(p.DeletedIdentity),p.DeletedChangedAtUnixNs,p.SourceRecordDigest,p.PurgeRecordDigest,p.PurgeMarkerDigest,p.HistoryMetadataDigest,p.StorageHash,new(p.StoreIdentity.Device,p.StoreIdentity.Inode),Identity(p.ArchiveIdentity),p.ArchiveChangedAtUnixNs,p.ArchiveBytes,p.ArchiveSha256,p.Fingerprint);
    private static App.FileObjectIdentityDto Identity(Rpc.FileObjectIdentity i)=>new(i.Device,i.Inode,i.Mode,i.OwnerUid,i.OwnerGid,i.LinkCount,i.SizeBytes,i.ModifiedAtUnixNs);
    private static App.TerminalActor Actor=>new("synthetic-admin",null,[],new('F',64),"synthetic-trace",null);
    private static App.ContainerRecycleArchiveOperationDto Operation()
    {var q=ContainerRecycleArchivePolicyTests.Request();return new(Guid.ParseExact(q.OperationId,"N"),q.Owner,Plan(q.Plan),new(q.Context.ActorSubject,q.Context.TraceId,Guid.ParseExact(q.Context.JobId,"N"),Guid.ParseExact(q.Context.IdempotencyKey,"N")));}
    [Theory]
    [InlineData(1U,58U,true)][InlineData(2U,59U,true)][InlineData(1U,59U,false)]
    public async Task OlderVersionOrMissingCapabilityRefusesBeforeOperation(uint major,uint minor,bool available)
    {using var h=new Harness(major,minor,available);await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.PlanContainerRecycleArchiveAsync(Actor,Operation().Plan.Command,default));Assert.Null(h.Request);}
    [Fact]
    public async Task PlanFreezesNestedByteArraysBeforeCapabilitiesAndSignsAllFields()
    {
        using var h=new Harness();var command=Operation().Plan.Command;var original=command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToArray();h.OnCapabilities=()=>command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0]^=42;
        var p=await h.Gateway.PlanContainerRecycleArchiveAsync(Actor,command,default);var q=Assert.IsType<Rpc.ContainerRecycleArchivePlanRequest>(h.Request);Assert.Equal(original,q.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value.ToByteArray());Assert.Equal(original,p.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value);h.AssertSignature();
    }
    [Theory]
    [InlineData("Reserve","Reserved")][InlineData("Apply","Completed")][InlineData("Inspect","Completed")][InlineData("Discard","Aborted")]
    public async Task ActionsKeepSourceAndNewJobAuthorityDistinct(string action,string state)
    {
        using var h=new Harness();var op=Operation();var r=action switch{"Reserve"=>await h.Gateway.ReserveContainerRecycleArchiveAsync(op,default),"Apply"=>await h.Gateway.ApplyContainerRecycleArchiveAsync(op,default),"Inspect"=>await h.Gateway.InspectContainerRecycleArchiveAsync(op,default),_=>await h.Gateway.DiscardContainerRecycleArchiveAsync(op,default)};
        Assert.NotNull(r);Assert.Equal(state,r.State);Assert.Equal(op.Id,r.Id);Assert.Equal(op.Plan.Command.Purge.Plan.Command.Source.OperationId,r.SourceOperationId);var q=Assert.IsType<Rpc.ContainerRecycleArchiveRequest>(h.Request);Assert.Equal(q.OperationId,q.Context.JobId);Assert.NotEqual(q.OperationId,q.Plan.Command.Purge.Plan.Command.Source.OperationId);h.AssertSignature();
    }
    [Fact]
    public async Task UnknownDoesNotBecomeFalseAndOriginalSourceIdCannotExecute()
    {using var h=new Harness{Unknown=true};Assert.Null((await h.Gateway.ApplyContainerRecycleArchiveAsync(Operation(),default)).ArchiveCopied);using var bad=new Harness();var op=Operation();await Assert.ThrowsAsync<ArgumentException>(()=>bad.Gateway.ApplyContainerRecycleArchiveAsync(op with{Id=op.Plan.Command.Purge.Plan.Command.Source.OperationId},default));Assert.Null(bad.Request);}
    [Theory]
    [InlineData(StatusCode.NotFound,"CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND",true)][InlineData(StatusCode.NotFound,"CONTAINER_RECYCLE_NOT_FOUND",false)][InlineData(StatusCode.Unavailable,"CONTAINER_RECYCLE_ARCHIVE_UNAVAILABLE",false)]
    public async Task OnlyExactPrivateAbsenceBecomesNull(StatusCode status,string detail,bool absent)
    {using var h=new Harness{Status=status,Detail=detail};if(absent)Assert.Null(await h.Gateway.InspectContainerRecycleArchiveAsync(Operation(),default));else await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.InspectContainerRecycleArchiveAsync(Operation(),default));}
    [Theory]
    [InlineData("source")][InlineData("id")][InlineData("digest")][InlineData("presence")][InlineData("state")]
    public async Task CorruptReceiptsNeverBecomeSuccessfulArchiveCopy(string corrupt)
    {using var h=new Harness{Corrupt=corrupt};await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Gateway.ApplyContainerRecycleArchiveAsync(Operation(),default));}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task NeitherOriginalSourceNorPurgeIdCanBecomeTheNewExecutionEvenWithMatchingJob(bool purge)
    {
        using var h=new Harness();var op=Operation();var id=purge?op.Plan.Command.Purge.OperationId:op.Plan.Command.Purge.Plan.Command.Source.OperationId;
        await Assert.ThrowsAsync<ArgumentException>(()=>h.Gateway.ApplyContainerRecycleArchiveAsync(op with{Id=id,Context=op.Context with{JobId=id}},default));Assert.Null(h.Request);
    }
    [Fact]
    public async Task ForeignCurrentClaimPreservesHistoricalCompletedAndClearsBindingProof()
    {using var h=new Harness{Corrupt="foreign-claim"};var result=await h.Gateway.InspectContainerRecycleArchiveAsync(Operation(),default);Assert.NotNull(result);Assert.True(result.ArchiveCopied);Assert.False(result.OwnedFence);Assert.False(result.BindingCurrent);}
    [Theory]
    [InlineData("plan-private")][InlineData("plan-source")][InlineData("plan-missing")]
    public async Task CorruptPlansFailClosed(string corrupt)
    {using var h=new Harness{Corrupt=corrupt};await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Gateway.PlanContainerRecycleArchiveAsync(Actor,Operation().Plan.Command,default));}

    private sealed class Harness:HttpMessageHandler
    {
        private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);private readonly string _file=Path.GetTempFileName();private readonly uint _major,_minor;private readonly bool _available;
        public AgentGateway Gateway{get;}public IMessage? Request{get;private set;}public Action? OnCapabilities{get;set;}public bool Unknown{get;init;}public string? Corrupt{get;init;}public StatusCode Status{get;init;}=StatusCode.OK;public string Detail{get;init;}="";
        private string _method="",_token="",_hash="";
        public Harness(uint major=1,uint minor=59,bool available=true)
        {
            _major=major;_minor=minor;_available=available;File.WriteAllBytes(_file,_key);Gateway=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=_file}).Build());
            var field=typeof(AgentGateway).GetField("_channel",BindingFlags.Instance|BindingFlags.NonPublic)!;((GrpcChannel)field.GetValue(Gateway)!).Dispose();var channel=GrpcChannel.ForAddress("http://127.0.0.1",new(){HttpHandler=this,DisposeHttpClient=false});field.SetValue(Gateway,channel);
            typeof(AgentGateway).GetField("_agent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Gateway,new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request);Assert.Equal(ContainerRecycleArchiveBinding.Hash(Request),_hash);var parts=_token.Split('.');Assert.Equal(3,parts.Length);Assert.InRange(long.Parse(parts[0],CultureInfo.InvariantCulture)-DateTimeOffset.UtcNow.ToUnixTimeSeconds(),1,60);
            var expected=HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(ContainerRecycleArchiveBinding.Payload(parts[0],parts[1],_method,_hash)));Assert.Equal(Convert.ToHexString(expected),parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.AbsolutePath;var caps=path.EndsWith("/GetCapabilities",StringComparison.Ordinal);IMessage? frame;
            if(caps){OnCapabilities?.Invoke();var response=new Rpc.CapabilitiesResponse{ProtocolMajor=_major,ProtocolMinor=_minor};if(_available)response.Capabilities.Add("docker.files.recycle.archive");frame=response;}
            else
            {
                _method=path;_token=Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));_hash=Assert.Single(request.Headers.GetValues(ContainerRecycleArchiveBinding.Header));var raw=await request.Content!.ReadAsByteArrayAsync(cancellationToken);Assert.Equal(0,raw[0]);Assert.Equal(raw.Length-5,BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1,4)));
                if(path.EndsWith("/Plan",StringComparison.Ordinal))
                {
                    Request=Rpc.ContainerRecycleArchivePlanRequest.Parser.ParseFrom(raw.AsSpan(5));var p=ContainerRecycleArchivePolicyTests.Plan();
                    if(Corrupt=="plan-private")p.PrivateIdentity=new(){Device=0,Inode=1};if(Corrupt=="plan-source"){p.Command.Purge.Owner=new('F',64);p.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(p);}if(Corrupt=="plan-missing")p.DeletedIdentity=null;frame=p;
                }
                else
                {
                    var q=Rpc.ContainerRecycleArchiveRequest.Parser.ParseFrom(raw.AsSpan(5));Request=q;using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleArchivePolicyTests.ReceiptInput(q,Unknown?"Unknown":path.EndsWith("/Reserve",StringComparison.Ordinal)?"Reserved":path.EndsWith("/Discard",StringComparison.Ordinal)?"Aborted":"Completed")));var r=ContainerRecycleArchivePolicy.Receipt(json.RootElement,q);
                    switch(Corrupt){case "source":r.SourceOperationId=q.OperationId;break;case "id":r.OperationId=q.Plan.Command.Purge.Plan.Command.Source.OperationId;break;case "digest":r.Fingerprint=new('6',64);break;case "presence":r.ClearArchiveCopied();break;case "state":r.State="Applying";break;case "foreign-claim":r.OwnedFence=false;r.BindingCurrent=false;break;}frame=r;
                }
                if(Status!=StatusCode.OK)frame=null;
            }
            using var body=new MemoryStream();if(frame is not null){var bytes=frame.ToByteArray();var prefix=new byte[5];BinaryPrimitives.WriteInt32BigEndian(prefix.AsSpan(1),bytes.Length);body.Write(prefix);body.Write(bytes);}
            var result=new HttpResponseMessage(HttpStatusCode.OK){Version=HttpVersion.Version20,Content=new ByteArrayContent(body.ToArray())};result.Content.Headers.ContentType=new MediaTypeHeaderValue("application/grpc");result.TrailingHeaders.Add("grpc-status",((int)(caps?StatusCode.OK:Status)).ToString(CultureInfo.InvariantCulture));if(!caps&&Detail.Length>0)result.TrailingHeaders.Add("grpc-message",Uri.EscapeDataString(Detail));return result;
        }
        protected override void Dispose(bool disposing)
        {if(disposing){Gateway.Dispose();if(typeof(AgentGateway).GetField("_capabilityKey",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Gateway)is byte[] key)CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(_key);File.Delete(_file);}base.Dispose(disposing);}
    }
}
