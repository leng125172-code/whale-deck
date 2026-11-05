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

public sealed class ContainerRecycleRestoreGatewayTests
{
    internal static App.ContainerRecyclePlanDto SourcePlan(Rpc.ContainerRecyclePlan p)=>new(p.SchemaVersion,new(new(p.Command.Location.ContainerId,p.Command.Location.InstanceId,p.Command.Location.Path),p.Command.ExpectedVersion),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,p.Fingerprint,
        new(p.SourceIdentity.Device,p.SourceIdentity.Inode,p.SourceIdentity.Mode,p.SourceIdentity.OwnerUid,p.SourceIdentity.OwnerGid,p.SourceIdentity.LinkCount,p.SourceIdentity.SizeBytes,p.SourceIdentity.ModifiedAtUnixNs),p.SourceChangedAtUnixNs,p.Sha256,p.Attributes.Select(a=>new App.ContainerRecycleAttributeDto(a.Name,a.Value.ToByteArray())).ToArray());
    internal static App.ContainerRecycleRestorePlanDto Plan(Rpc.ContainerRecycleRestorePlan p)=>new(p.SchemaVersion,new(new(Guid.ParseExact(p.Command.Source.OperationId,"N"),p.Command.Source.Owner,SourcePlan(p.Command.Source.Plan)),p.Command.HistoryDigest),p.TargetProof,new(p.ParentIdentity.Device,p.ParentIdentity.Inode),p.ParentVersion,p.OwnerUid,p.OwnerGid,new(p.PrivateIdentity.Device,p.PrivateIdentity.Inode),
        new(p.RetainedIdentity.Device,p.RetainedIdentity.Inode,p.RetainedIdentity.Mode,p.RetainedIdentity.OwnerUid,p.RetainedIdentity.OwnerGid,p.RetainedIdentity.LinkCount,p.RetainedIdentity.SizeBytes,p.RetainedIdentity.ModifiedAtUnixNs),p.RetainedChangedAtUnixNs,p.Fingerprint);
    private static App.TerminalActor Actor=>new("synthetic-admin",null,[],new('F',64),"synthetic-trace",null);
    private static App.ContainerRecycleRestoreOperationDto Operation()
    {var q=ContainerRecycleRestorePolicyTests.Request();return new(Guid.ParseExact(q.OperationId,"N"),q.Owner,Plan(q.Plan),new(q.Context.ActorSubject,q.Context.TraceId,Guid.ParseExact(q.Context.JobId,"N"),Guid.ParseExact(q.Context.IdempotencyKey,"N")));}

