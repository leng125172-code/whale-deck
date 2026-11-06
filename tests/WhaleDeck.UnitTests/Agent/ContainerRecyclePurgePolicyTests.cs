using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecyclePurgePolicyTests
{
    internal static ContainerRecyclePurgePlan Plan()
    {
        var old=ContainerRecycleRestorePolicyTests.Plan();var p=new ContainerRecyclePurgePlan{SchemaVersion=1,Command=new(){Source=new(){OperationId=old.Command.Source.OperationId,Owner=old.Command.Source.Owner,Plan=old.Command.Source.Plan.Clone()},HistoryDigest=old.Command.HistoryDigest,Scope="Originals"},TargetProof=old.TargetProof,ParentIdentity=old.ParentIdentity.Clone(),ParentVersion=old.ParentVersion,OwnerUid=old.OwnerUid,OwnerGid=old.OwnerGid,PrivateIdentity=old.PrivateIdentity.Clone(),RetainedIdentity=old.RetainedIdentity.Clone(),RetainedChangedAtUnixNs=old.RetainedChangedAtUnixNs,
            SourceRecordDigest=new('1',64),HistoryMetadataDigest=new('2',64),HistoryCreatedAtUnixMs=1793925351911,StorageHash=new('3',64),StoreIdentity=new(){Device=0,Inode=404},ArchiveIdentity=new(){Device=0,Inode=505,Mode=33152,OwnerUid=0,OwnerGid=0,LinkCount=1,SizeBytes=old.RetainedIdentity.SizeBytes,ModifiedAtUnixNs=1793925351911000000},ArchiveChangedAtUnixNs=1793925351911000001,ArchiveBytes=old.RetainedIdentity.SizeBytes,ArchiveSha256=old.Command.Source.Plan.Sha256};
        p.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(p);return p;
    }
    internal static ContainerRecyclePurgeRequest Request()=>new(){OperationId="cc0a8865343e49bdb01a72680131bf39",Owner=new('E',64),Plan=Plan(),Context=new(){ActorSubject="synthetic-admin",TraceId="synthetic-trace",JobId="cc0a8865343e49bdb01a72680131bf39",IdempotencyKey="bb0a8865343e49bdb01a72680131bf39"}};
    internal static object ReceiptInput(ContainerRecyclePurgeRequest q,string state="Completed")=>new{operationId=q.OperationId,sourceOperationId=q.Plan.Command.Source.OperationId,fingerprint=q.Plan.Fingerprint,state,sourceFenced=state!="Aborted",ownedFence=state!="Aborted",purgeApplied=state=="Unknown"?(bool?)null:state=="Completed"};
    [Fact]
    public void CompleteNativePlanRoundTripBindsBothCopiesAndExactNumbers()
    {var p=Plan();ContainerRecyclePurgePolicy.Plan(p);using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecyclePurgeWire.Plan(p)));Assert.Equal(18,json.RootElement.EnumerateObject().Count());Assert.Equal(p,ContainerRecyclePurgePolicy.PlanOutput(json.RootElement,p.Command));ContainerRecyclePurgePolicy.Request(Request());}
    [Fact]
    public void PythonCanonicalGoldenKeepsUnicodeAndExactNanosecondIntegers()
    {Assert.Equal("F0AFA7F8C6B612D0D3CFEFFAD57186EB1D5C2736F4082BA0D0D08C0E4D3EA782",Plan().Fingerprint);}
    [Theory]
    [InlineData("scope")][InlineData("parent")][InlineData("store")][InlineData("source")][InlineData("retained")][InlineData("archive-owner")][InlineData("archive-mode")][InlineData("archive-link")][InlineData("archive-size")][InlineData("archive-hash")][InlineData("archive-ctime")][InlineData("time")][InlineData("hash")]
    public void InvalidPlanIsRejectedEvenAfterRehash(string field)
    {
        var p=Plan();switch(field){case "scope":p.Command.Scope="All";break;case "parent":p.ParentIdentity.Inode++;break;case "store":p.StoreIdentity.Inode=0;break;case "source":p.Command.Source.OperationId="";break;case "retained":p.RetainedIdentity.Inode++;break;case "archive-owner":p.ArchiveIdentity.OwnerUid=1;break;case "archive-mode":p.ArchiveIdentity.Mode=33188;break;case "archive-link":p.ArchiveIdentity.LinkCount=2;break;case "archive-size":p.ArchiveBytes++;break;case "archive-hash":p.ArchiveSha256=new('4',64);break;case "archive-ctime":p.ArchiveChangedAtUnixNs=-1;break;case "time":p.HistoryCreatedAtUnixMs=-1;break;default:p.SourceRecordDigest="";break;}
        p.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(p);Assert.Throws<ArgumentException>(()=>ContainerRecyclePurgePolicy.Plan(p));
    }
    [Theory]
    [InlineData("command")][InlineData("source")][InlineData("source-plan")][InlineData("archive")][InlineData("store")][InlineData("context")][InlineData("request")]
    public void NestedUnknownProtobufFieldsNeverReachRoot(string field)
    {
        var q=Request();var unknown=new byte[]{0xA0,0x06,0x01};
        switch(field){case "command":q.Plan.Command=ContainerRecyclePurgeCommand.Parser.ParseFrom(q.Plan.Command.ToByteArray().Concat(unknown).ToArray());break;case "source":q.Plan.Command.Source=ContainerRecyclePurgeSource.Parser.ParseFrom(q.Plan.Command.Source.ToByteArray().Concat(unknown).ToArray());break;case "source-plan":q.Plan.Command.Source.Plan=ContainerRecyclePlan.Parser.ParseFrom(q.Plan.Command.Source.Plan.ToByteArray().Concat(unknown).ToArray());break;case "archive":q.Plan.ArchiveIdentity=FileObjectIdentity.Parser.ParseFrom(q.Plan.ArchiveIdentity.ToByteArray().Concat(unknown).ToArray());break;case "store":q.Plan.StoreIdentity=FileDirectoryIdentity.Parser.ParseFrom(q.Plan.StoreIdentity.ToByteArray().Concat(unknown).ToArray());break;case "context":q.Context=RequestContext.Parser.ParseFrom(q.Context.ToByteArray().Concat(unknown).ToArray());break;default:q=ContainerRecyclePurgeRequest.Parser.ParseFrom(q.ToByteArray().Concat(unknown).ToArray());break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecyclePurgePolicy.Request(q));
    }
    [Theory]
    [InlineData("Reserved")][InlineData("Completed")][InlineData("Aborted")][InlineData("Unknown")]
    public void ReceiptPresenceKeepsUnknownDistinctFromFalse(string state)
    {
        var q=Request();using var json=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q,state)));var r=ContainerRecyclePurgePolicy.Receipt(json.RootElement,q);Assert.Equal(state!="Unknown",r.HasPurgeApplied);
        if(state=="Unknown")r.PurgeApplied=false;else r.ClearPurgeApplied();Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePurgeWire.Receipt(q,r));
    }
    [Fact]
    public void NativeDuplicateUnknownAndLossyValuesFailClosed()
    {
        var p=Plan();var raw=JsonSerializer.Serialize(ContainerRecyclePurgeWire.Plan(p));
        foreach(var bad in new[]{raw[..^1]+",\"archiveBytes\":0}",raw[..^1]+",\"extra\":true}"}){using var doc=JsonDocument.Parse(bad);Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePurgePolicy.PlanOutput(doc.RootElement,p.Command));}
        var n=JsonNode.Parse(raw)!;n["archiveBytes"]=true;using var wrong=JsonDocument.Parse(n.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePurgePolicy.PlanOutput(wrong.RootElement,p.Command));
    }
    [Fact]
    public async Task CapabilitiesAdvertiseNewSeparateVersionAndActionsRejectWithoutStartingProcess()
    {
        var c=await new AgentGrpcService(null!).GetCapabilities(new(),null!);Assert.Equal(59U,c.ProtocolMinor);Assert.Contains("docker.files.recycle.purge",c.Capabilities);
        await Assert.ThrowsAsync<ArgumentException>(()=>new FileTransferRunner().ContainerRecyclePurgeOnceAsync("restore","{}",default));
    }
    [Theory]
    [InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public async Task NoOperationBytesBeforeReadyAndCanceledCommitCannotFlushABody(string action)
    {
        using var bytes=new MemoryStream();using var writer=new StreamWriter(bytes,leaveOpen:true);var name="container-recycle-purge:"+action;
        await FileTransferPipe.OpenBeforeReady(writer,name,"{\"operationId\":\"private\"}",default);Assert.Equal(0,bytes.Length);
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>FileTransferPipe.CommitAfterReady(writer,name,"{}",cancel.Token));Assert.Equal(0,bytes.Length);
        await FileTransferPipe.CommitAfterReady(writer,name,"{}",default);Assert.True(bytes.Length>0);
    }
    [Theory]
    [InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void HelperAndStopRemainFixedToExactInvocation(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;var id=new string('a',32);var invocation=new string('b',32);
        var start=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-purge:"+action,id,null]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-purges",action,id},start.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-purge:"+action,id,invocation]));Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-purges-stop",id,invocation},stop.ArgumentList);Assert.False(start.UseShellExecute);Assert.Equal(2,start.Environment.Count);
    }
}
