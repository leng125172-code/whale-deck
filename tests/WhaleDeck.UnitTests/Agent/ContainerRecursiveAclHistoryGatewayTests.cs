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
public sealed class ContainerRecursiveAclHistoryGatewayTests
{
    private static readonly App.TerminalActor Actor=new("subject",null,[],"login","trace",null);
    private static readonly App.ContainerFileLocationDto Location=new(new('a',64),"777:2026-11-03T01:02:03.123456789Z","work/中文.bin");
    [Theory][InlineData(false)][InlineData(true)]
    public async Task TypedCallsCarryFullIndependentAuthorityAndReturnVerifiedProjection(bool read)
    {
        using var h=new Harness();
        if(read){var value=await h.Gateway.ReadContainerRecursiveAclHistoryAsync(Actor,Location,new('2',32),new('C',64),0,10,default);Assert.Equal(0U,value.Entries[0].OriginalMode);Assert.Equal(0U,value.Entries[0].AccessAcl[4].Permissions);Assert.Equal(0U,value.Entries[0].AccessAcl[1].Id);}
        else{var value=await h.Gateway.ListContainerRecursiveAclHistoryAsync(Actor,Location,new('1',32),10,new('A',64),default);Assert.True(value.Complete);Assert.Equal(Location,value.Location);Assert.Single(value.Entries);}
        h.AssertSignature();
        var context=read?Assert.IsType<Rpc.ContainerRecursiveAclHistoryReadRequest>(h.Request).Context:Assert.IsType<Rpc.ContainerRecursiveAclHistoryListRequest>(h.Request).Context;
        Assert.Equal(Actor.Subject,context.ActorSubject);Assert.Equal(Actor.TraceId,context.TraceId);Assert.Equal("",context.JobId);Assert.Empty(context.Capability);
        if(read){var request=Assert.IsType<Rpc.ContainerRecursiveAclHistoryReadRequest>(h.Request);Assert.Equal(new string('2',32),request.CaptureId);Assert.Equal(new string('C',64),request.ExpectedHistoryDigest);}
        else{var request=Assert.IsType<Rpc.ContainerRecursiveAclHistoryListRequest>(h.Request);Assert.Equal(new string('1',32),request.After);Assert.Equal(new string('A',64),request.ExpectedVersion);Assert.Equal(10U,request.Take);}
    }
    [Theory][InlineData(1U,53U,true)][InlineData(2U,54U,true)][InlineData(1U,54U,false)]
    public async Task OldMajorMinorOrMissingCapabilityNeverFallsBackToAclWrites(uint major,uint minor,bool capability)
    {
        using var h=new Harness(major,minor,capability);
        var error=await Assert.ThrowsAsync<RpcException>(()=>h.Gateway.ListContainerRecursiveAclHistoryAsync(Actor,Location,"",10,"",default));
        Assert.Equal(StatusCode.Unimplemented,error.StatusCode);Assert.Null(h.Request);
    }
    [Theory][InlineData("digest")][InlineData("mode-presence")][InlineData("permissions-presence")][InlineData("location")][InlineData("time-presence")][InlineData("entry-count-presence")][InlineData("offset-presence")][InlineData("next")][InlineData("order")][InlineData("default-on-file")]
    public async Task UntrustedSnapshotCannotOverrideCaptureOrDropExplicitZeroPresence(string corruption)
    {
        using var h=new Harness(){Corrupt=corruption};
        await Assert.ThrowsAnyAsync<Exception>(()=>h.Gateway.ReadContainerRecursiveAclHistoryAsync(Actor,Location,new('2',32),new('C',64),0,10,default));
    }
    [Theory][InlineData("scanned-presence")][InlineData("invalid-presence")][InlineData("complete-presence")]
    public async Task MissingPageScalarsAreNotAssumedToBeRealZeroOrFalse(string corruption)
    {
        using var h=new Harness(){Corrupt=corruption,EmptyPage=true};
        await Assert.ThrowsAnyAsync<Exception>(()=>h.Gateway.ListContainerRecursiveAclHistoryAsync(Actor,Location,"",10,"",default));
    }
    private sealed class Harness:HttpMessageHandler
    {
        private readonly string _file=Path.GetTempFileName();private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
        private readonly uint _major,_minor;private readonly bool _capability;
        public AgentGateway Gateway{get;}
        public string? Corrupt{get;init;}
        public bool EmptyPage{get;init;}
        public StatusCode? Failure{get;init;}
        public IMessage? Request{get;private set;}
        private string _token="",_hash="",_method="";
        public Harness(uint major=1,uint minor=54,bool capability=true)
        {
            _major=major;_minor=minor;_capability=capability;File.WriteAllBytes(_file,_key);
            Gateway=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Agent:CapabilityKeyFile",_file}}).Build());
            var channel=GrpcChannel.ForAddress("http://127.0.0.1",new(){HttpHandler=this,DisposeHttpClient=false});
            var field=typeof(AgentGateway).GetField("_channel",BindingFlags.Instance|BindingFlags.NonPublic)!;((GrpcChannel)field.GetValue(Gateway)!).Dispose();field.SetValue(Gateway,channel);
            typeof(AgentGateway).GetField("_agent",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Gateway,new Rpc.AgentService.AgentServiceClient(channel));
        }
        public void AssertSignature()
        {
            Assert.NotNull(Request);Assert.Equal(ContainerRecursiveAclHistoryBinding.Hash(Request),_hash);var p=_token.Split('.');Assert.Equal(3,p.Length);
            Assert.InRange(long.Parse(p[0],CultureInfo.InvariantCulture)-DateTimeOffset.UtcNow.ToUnixTimeSeconds(),1,60);
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(ContainerRecursiveAclHistoryBinding.Payload(p[0],p[1],_method,_hash)))),p[2]);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.AbsolutePath;IMessage frame;
            if(path.EndsWith("/GetCapabilities",StringComparison.Ordinal))
            {var c=new Rpc.CapabilitiesResponse{ProtocolMajor=_major,ProtocolMinor=_minor};c.Capabilities.Add("docker.files.acl-intents");if(_capability)c.Capabilities.Add("docker.files.recursive-acl-history");frame=c;}
            else
            {
                _method=path;_token=Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));_hash=Assert.Single(request.Headers.GetValues(ContainerRecursiveAclHistoryBinding.Header));
                var raw=await request.Content!.ReadAsByteArrayAsync(cancellationToken);Assert.Equal(0,raw[0]);Assert.Equal(raw.Length-5,BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(1,4)));
                if(path.EndsWith("/List",StringComparison.Ordinal)){Request=Rpc.ContainerRecursiveAclHistoryListRequest.Parser.ParseFrom(raw.AsSpan(5));var value=ContainerRecursiveAclHistoryTests.Page();if(EmptyPage){value.Entries.Clear();value.ScannedCount=0;value.InvalidCount=0;}switch(Corrupt){case"scanned-presence":value.ClearScannedCount();break;case"invalid-presence":value.ClearInvalidCount();break;case"complete-presence":value.ClearComplete();break;}frame=value;}
                else
                {
                    Assert.EndsWith("/Read",path);Request=Rpc.ContainerRecursiveAclHistoryReadRequest.Parser.ParseFrom(raw.AsSpan(5));var value=ContainerRecursiveAclHistoryTests.Snapshot();
                    switch(Corrupt){case"digest":value.Summary.HistoryDigest=new('D',64);break;case"mode-presence":value.Entries[0].ClearOriginalMode();break;case"permissions-presence":value.Entries[0].AccessAcl[4].ClearPermissions();break;case"location":value.Location.Path="else";break;case"time-presence":value.Summary.ClearCreatedAtUnixMs();break;case"entry-count-presence":value.Summary.ClearEntryCount();break;case"offset-presence":value.ClearOffset();break;case"next":value.NextOffset=2;break;case"order":value.Entries[0].RelativePath="";break;case"default-on-file":value.Entries[0].DefaultAcl.Add(value.Entries[0].AccessAcl);break;}
                    frame=value;
                }
            }
            var bytes=frame.ToByteArray();var payload=new byte[bytes.Length+5];BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1,4),bytes.Length);bytes.CopyTo(payload,5);
            var response=new HttpResponseMessage(HttpStatusCode.OK){Version=new(2,0),Content=new ByteArrayContent(payload)};response.Content.Headers.ContentType=new MediaTypeHeaderValue("application/grpc");response.TrailingHeaders.Add("grpc-status",Request is not null && Failure is {} status?((int)status).ToString(CultureInfo.InvariantCulture):"0");return response;
        }
        protected override void Dispose(bool disposing){if(disposing){Gateway.Dispose();File.Delete(_file);CryptographicOperations.ZeroMemory(_key);}base.Dispose(disposing);}
    }
}
