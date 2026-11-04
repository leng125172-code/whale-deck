using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecursiveAclHistoryTests : IDisposable
{
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    private readonly string _file=Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerRecursiveAclHistoryTests()
    {File.WriteAllBytes(_file,_key);_validator=new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Agent:CapabilityKeyFile",_file}}).Build());}
    internal static ContainerFileLocation Location()=>ContainerAclHistoryTests.Location();
    internal static ContainerRecursiveAclHistoryListRequest List()=>new(){Location=Location(),Take=10,Context=new(){ActorSubject="subject",TraceId="trace"}};
    internal static ContainerRecursiveAclHistoryReadRequest Read()=>new(){Location=Location(),CaptureId=new('2',32),ExpectedHistoryDigest=new('C',64),Offset=0,Take=10,Context=new(){ActorSubject="subject",TraceId="trace"}};
    internal static ContainerRecursiveAclHistorySummary Summary()=>new(){CaptureId=new('2',32),HistoryDigest=new('C',64),CreatedAtUnixMs=0,SourceInstanceId="666:2026-11-02T01:02:03Z",ChangedAclType="access",ManifestDigest=new('D',64),EntryCount=2,FileCount=1,DirectoryCount=1,ChangeCount=0};
    internal static ContainerRecursiveAclHistoryPage Page()=>new(){Location=Location(),Version=new('A',64),Entries={Summary()},Complete=true,ScannedCount=1,InvalidCount=0};
    internal static ContainerRecursiveAclHistoryEntry Item(string path,string kind)=>new(){RelativePath=path,Kind=kind,OriginalMode=0,AccessAcl={new ContainerAclEntry{Tag="user_obj",Permissions=0},new ContainerAclEntry{Tag="user",Id=0,Permissions=0},new ContainerAclEntry{Tag="group_obj",Permissions=0},new ContainerAclEntry{Tag="mask",Permissions=0},new ContainerAclEntry{Tag="other",Permissions=0}}};
    internal static ContainerRecursiveAclHistorySnapshot Snapshot()=>new(){Location=Location(),Summary=Summary(),Offset=0,Complete=true,Entries={Item("child.bin","File"),Item("","Directory")}};
    private string Sign(string method,string hash,int seconds=30,bool legacy=false)
    {
        var expires=DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);var nonce=Guid.NewGuid().ToString("N");
        return expires+"."+nonce+"."+Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(legacy?$"{expires}.{nonce}.{method}":ContainerRecursiveAclHistoryBinding.Payload(expires,nonce,method,hash))));
    }
    [Fact]public async Task IndependentCapabilityAndExactlyTwoReadOnlyUnaryMethods()
    {
        Assert.Equal<string>(["List","Read"],ContainerRecursiveAclHistoryService.Descriptor.Methods.Select(m=>m.Name));
        Assert.All(ContainerRecursiveAclHistoryService.Descriptor.Methods,m=>{Assert.False(m.IsClientStreaming);Assert.False(m.IsServerStreaming);});
        var caps=await new AgentGrpcService(null!).GetCapabilities(new(),null!);Assert.Equal(54U,caps.ProtocolMinor);Assert.Contains("docker.files.recursive-acl-history",caps.Capabilities);Assert.Contains("docker.files.acl-history",caps.Capabilities);
    }
    [Theory][InlineData("List")][InlineData("Read")]
    public void IndependentShortLivedContentBoundSingleUseAuthority(string action)
    {
        var method=ContainerRecursiveAclHistoryBinding.Service+action;var hash=ContainerRecursiveAclHistoryBinding.Hash(Read());var token=Sign(method,hash);
        Assert.False(_validator.Validate(token,method));Assert.False(_validator.ValidateContainerRecursiveAclHistory(Sign(method,hash,legacy:true),method,hash));
        Assert.False(_validator.ValidateContainerRecursiveAclHistory(Sign(method,hash,-1),method,hash));Assert.False(_validator.ValidateContainerRecursiveAclHistory(Sign(method,hash,120),method,hash));
        Assert.False(_validator.ValidateContainerRecursiveAcl(token,method,hash));Assert.False(_validator.ValidateContainerAclHistory(token,method,hash));
        Assert.False(_validator.ValidateContainerRecursiveAclHistory(token,ContainerRecursiveAclHistoryBinding.Service+(action=="List"?"Read":"List"),hash));
        Assert.False(_validator.ValidateContainerRecursiveAclHistory(token,ContainerRecursiveAclHistoryBinding.Service+"Apply",hash));
        Assert.True(_validator.ValidateContainerRecursiveAclHistory(token,method,hash));Assert.False(_validator.ValidateContainerRecursiveAclHistory(token,method,hash));
    }
    [Theory][InlineData("cid")][InlineData("instance")][InlineData("path")][InlineData("after")][InlineData("take")][InlineData("version")][InlineData("actor")][InlineData("trace")]
    public void ListHashBindsAllScopeCursorAndContextFields(string field)
    {
        var original=List();var changed=original.Clone();
        switch(field){case"cid":changed.Location.ContainerId=new('b',64);break;case"instance":changed.Location.InstanceId="888:2026-11-03T01:02:03Z";break;case"path":changed.Location.Path="else";break;case"after":changed.After=new('1',32);break;case"take":changed.Take++;break;case"version":changed.ExpectedVersion=new('B',64);break;case"actor":changed.Context.ActorSubject="other";break;case"trace":changed.Context.TraceId="other";break;}
        var hash=ContainerRecursiveAclHistoryBinding.Hash(original);var method=ContainerRecursiveAclHistoryBinding.Service+"List";var token=Sign(method,hash);
        Assert.NotEqual(hash,ContainerRecursiveAclHistoryBinding.Hash(changed));Assert.NotEqual(ContainerRecursiveAclBinding.Hash(original),hash);Assert.NotEqual(ContainerAclHistoryBinding.Hash(original),hash);
        Assert.False(_validator.ValidateContainerRecursiveAclHistory(token,method,ContainerRecursiveAclHistoryBinding.Hash(changed)));Assert.True(_validator.ValidateContainerRecursiveAclHistory(token,method,hash));
    }
    [Theory][InlineData("capture")][InlineData("digest")][InlineData("offset")][InlineData("offset-presence")][InlineData("take")][InlineData("context")]
    public void ReadSelectionAndEachPageAreBound(string field)
    {
        var original=Read();var changed=original.Clone();switch(field){case"capture":changed.CaptureId=new('3',32);break;case"digest":changed.ExpectedHistoryDigest=new('F',64);break;case"offset":changed.Offset++;break;case"offset-presence":changed.ClearOffset();break;case"take":changed.Take++;break;case"context":changed.Context.ActorSubject="other";break;}
        Assert.NotEqual(ContainerRecursiveAclHistoryBinding.Hash(original),ContainerRecursiveAclHistoryBinding.Hash(changed));
    }
    [Theory][InlineData("empty-path")][InlineData("take-zero")][InlineData("take-large")][InlineData("cursor-no-version")][InlineData("bad-version")][InlineData("no-context")][InlineData("job")][InlineData("capability")]
    public void InvalidListRequestsCannotReachHelper(string field)
    {
        var r=List();switch(field){case"empty-path":r.Location.Path="";break;case"take-zero":r.Take=0;break;case"take-large":r.Take=51;break;case"cursor-no-version":r.After=new('1',32);break;case"bad-version":r.ExpectedVersion="bad";break;case"no-context":r.Context=null;break;case"job":r.Context.JobId=new('3',32);break;case"capability":r.Context.Capability=ByteString.CopyFromUtf8("bad");break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecursiveAclHistoryPolicy.List(r));
    }
    [Theory][InlineData("missing-offset")][InlineData("offset-limit")][InlineData("take-zero")][InlineData("take-large")]
    public void ReadBoundsAndExplicitZeroAreRequired(string field)
    {
        var r=Read();switch(field){case"missing-offset":r.ClearOffset();break;case"offset-limit":r.Offset=10000;break;case"take-zero":r.Take=0;break;case"take-large":r.Take=51;break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecursiveAclHistoryPolicy.Read(r));
    }
    [Theory][InlineData("location")][InlineData("time-presence")][InlineData("entries-presence")][InlineData("files-presence")][InlineData("directories-presence")][InlineData("changes-presence")][InlineData("counter")][InlineData("duplicate")][InlineData("stale")][InlineData("cursor")][InlineData("counts")][InlineData("default-count")][InlineData("complete-presence")]
    public void MalformedPageIsRejected(string field)
    {
        var r=List();var p=Page();switch(field){case"location":p.Location.Path="other";break;case"time-presence":p.Entries[0].ClearCreatedAtUnixMs();break;case"entries-presence":p.Entries[0].ClearEntryCount();break;case"files-presence":p.Entries[0].ClearFileCount();break;case"directories-presence":p.Entries[0].ClearDirectoryCount();break;case"changes-presence":p.Entries[0].ClearChangeCount();break;case"counter":p.ScannedCount=201;break;case"duplicate":p.Entries.Add(Summary());p.ScannedCount=2;break;case"stale":r.ExpectedVersion=new('B',64);break;case"cursor":p.Complete=false;break;case"counts":p.Entries[0].EntryCount=3;break;case"default-count":p.Entries[0].ChangedAclType="default";p.Entries[0].ChangeCount=2;break;case"complete-presence":p.ClearComplete();break;}
        Assert.ThrowsAny<Exception>(()=>ContainerRecursiveAclHistoryPolicy.Page(p,r));
    }
    [Theory][InlineData("digest")][InlineData("capture")][InlineData("offset-presence")][InlineData("offset")][InlineData("next")][InlineData("complete")][InlineData("permissions-presence")][InlineData("mode-presence")][InlineData("mode")][InlineData("entries")][InlineData("named-id")][InlineData("file-default")][InlineData("order")][InlineData("path")][InlineData("root")]
    public void SnapshotMustMatchFullSelectedManifestAndProgress(string field)
    {
        var s=Snapshot();switch(field){case"digest":s.Summary.HistoryDigest=new('F',64);break;case"capture":s.Summary.CaptureId=new('3',32);break;case"offset-presence":s.ClearOffset();break;case"offset":s.Offset=1;break;case"next":s.NextOffset=2;break;case"complete":s.Complete=false;break;case"permissions-presence":s.Entries[0].AccessAcl[4].ClearPermissions();break;case"mode-presence":s.Entries[0].ClearOriginalMode();break;case"mode":s.Entries[0].OriginalMode=1;break;case"entries":s.Entries[0].AccessAcl.RemoveAt(0);break;case"named-id":s.Entries[0].AccessAcl[1].ClearId();break;case"file-default":s.Entries[0].DefaultAcl.Add(s.Entries[0].AccessAcl);break;case"order":var first=s.Entries[0];s.Entries[0]=s.Entries[1];s.Entries[1]=first;break;case"path":s.Entries[0].RelativePath="../else";break;case"root":s.Entries[1].Kind="File";break;}
        Assert.ThrowsAny<Exception>(()=>ContainerRecursiveAclHistoryPolicy.Snapshot(s,Read()));
    }
    [Fact]public void PartialPageAndLastRootPreserveZerosOldInstanceAndHeterogeneousAcl()
    {
        var r=Read();var first=Snapshot();first.Entries.RemoveAt(1);first.Complete=false;first.NextOffset=1;ContainerRecursiveAclHistoryPolicy.Snapshot(first,r);
        var last=Snapshot();last.Entries.RemoveAt(0);last.Offset=1;r.Offset=1;last.Entries[0].AccessAcl[0].Permissions=6;last.Entries[0].OriginalMode=384;ContainerRecursiveAclHistoryPolicy.Snapshot(last,r);
        Assert.True(first.Entries[0].AccessAcl[1].HasId);Assert.True(first.Entries[0].HasOriginalMode);Assert.NotEqual(last.Summary.SourceInstanceId,last.Location.InstanceId);
    }
    [Fact]public void CanonicalOrderUsesUtf8NotUtf16AndRootIsLast()
    {
        var s=Snapshot();s.Summary.EntryCount=3;s.Summary.FileCount=2;s.Entries.Insert(0,Item("\uE000","File"));s.Entries[1].RelativePath="\U00010000";ContainerRecursiveAclHistoryPolicy.Snapshot(s,Read());
    }
    [Fact]public void JsonNullContinuationAndStrictProjectionAreValidated()
    {
        using var document=JsonDocument.Parse(JsonSnapshot());var parsed=ContainerRecursiveAclHistoryPolicy.Snapshot(document.RootElement,Read());Assert.True(parsed.Complete);Assert.False(parsed.HasNextOffset);Assert.Equal(0U,parsed.Offset);
        using var extra=JsonDocument.Parse(JsonSnapshot().Replace("\"offset\":0","\"owner\":\"private\",\"offset\":0",StringComparison.Ordinal));Assert.Throws<InvalidOperationException>(()=>ContainerRecursiveAclHistoryPolicy.Snapshot(extra.RootElement,Read()));
    }
    internal static string JsonSnapshot()=>JsonSerializer.Serialize(new{containerId=Location().ContainerId,instanceId=Location().InstanceId,path=Location().Path,captureId=new string('2',32),historyDigest=new string('C',64),createdAtUnixMs=0,sourceInstanceId=Summary().SourceInstanceId,changedAclType="access",manifestDigest=new string('D',64),entryCount=2,fileCount=1,directoryCount=1,changeCount=0,offset=0,entries=Snapshot().Entries.Select(e=>new{relativePath=e.RelativePath,kind=e.Kind,originalMode=e.OriginalMode,accessAcl=e.AccessAcl.Select(a=>new{tag=a.Tag,id=a.HasId?(uint?)a.Id:null,permissions=a.Permissions}),defaultAcl=Array.Empty<object>()}),complete=true,nextOffset=(int?)null});
    [Theory][InlineData("list")][InlineData("read")]
    public void RunnerUsesFixedReadOnlyRouteAndExactInvocationStop(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic)!;
        var info=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recursive-acl-history:"+action,new string('1',32),null]));Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recursive-acl-history",action,new string('1',32)},info.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recursive-acl-history:"+action,new string('1',32),new string('2',32)]));Assert.Contains("container-file-recursive-acl-history-stop",stop.ArgumentList);Assert.Equal(new string('2',32),stop.ArgumentList.Last());
    }
    [Theory][InlineData("apply")][InlineData("restore")][InlineData("read;reboot")]
    public void RunnerRejectsWriteOrShellActions(string action)
    {var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,["container-recursive-acl-history:"+action,new string('1',32),null])).InnerException);}
    [Theory][InlineData("NOT_FOUND",StatusCode.NotFound)][InlineData("VERSION_CONFLICT",StatusCode.Aborted)][InlineData("TIMEOUT",StatusCode.DeadlineExceeded)][InlineData("INVALID",StatusCode.FailedPrecondition)][InlineData("RESPONSE_LIMIT",StatusCode.ResourceExhausted)][InlineData("PRIVATE_DETAIL",StatusCode.Unavailable)]
    public void FailureMappingKeepsOutagesDistinctAndSanitizesUnknowns(string code,StatusCode status)
    {var error=ContainerRecursiveAclHistoryPolicy.Failure("CONTAINER_RECURSIVE_ACL_HISTORY_"+code);Assert.Equal(status,error.StatusCode);if(status==StatusCode.Unavailable)Assert.DoesNotContain("PRIVATE_DETAIL",error.Status.Detail);}
    [Theory]
    [InlineData("container-recursive-acl-history:read",131072,true)]
    [InlineData("container-recursive-acl-history:read",131073,false)]
    [InlineData("container-acl-history:read",65536,true)]
    [InlineData("container-acl-history:read",65537,false)]
    public async Task ActualDriverLineReaderPreservesIndependentResponseBounds(string action,int size,bool accepted)
    {
        // Supply one buffered frame; no child, sudo, shell or runtime helper starts.
        var type=typeof(FileTransferRunner).GetNestedType("Driver",BindingFlags.NonPublic)!;
        using var driver=(IDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[action],null)!;
        var frame=Encoding.UTF8.GetBytes("{\"x\":\""+new string('x',size-8)+"\"}\n");Assert.Equal(size+1,frame.Length);
        type.GetField("<Buffer>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(driver,frame);
        type.GetProperty("BufferCount")!.SetValue(driver,frame.Length);
        var method=typeof(FileTransferRunner).GetMethod("ReadJsonAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
        var task=Assert.IsAssignableFrom<Task<JsonDocument>>(method.Invoke(null,[driver,CancellationToken.None]));
        if(accepted){using var result=await task;Assert.Equal(size-8,result.RootElement.GetProperty("x").GetString()!.Length);}
        else Assert.Equal("FILES_OUTPUT_LIMIT",(await Assert.ThrowsAsync<InvalidOperationException>(()=>task)).Message);
    }
    public void Dispose(){File.Delete(_file);CryptographicOperations.ZeroMemory(_key);}
}