    [Theory]
    [InlineData(1U,56U,true)][InlineData(2U,57U,true)][InlineData(1U,57U,false)]
    public async Task CapabilityGateRejectsBeforeAnyOperationFrame(uint major,uint minor,bool available)
    {using var h=new Harness(major,minor,available);await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.PlanContainerRecycleRestoreAsync(Actor,Operation().Plan.Command,default));Assert.Null(h.Request);}

    [Fact]
    public async Task PlanFreezesNestedSourceArraysBeforeNegotiationAndSignsAllFields()
    {
        using var h=new Harness();var command=Operation().Plan.Command;var original=command.Source.Plan.Attributes[0].Value.ToArray();
        h.OnCapabilities=()=>command.Source.Plan.Attributes[0].Value[0]^=42;
        var result=await h.Gateway.PlanContainerRecycleRestoreAsync(Actor,command,default);var request=Assert.IsType<Rpc.ContainerRecycleRestorePlanRequest>(h.Request);
        Assert.Equal(original,request.Command.Source.Plan.Attributes[0].Value.ToByteArray());Assert.Equal(original,result.Command.Source.Plan.Attributes[0].Value);Assert.Equal(Actor.Subject,request.Context.ActorSubject);Assert.Empty(request.Context.JobId);Assert.Empty(request.Context.IdempotencyKey);h.AssertSignature();
    }
    [Theory]
    [InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public async Task NewActionsCarryNewJobAndOriginalSourceAndValidateOwnReceipt(string action)
    {
        using var h=new Harness();var op=Operation();var result=action switch {"Apply"=>await h.Gateway.ApplyContainerRecycleRestoreAsync(op,default),"Inspect"=>await h.Gateway.InspectContainerRecycleRestoreAsync(op,default),_=>await h.Gateway.DiscardContainerRecycleRestoreAsync(op,default)};
        Assert.NotNull(result);Assert.Equal(op.Id,result.Id);Assert.Equal(op.Plan.Command.Source.OperationId,result.SourceOperationId);Assert.True(result.RestoredByThisOperation);
        var request=Assert.IsType<Rpc.ContainerRecycleRestoreRequest>(h.Request);Assert.Equal(op.Id.ToString("N"),request.OperationId);Assert.Equal(request.OperationId,request.Context.JobId);Assert.NotEqual(request.OperationId,request.Plan.Command.Source.OperationId);h.AssertSignature();
    }
    [Fact]
    public async Task UnknownFactsKeepPresenceAbsentAndOperationMismatchNeverSends()
    {
        using var h=new Harness{Unknown=true};var op=Operation();var value=await h.Gateway.ApplyContainerRecycleRestoreAsync(op,default);Assert.Null(value.SourceRetained);Assert.Null(value.SourceRestored);Assert.Null(value.RestoredByThisOperation);
        using var bad=new Harness();await Assert.ThrowsAsync<ArgumentException>(()=>bad.Gateway.ApplyContainerRecycleRestoreAsync(op with{Id=op.Plan.Command.Source.OperationId},default));Assert.Null(bad.Request);
    }
    [Theory]
    [InlineData(StatusCode.NotFound,"CONTAINER_RECYCLE_RESTORE_NOT_FOUND",true)]
    [InlineData(StatusCode.NotFound,"CONTAINER_RECYCLE_NOT_FOUND",false)]
    [InlineData(StatusCode.Unavailable,"CONTAINER_RECYCLE_RESTORE_UNAVAILABLE",false)]
    public async Task OnlyExactNewPrivateAbsenceBecomesNull(StatusCode status,string detail,bool absent)
    {using var h=new Harness{Status=status,Detail=detail};if(absent)Assert.Null(await h.Gateway.InspectContainerRecycleRestoreAsync(Operation(),default));else await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.InspectContainerRecycleRestoreAsync(Operation(),default));}

    [Theory]
    [InlineData(StatusCode.Aborted,"CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED","CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED")]
    [InlineData(StatusCode.ResourceExhausted,"CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY","CONTAINER_RECYCLE_RESTORE_SOURCE_BUSY")]
    [InlineData(StatusCode.Aborted,"CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS","CONTAINER_RECYCLE_RESTORE_TARGET_EXISTS")]
    [InlineData(StatusCode.FailedPrecondition,"CONTAINER_FILES_INSTANCE_CHANGED","CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED")]
    [InlineData(StatusCode.PermissionDenied,"CONTAINER_RECYCLE_PROTECTED","CONTAINER_RECYCLE_RESTORE_PROTECTED")]
    [InlineData(StatusCode.FailedPrecondition,"CONTAINER_RECYCLE_HISTORY_CHANGED","CONTAINER_RECYCLE_RESTORE_HISTORY_UNAVAILABLE")]
    public async Task OnlyReadOnlyExactPlanningRefusalsAreTyped(StatusCode status,string detail,string code)
    {
        using var h=new Harness{Status=status,Detail=detail};var error=await Assert.ThrowsAsync<App.ContainerRecycleRestorePreflightRejectedException>(()=>h.Gateway.PlanContainerRecycleRestoreAsync(Actor,Operation().Plan.Command,default));Assert.Equal(code,error.Code);
        await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.ApplyContainerRecycleRestoreAsync(Operation(),default));
    }
    [Theory]
    [InlineData(StatusCode.Unavailable,"CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED")]
    [InlineData(StatusCode.Aborted,"CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED extra")]
    [InlineData(StatusCode.DeadlineExceeded,"CONTAINER_RECYCLE_RESTORE_TIMEOUT")]
    public async Task AmbiguousOutagesNeverBecomeDefinitivePlanningProof(StatusCode status,string detail)
    {using var h=new Harness{Status=status,Detail=detail};await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.PlanContainerRecycleRestoreAsync(Actor,Operation().Plan.Command,default));}

    [Theory]
    [InlineData("source")][InlineData("id")][InlineData("digest")][InlineData("presence")][InlineData("state")]
    public async Task CorruptRepliesNeverBecomeSuccessfulRestore(string field)
    {using var h=new Harness{Corrupt=field};await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Gateway.ApplyContainerRecycleRestoreAsync(Operation(),default));}
    [Theory]
    [InlineData("plan-private")][InlineData("plan-source")][InlineData("plan-missing")]
    public async Task CorruptPlanProjectionIsRejected(string field)
    {using var h=new Harness{Corrupt=field};await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Gateway.PlanContainerRecycleRestoreAsync(Actor,Operation().Plan.Command,default));}

    private sealed class Harness:HttpMessageHandler
    {
        private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);private readonly string _file=Path.GetTempFileName();private readonly uint _major,_minor;private readonly bool _available;
        public AgentGateway Gateway{get;}public IMessage? Request{get;private set;}public Action? OnCapabilities{get;set;}public bool Unknown{get;init;}public string? Corrupt{get;init;}public StatusCode Status{get;init;}=StatusCode.OK;public string Detail{get;init;}="";
        private string _method="",_token="",_hash="";
        public Harness(uint major=1,uint minor=57,bool available=true)
        {
            _major=major;_minor=minor;_available=available;File.WriteAllBytes(_file,_key);Gateway=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Agent:CapabilityKeyFile"]=_file}).Build());
            var field=typeof(AgentGateway).GetField("_channel",BindingFlags.Instance|BindingFlags.NonPublic)!;((GrpcChannel)field.GetValue(Gateway)!).Dispose();var channel=GrpcChannel.ForAddress("http://127.0.0.1",new(){HttpHandler=this,DisposeHttpClient=false});field.SetValue(Gateway,channel);
            typeof(AgentGateway).GetField("_agent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Gateway,new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request);Assert.Equal(ContainerRecycleRestoreBinding.Hash(Request),_hash);var parts=_token.Split('.');Assert.Equal(3,parts.Length);Assert.InRange(long.Parse(parts[0],CultureInfo.InvariantCulture)-DateTimeOffset.UtcNow.ToUnixTimeSeconds(),1,60);
            var expected=HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(ContainerRecycleRestoreBinding.Payload(parts[0],parts[1],_method,_hash)));Assert.Equal(Convert.ToHexString(expected),parts[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.AbsolutePath;var caps=path.EndsWith("/GetCapabilities",StringComparison.Ordinal);IMessage? frame;
            if(caps){OnCapabilities?.Invoke();var response=new Rpc.CapabilitiesResponse{ProtocolMajor=_major,ProtocolMinor=_minor};if(_available)response.Capabilities.Add("docker.files.recycle.restore");frame=response;}
            else
            {
                _method=path;_token=Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));_hash=Assert.Single(request.Headers.GetValues(ContainerRecycleRestoreBinding.Header));var raw=await request.Content!.ReadAsByteArrayAsync(cancellationToken);Assert.Equal(0,raw[0]);Assert.Equal(raw.Length-5,BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1,4)));
                if(path.EndsWith("/Plan",StringComparison.Ordinal))
                {
                    Request=Rpc.ContainerRecycleRestorePlanRequest.Parser.ParseFrom(raw.AsSpan(5));var p=ContainerRecycleRestorePolicyTests.Plan();
                    if(Corrupt=="plan-private")p.PrivateIdentity.Inode++;if(Corrupt=="plan-source"){p.Command.Source.Owner=new('F',64);p.Fingerprint=ContainerRecycleRestoreWire.Fingerprint(p);}if(Corrupt=="plan-missing")p.PrivateIdentity=null;frame=p;
                }
                else
                {
                    var q=Rpc.ContainerRecycleRestoreRequest.Parser.ParseFrom(raw.AsSpan(5));Request=q;using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleRestorePolicyTests.ReceiptInput(q,Unknown?"Unknown":"Completed")));var r=ContainerRecycleRestorePolicy.Receipt(json.RootElement,q);
                    switch(Corrupt){case "source":r.SourceOperationId=q.OperationId;break;case "id":r.OperationId=q.Plan.Command.Source.OperationId;break;case "digest":r.HistoryDigest=new('6',64);break;case "presence":r.ClearRestoredByThisOperation();break;case "state":r.State="Applying";break;}frame=r;
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
