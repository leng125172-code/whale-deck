using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerAclHistoryTests : IDisposable
{
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    private readonly string _file=Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerAclHistoryTests()
    {File.WriteAllBytes(_file,_key);_validator=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Agent:CapabilityKeyFile",_file}}).Build());}
    internal static ContainerFileLocation Location()=>new(){ContainerId=new('a',64),InstanceId="777:2026-11-03T01:02:03.123456789Z",Path="work/中文.bin"};
    internal static ContainerAclHistoryListRequest List()=>new(){Location=Location(),Take=10,Context=new(){ActorSubject="subject",TraceId="trace"}};
    internal static ContainerAclHistoryReadRequest Read()=>new(){Location=Location(),CaptureId=new('2',32),ExpectedHistoryDigest=new('C',64),Context=new(){ActorSubject="subject",TraceId="trace"}};
    internal static ContainerAclHistoryEntry Entry()=>new(){CaptureId=new('2',32),HistoryDigest=new('C',64),CreatedAtUnixMs=1700000000000,SourceInstanceId="666:2026-11-02T01:02:03Z",ChangedAclType="access",Kind="File",OriginalMode=416,AccessEntryCount=3,DefaultEntryCount=0};
    internal static ContainerAclHistoryPage Page()=>new(){Location=Location(),Version=new('A',64),Entries={Entry()},Complete=true,ScannedCount=1,InvalidCount=0};
    internal static ContainerAclHistorySnapshot Snapshot()=>new(){Location=Location(),Entry=Entry(),AccessAcl={new ContainerAclEntry{Tag="user_obj",Permissions=6},new ContainerAclEntry{Tag="group_obj",Permissions=4},new ContainerAclEntry{Tag="other",Permissions=0}}};
    private string Sign(string method,string hash,int seconds=30,bool legacy=false)
    {
        var expires=DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        return expires+"."+nonce+"."+Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(legacy?$"{expires}.{nonce}.{method}":ContainerAclHistoryBinding.Payload(expires,nonce,method,hash))));
    }
    [Fact] public void ExactlyTwoReadOnlyUnaryMethods()
    {Assert.Equal<string>(["List","Read"],ContainerAclHistoryService.Descriptor.Methods.Select(m=>m.Name));Assert.All(ContainerAclHistoryService.Descriptor.Methods,m=>{Assert.False(m.IsClientStreaming);Assert.False(m.IsServerStreaming);});}
    [Theory][InlineData("List")][InlineData("Read")]
    public void RequiresIndependentShortLivedContentBoundSingleUseAuthority(string action)
    {
        var method=ContainerAclHistoryBinding.Service+action;var hash=ContainerAclHistoryBinding.Hash(Read());var token=Sign(method,hash);
        Assert.False(_validator.Validate(token,method));Assert.False(_validator.ValidateContainerAclHistory(Sign(method,hash,legacy:true),method,hash));
        Assert.False(_validator.ValidateContainerAclHistory(Sign(method,hash,-1),method,hash));Assert.False(_validator.ValidateContainerAclHistory(Sign(method,hash,120),method,hash));
        Assert.False(_validator.ValidateContainerAcl(token,method,hash));Assert.False(_validator.ValidateContainerHistory(token,method,hash));
        Assert.False(_validator.ValidateContainerAclHistory(token,ContainerAclHistoryBinding.Service+(action=="List"?"Read":"List"),hash));
        Assert.False(_validator.ValidateContainerAclHistory(token,ContainerAclHistoryBinding.Service+"Apply",hash));
        Assert.True(_validator.ValidateContainerAclHistory(token,method,hash));Assert.False(_validator.ValidateContainerAclHistory(token,method,hash));
    }
    [Theory][InlineData("cid")][InlineData("instance")][InlineData("path")][InlineData("after")][InlineData("take")][InlineData("version")][InlineData("actor")][InlineData("trace")]
    public void EveryListScopeAndContextFieldChangesTheSignedHash(string field)
    {
        var original=List();var changed=original.Clone();
        switch(field){case"cid":changed.Location.ContainerId=new('b',64);break;case"instance":changed.Location.InstanceId="888:2026-11-03T01:02:03Z";break;case"path":changed.Location.Path="work/else";break;case"after":changed.After=new('1',32);break;case"take":changed.Take++;break;case"version":changed.ExpectedVersion=new('B',64);break;case"actor":changed.Context.ActorSubject="other";break;case"trace":changed.Context.TraceId="other";break;}
        var hash=ContainerAclHistoryBinding.Hash(original);var method=ContainerAclHistoryBinding.Service+"List";var token=Sign(method,hash);
        Assert.NotEqual(hash,ContainerAclHistoryBinding.Hash(changed));Assert.NotEqual(ContainerAclBinding.Hash(original),hash);Assert.NotEqual(ContainerHistoryBinding.Hash(original),hash);
        Assert.False(_validator.ValidateContainerAclHistory(token,method,ContainerAclHistoryBinding.Hash(changed)));Assert.True(_validator.ValidateContainerAclHistory(token,method,hash));
    }
    [Fact]public void ReadCaptureAndExpectedDigestAreBothBound()
    {var r=Read();var hash=ContainerAclHistoryBinding.Hash(r);r.CaptureId=new('3',32);Assert.NotEqual(hash,ContainerAclHistoryBinding.Hash(r));r=Read();r.ExpectedHistoryDigest=new('D',64);Assert.NotEqual(hash,ContainerAclHistoryBinding.Hash(r));}
    [Theory][InlineData("empty-path")][InlineData("take-zero")][InlineData("take-large")][InlineData("cursor-no-version")][InlineData("bad-version")][InlineData("no-context")][InlineData("job")][InlineData("capability")]
    public void InvalidListRequestsNeverReachTheHelper(string field)
    {
        var r=List();switch(field){case"empty-path":r.Location.Path="";break;case"take-zero":r.Take=0;break;case"take-large":r.Take=51;break;case"cursor-no-version":r.After=new('1',32);break;case"bad-version":r.ExpectedVersion="bad";break;case"no-context":r.Context=null;break;case"job":r.Context.JobId=new('3',32);break;case"capability":r.Context.Capability=Google.Protobuf.ByteString.CopyFromUtf8("not accepted");break;}
        Assert.Throws<ArgumentException>(()=>ContainerAclHistoryPolicy.List(r));
    }
    [Theory][InlineData("location")][InlineData("mode-presence")][InlineData("count")][InlineData("duplicate")][InlineData("stale")][InlineData("cursor")][InlineData("time")][InlineData("file-default")]
    public void MismatchedOrMalformedPageCannotBeAccepted(string field)
    {
        var r=List();var p=Page();switch(field){case"location":p.Location.Path="other";break;case"mode-presence":p.Entries[0].ClearOriginalMode();break;case"count":p.ScannedCount=201;break;case"duplicate":p.Entries.Add(Entry());p.ScannedCount=2;break;case"stale":r.ExpectedVersion=new('B',64);break;case"cursor":p.Complete=false;break;case"time":p.Entries[0].CreatedAtUnixMs=long.MaxValue;break;case"file-default":p.Entries[0].DefaultEntryCount=3;break;}
        Assert.ThrowsAny<Exception>(()=>ContainerAclHistoryPolicy.Page(p,r));
    }
    [Theory][InlineData("digest")][InlineData("capture")][InlineData("location")][InlineData("permissions-presence")][InlineData("mode")][InlineData("entries")][InlineData("named-id")]
    public void SnapshotMustMatchTheSelectedImmutableCaptureAndCompleteAcl(string field)
    {
        var s=Snapshot();switch(field){case"digest":s.Entry.HistoryDigest=new('D',64);break;case"capture":s.Entry.CaptureId=new('3',32);break;case"location":s.Location.ContainerId=new('b',64);break;case"permissions-presence":s.AccessAcl[2].ClearPermissions();break;case"mode":s.Entry.OriginalMode=0;break;case"entries":s.AccessAcl.RemoveAt(0);break;case"named-id":s.AccessAcl[0].Id=0;break;}
        Assert.ThrowsAny<Exception>(()=>ContainerAclHistoryPolicy.Snapshot(s,Read()));
    }
    [Fact]public void ValidSummaryAndZeroPermissionsRemainExplicit()
    {ContainerAclHistoryPolicy.List(List());ContainerAclHistoryPolicy.Read(Read());ContainerAclHistoryPolicy.Page(Page(),List());var s=Snapshot();ContainerAclHistoryPolicy.Snapshot(s,Read());Assert.True(s.AccessAcl[2].HasPermissions);Assert.Equal(0U,s.AccessAcl[2].Permissions);}
    [Fact]public void JsonParserRejectsPrivateExtraFieldsInsteadOfReturningRawPlans()
    {
        using var document=JsonDocument.Parse("{\"containerId\":\"x\",\"owner\":\"private\"}");
        Assert.Throws<InvalidOperationException>(()=>ContainerAclHistoryPolicy.Page(document.RootElement,List()));
    }
    [Theory][InlineData("list")][InlineData("read")]
    public void RunnerOnlyUsesItsFixedReadOnlyRouteAndInvocationStop(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic)!;
        var info=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-acl-history:"+action,new string('1',32),null]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-acl-history",action,new string('1',32)},info.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-acl-history:"+action,new string('1',32),new string('2',32)]));
        Assert.Contains("container-file-acl-history-stop",stop.ArgumentList);Assert.Equal(new string('2',32),stop.ArgumentList.Last());
    }
    [Fact]public void RunnerRejectsWriteOrShellActions()
    {var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,["container-acl-history:apply",new string('1',32),null])).InnerException);}
    [Fact]public void HelperJsonMapsNullFinalCursorAndExplicitZeroAcl()
    {
        using var page=JsonDocument.Parse(JsonSerializer.Serialize(new {containerId=Location().ContainerId,instanceId=Location().InstanceId,path=Location().Path,version=new string('A',64),entries=new[]{new {captureId=new string('2',32),historyDigest=new string('C',64),createdAtUnixMs=1700000000000L,sourceInstanceId=Entry().SourceInstanceId,changedAclType="access",kind="File",originalMode=416,accessEntryCount=3,defaultEntryCount=0}},nextAfter=(string?)null,scannedCount=1,invalidCount=0,complete=true}));
        Assert.True(ContainerAclHistoryPolicy.Page(page.RootElement,List()).Complete);
        using var snapshot=JsonDocument.Parse(JsonSerializer.Serialize(new {containerId=Location().ContainerId,instanceId=Location().InstanceId,path=Location().Path,captureId=new string('2',32),historyDigest=new string('C',64),createdAtUnixMs=1700000000000L,sourceInstanceId=Entry().SourceInstanceId,changedAclType="access",kind="File",originalMode=416,accessEntryCount=3,defaultEntryCount=0,accessAcl=new[]{new {tag="user_obj",id=(uint?)null,permissions=6},new {tag="group_obj",id=(uint?)null,permissions=4},new {tag="other",id=(uint?)null,permissions=0}},defaultAcl=Array.Empty<object>()}));
        Assert.True(ContainerAclHistoryPolicy.Snapshot(snapshot.RootElement,Read()).AccessAcl[2].HasPermissions);
    }
    [Fact]public void NamedRootIdAndZeroModeAreNotMissingValues()
    {
        var snapshot=Snapshot();snapshot.Entry.AccessEntryCount=5;snapshot.AccessAcl.Insert(1,new(){Tag="user",Id=0,Permissions=0});snapshot.AccessAcl.Insert(3,new(){Tag="mask",Permissions=4});
        ContainerAclHistoryPolicy.Snapshot(snapshot,Read());Assert.True(snapshot.AccessAcl[1].HasId);
        snapshot=Snapshot();snapshot.Entry.OriginalMode=0;foreach(var entry in snapshot.AccessAcl)entry.Permissions=0;ContainerAclHistoryPolicy.Snapshot(snapshot,Read());
    }
    [Fact]public void ExplicitFalseAndEpochZeroRemainValidButMissingPresenceIsRejected()
    {
        var page=Page();page.Complete=false;page.NextAfter=new('3',32);page.ScannedCount=2;page.Entries[0].CreatedAtUnixMs=0;
        ContainerAclHistoryPolicy.Page(page,List());
        var missing=page.Clone();missing.ClearComplete();Assert.Throws<InvalidOperationException>(()=>ContainerAclHistoryPolicy.Page(missing,List()));
        missing=page.Clone();missing.Entries[0].ClearCreatedAtUnixMs();Assert.Throws<InvalidOperationException>(()=>ContainerAclHistoryPolicy.Page(missing,List()));
    }
    public void Dispose(){File.Delete(_file);CryptographicOperations.ZeroMemory(_key);}
}
