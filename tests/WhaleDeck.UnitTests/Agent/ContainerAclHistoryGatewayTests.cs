using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Contracts;
using WhaleDeck.Infrastructure.Agent;
using App=WhaleDeck.Application.Models;
using Rpc=WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;
public sealed class ContainerAclHistoryGatewayTests
{
    private static readonly App.TerminalActor Actor=new("subject",null,[],"login","trace",null);
    private static readonly App.ContainerFileLocationDto Location=new(new('a',64),"777:2026-11-03T01:02:03.123456789Z","work/中文.bin");
    [Theory][InlineData(false)][InlineData(true)]
    public async Task TypedCallsCarryFullIndependentAuthorityAndReturnVerifiedProjection(bool read)
    {
        using var h=new Harness();
        if(read){var value=await h.Gateway.ReadContainerAclHistoryAsync(Actor,Location,new('2',32),new('C',64),default);Assert.Equal(416U,value.Entry.OriginalMode);Assert.Equal(0U,value.AccessAcl[2].Permissions);}
        else{var value=await h.Gateway.ListContainerAclHistoryAsync(Actor,Location,new('1',32),10,new('A',64),default);Assert.True(value.Complete);Assert.Equal(Location,value.Location);Assert.Single(value.Entries);}
        h.AssertSignature();
        var context=read?Assert.IsType<Rpc.ContainerAclHistoryReadRequest>(h.Request).Context:Assert.IsType<Rpc.ContainerAclHistoryListRequest>(h.Request).Context;
        Assert.Equal(Actor.Subject,context.ActorSubject);Assert.Equal(Actor.TraceId,context.TraceId);Assert.Equal("",context.JobId);Assert.Empty(context.Capability);
        if(read){var request=Assert.IsType<Rpc.ContainerAclHistoryReadRequest>(h.Request);Assert.Equal(new string('2',32),request.CaptureId);Assert.Equal(new string('C',64),request.ExpectedHistoryDigest);}
        else{var request=Assert.IsType<Rpc.ContainerAclHistoryListRequest>(h.Request);Assert.Equal(new string('1',32),request.After);Assert.Equal(new string('A',64),request.ExpectedVersion);Assert.Equal(10U,request.Take);}
    }
    [Theory][InlineData(1U,51U,true)][InlineData(2U,52U,true)][InlineData(1U,52U,false)]
    public async Task OldMajorMinorOrMissingCapabilityNeverFallsBackToAclWrites(uint major,uint minor,bool capability)
    {
        using var h=new Harness(major,minor,capability);
        var error=await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.ListContainerAclHistoryAsync(Actor,Location,"",10,"",default));
        Assert.Equal(StatusCode.Unimplemented,error.StatusCode);Assert.Null(h.Request);
    }
    [Theory][InlineData("digest")][InlineData("mode-presence")][InlineData("permissions-presence")][InlineData("location")][InlineData("time-presence")][InlineData("default-count-presence")][InlineData("access-count-presence")]
    public async Task UntrustedSnapshotCannotOverrideCaptureOrDropExplicitZeroPresence(string corruption)
    {
        using var h=new Harness(){Corrupt=corruption};
        await Assert.ThrowsAnyAsync<Exception>(()=>h.Gateway.ReadContainerAclHistoryAsync(Actor,Location,new('2',32),new('C',64),default));
    }
    [Theory][InlineData("scanned-presence")][InlineData("invalid-presence")][InlineData("complete-presence")]
    public async Task MissingPageScalarsAreNotAssumedToBeRealZeroOrFalse(string corruption)
    {
        using var h=new Harness(){Corrupt=corruption,EmptyPage=true};
        await Assert.ThrowsAnyAsync<Exception>(()=>h.Gateway.ListContainerAclHistoryAsync(Actor,Location,"",10,"",default));
    }
    private sealed class Harness:HttpMessageHandler
    {
        private readonly string _file=Path.GetTempFileName();private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
        private readonly uint _major,_minor;private readonly bool _capability;
        public AgentGateway Gateway{get;}
        public string? Corrupt{get;init;}
        public bool EmptyPage{get;init;}
        public IMessage? Request{get;private set;}
        private string _token="",_hash="",_method="";
        public Harness(uint major=1,uint minor=52,bool capability=true)
        {
            _major=major;_minor=minor;_capability=capability;File.WriteAllBytes(_file,_key);
            Gateway=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Agent:CapabilityKeyFile",_file}}).Build());
            var channel=GrpcChannel.ForAddress("http://127.0.0.1",new(){HttpHandler=this,DisposeHttpClient=false});
            var field=typeof(AgentGateway).GetField("_channel",BindingFlags.Instance|BindingFlags.NonPublic)!;((GrpcChannel)field.GetValue(Gateway)!).Dispose();field.SetValue(Gateway,channel);
            typeof(AgentGateway).GetField("_agent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Gateway,new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request);Assert.Equal(ContainerAclHistoryBinding.Hash(Request),_hash);var p=_token.Split('.');Assert.Equal(3,p.Length);
            Assert.InRange(long.Parse(p[0],CultureInfo.InvariantCulture)-DateTimeOffset.UtcNow.ToUnixTimeSeconds(),1,60);
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(ContainerAclHistoryBinding.Payload(p[0],p[1],_method,_hash)))),p[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.AbsolutePath;IMessage frame;
            if(path.EndsWith("/GetCapabilities",StringComparison.Ordinal))
            {var c=new Rpc.CapabilitiesResponse{ProtocolMajor=_major,ProtocolMinor=_minor};c.Capabilities.Add("docker.files.acl-intents");if(_capability)c.Capabilities.Add("docker.files.acl-history");frame=c;}
            else
            {
                _method=path;_token=Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));_hash=Assert.Single(request.Headers.GetValues(ContainerAclHistoryBinding.Header));
                var raw=await request.Content!.ReadAsByteArrayAsync(cancellationToken);Assert.Equal(0,raw[0]);Assert.Equal(raw.Length-5,BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1,4)));
                if(path.EndsWith("/List",StringComparison.Ordinal)){Request=Rpc.ContainerAclHistoryListRequest.Parser.ParseFrom(raw.AsSpan(5));var value=ContainerAclHistoryTests.Page();if(EmptyPage){value.Entries.Clear();value.ScannedCount=0;value.InvalidCount=0;}switch(Corrupt){case"scanned-presence":value.ClearScannedCount();break;case"invalid-presence":value.ClearInvalidCount();break;case"complete-presence":value.ClearComplete();break;}frame=value;}
                else
                {
                    Assert.EndsWith("/Read",path);Request=Rpc.ContainerAclHistoryReadRequest.Parser.ParseFrom(raw.AsSpan(5));var value=ContainerAclHistoryTests.Snapshot();
                    switch(Corrupt){case"digest":value.Entry.HistoryDigest=new('D',64);break;case"mode-presence":value.Entry.ClearOriginalMode();break;case"permissions-presence":value.AccessAcl[2].ClearPermissions();break;case"location":value.Location.Path="else";break;case"time-presence":value.Entry.ClearCreatedAtUnixMs();break;case"default-count-presence":value.Entry.ClearDefaultEntryCount();break;case"access-count-presence":value.Entry.ClearAccessEntryCount();break;}
                    frame=value;
                }
            }
            var bytes=frame.ToByteArray();var payload=new byte[bytes.Length+5];BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1,4),bytes.Length);bytes.CopyTo(payload,5);
            var response=new HttpResponseMessage(HttpStatusCode.OK){Version=new(2,0),Content=new ByteArrayContent(payload)};response.Content.Headers.ContentType=new MediaTypeHeaderValue("application/grpc");response.TrailingHeaders.Add("grpc-status","0");return response;
        }
        protected override void Dispose(bool disposing){if(disposing){Gateway.Dispose();File.Delete(_file);CryptographicOperations.ZeroMemory(_key);}base.Dispose(disposing);}
    }
}
