using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchivePolicyTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task NativePythonDigestMatchesUnicodeNanosecondsAndPrivatePresence(bool present)
    {
        var p=Plan(present);var module=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../deploy/host/whaledeck-container-recycle-archive-plan.py"));Assert.True(File.Exists(module));
        var start=new System.Diagnostics.ProcessStartInfo("python3"){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("-I");start.ArgumentList.Add("-B");start.ArgumentList.Add("-c");start.ArgumentList.Add("import importlib.util,json,sys; s=importlib.util.spec_from_file_location('archive',sys.argv[1]); m=importlib.util.module_from_spec(s); s.loader.exec_module(m); print(m.ContainerRecycleArchivePreflight.digest(json.load(sys.stdin)))");start.ArgumentList.Add(module);
        using var child=System.Diagnostics.Process.Start(start)!;await child.StandardInput.WriteAsync(JsonSerializer.Serialize(ContainerRecycleArchiveWire.Plan(p)));child.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try{await child.WaitForExitAsync(timeout.Token);}catch{child.Kill(entireProcessTree:true);throw;}
        Assert.Equal(0,child.ExitCode);Assert.Equal("",await child.StandardError.ReadToEndAsync());Assert.Equal(p.Fingerprint,(await child.StandardOutput.ReadToEndAsync()).Trim());
    }
    internal static ContainerRecycleArchivePlan Plan(bool privatePresent=false)
    {
        var old=ContainerRecyclePurgePolicyTests.Plan();var p=new ContainerRecycleArchivePlan{SchemaVersion=1,Command=new(){Purge=new(){OperationId="cc0a8865343e49bdb01a72680131bf39",Owner=new('E',64),Plan=old},HistoryDigest=old.Command.HistoryDigest,Mode="ArchiveCopy"},TargetProof=old.TargetProof,ParentIdentity=old.ParentIdentity.Clone(),ParentVersion=old.ParentVersion,OwnerUid=old.OwnerUid,OwnerGid=old.OwnerGid,PrivateIdentity=privatePresent?old.PrivateIdentity.Clone():null,DeletedIdentity=old.RetainedIdentity.Clone(),DeletedChangedAtUnixNs=old.RetainedChangedAtUnixNs,SourceRecordDigest=new('4',64),PurgeRecordDigest=new('5',64),PurgeMarkerDigest=new('6',64),HistoryMetadataDigest=new('7',64),StorageHash=old.StorageHash,StoreIdentity=old.StoreIdentity.Clone(),ArchiveIdentity=old.ArchiveIdentity.Clone(),ArchiveChangedAtUnixNs=old.ArchiveChangedAtUnixNs,ArchiveBytes=old.ArchiveBytes,ArchiveSha256=old.ArchiveSha256};p.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(p);return p;
    }
    internal static ContainerRecycleArchiveRequest Request()=>new(){OperationId="dd0a8865343e49bdb01a72680131bf39",Owner=new('A',64),Plan=Plan(),Context=new(){ActorSubject="synthetic-admin",TraceId="synthetic-trace",JobId="dd0a8865343e49bdb01a72680131bf39",IdempotencyKey="bb0a8865343e49bdb01a72680131bf39"}};
    internal static object ReceiptInput(ContainerRecycleArchiveRequest q,string state="Completed",bool? binding=true)
    {
        var original=q.Plan.Command.Purge.Plan.Command.Source.Plan.SourceIdentity;var published=original.Clone();published.Device=q.Plan.ParentIdentity.Device;published.Inode+=1000;
        return new{operationId=q.OperationId,sourceOperationId=q.Plan.Command.Purge.Plan.Command.Source.OperationId,purgeOperationId=q.Plan.Command.Purge.OperationId,fingerprint=q.Plan.Fingerprint,state,sourceFenced=state!="Aborted",ownedFence=state!="Aborted",archiveCopied=state=="Unknown"?(bool?)null:state=="Completed",bindingCurrent=state=="Completed"?binding:null,publishedIdentity=state=="Completed"?ContainerRecycleArchiveWire.Identity(published,1793925351911000007):null};
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void FullNativePlanRoundTripKeepsNullAndExistingPrivateDirectoryDistinct(bool present)
    {var p=Plan(present);ContainerRecycleArchivePolicy.Plan(p);ContainerRecycleArchivePolicy.Request(Request());using var doc=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleArchiveWire.Plan(p)));Assert.Equal(19,doc.RootElement.EnumerateObject().Count());Assert.Equal(p,ContainerRecycleArchivePolicy.PlanOutput(doc.RootElement,p.Command));Assert.Equal(!present,doc.RootElement.GetProperty("privateIdentity").ValueKind==JsonValueKind.Null);}
    [Theory]
    [InlineData("mode")][InlineData("purge-id")][InlineData("history")][InlineData("parent")][InlineData("private")][InlineData("deleted")][InlineData("deleted-time")][InlineData("store")][InlineData("archive")][InlineData("bytes")][InlineData("sha")][InlineData("source-digest")][InlineData("purge-digest")][InlineData("marker-digest")]
    public void InvalidPlanCannotBeRehashedIntoAuthority(string field)
    {
        var p=Plan();switch(field){case "mode":p.Command.Mode="Originals";break;case "purge-id":p.Command.Purge.OperationId=p.Command.Purge.Plan.Command.Source.OperationId;break;case "history":p.Command.HistoryDigest=new('A',64);break;case "parent":p.ParentIdentity.Inode++;break;case "private":p.PrivateIdentity=new(){Device=0,Inode=1};break;case "deleted":p.DeletedIdentity.Inode++;break;case "deleted-time":p.DeletedChangedAtUnixNs++;break;case "store":p.StoreIdentity.Inode++;break;case "archive":p.ArchiveIdentity.Inode++;break;case "bytes":p.ArchiveBytes++;break;case "sha":p.ArchiveSha256=new('F',64);break;case "source-digest":p.SourceRecordDigest="";break;case "purge-digest":p.PurgeRecordDigest="";break;default:p.PurgeMarkerDigest="";break;}
        p.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(p);Assert.Throws<ArgumentException>(()=>ContainerRecycleArchivePolicy.Plan(p));
    }
    [Theory][InlineData("request")][InlineData("command")][InlineData("purge")][InlineData("purge-plan")][InlineData("source-plan")][InlineData("archive")][InlineData("context")]
    public void NestedUnknownFieldsNeverReachExecution(string field)
    {
        var q=Request();var unknown=new byte[]{0xA0,0x06,0x01};
        switch(field){case "request":q=ContainerRecycleArchiveRequest.Parser.ParseFrom(q.ToByteArray().Concat(unknown).ToArray());break;case "command":q.Plan.Command=ContainerRecycleArchiveCommand.Parser.ParseFrom(q.Plan.Command.ToByteArray().Concat(unknown).ToArray());break;case "purge":q.Plan.Command.Purge=ContainerRecycleArchiveSource.Parser.ParseFrom(q.Plan.Command.Purge.ToByteArray().Concat(unknown).ToArray());break;case "purge-plan":q.Plan.Command.Purge.Plan=ContainerRecyclePurgePlan.Parser.ParseFrom(q.Plan.Command.Purge.Plan.ToByteArray().Concat(unknown).ToArray());break;case "source-plan":q.Plan.Command.Purge.Plan.Command.Source.Plan=ContainerRecyclePlan.Parser.ParseFrom(q.Plan.Command.Purge.Plan.Command.Source.Plan.ToByteArray().Concat(unknown).ToArray());break;case "archive":q.Plan.ArchiveIdentity=FileObjectIdentity.Parser.ParseFrom(q.Plan.ArchiveIdentity.ToByteArray().Concat(unknown).ToArray());break;default:q.Context=RequestContext.Parser.ParseFrom(q.Context.ToByteArray().Concat(unknown).ToArray());break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecycleArchivePolicy.Request(q));
    }
    [Theory][InlineData("Reserved")][InlineData("Aborted")][InlineData("Unknown")][InlineData("Completed")]
    public void ReceiptPreservesHistoricalResultAndNullableCurrentBinding(string state)
    {var q=Request();using var doc=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q,state,null)));var r=ContainerRecycleArchivePolicy.Receipt(doc.RootElement,q);Assert.Equal(state!="Unknown",r.HasArchiveCopied);Assert.False(r.HasBindingCurrent);if(state=="Completed")Assert.NotNull(r.PublishedIdentity);else Assert.Null(r.PublishedIdentity);}
    [Theory][InlineData(false)][InlineData(null)]
    public void HistoricalCompletedWithForeignClaimNeverMeansVerifiedCurrentBinding(bool? binding)
    {
        var q=Request();using var doc=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q,"Completed",binding)));var r=ContainerRecycleArchivePolicy.Receipt(doc.RootElement,q);r.OwnedFence=false;
        ContainerRecycleArchiveWire.Receipt(q,r);r.BindingCurrent=true;Assert.Throws<InvalidOperationException>(()=>ContainerRecycleArchiveWire.Receipt(q,r));
    }
    [Theory][InlineData("old-inode")][InlineData("archive-inode")][InlineData("owner")][InlineData("mode")][InlineData("size")][InlineData("mtime")][InlineData("link")][InlineData("missing-time")][InlineData("extra")][InlineData("source-id")][InlineData("purge-id")][InlineData("unknown-false")][InlineData("unknown-binding")]
    public void ForgedPublicationOrUnknownFalseIsRejected(string field)
    {
        var q=Request();using var doc=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q,field.StartsWith("unknown-",StringComparison.Ordinal)?"Unknown":"Completed")));var r=ContainerRecycleArchivePolicy.Receipt(doc.RootElement,q);
        switch(field){case "old-inode":r.PublishedIdentity=q.Plan.Command.Purge.Plan.Command.Source.Plan.SourceIdentity.Clone();break;case "archive-inode":r.PublishedIdentity=q.Plan.ArchiveIdentity.Clone();break;case "owner":r.PublishedIdentity.OwnerUid++;break;case "mode":r.PublishedIdentity.Mode++;break;case "size":r.PublishedIdentity.SizeBytes++;break;case "mtime":r.PublishedIdentity.ModifiedAtUnixNs++;break;case "link":r.PublishedIdentity.LinkCount=2;break;case "missing-time":r.ClearPublishedChangedAtUnixNs();break;case "source-id":r.SourceOperationId=q.OperationId;break;case "purge-id":r.PurgeOperationId=q.OperationId;break;case "unknown-false":r.ArchiveCopied=false;break;case "unknown-binding":r.BindingCurrent=true;break;default:r=ContainerRecycleArchiveReceipt.Parser.ParseFrom(r.ToByteArray().Concat(new byte[]{0xA0,0x06,0x01}).ToArray());break;}
        Assert.Throws<InvalidOperationException>(()=>ContainerRecycleArchiveWire.Receipt(q,r));
    }
    [Fact]
    public void NativeDuplicatesExtraFieldsAndLossyNumbersFailClosed()
    {
        var p=Plan();var raw=JsonSerializer.Serialize(ContainerRecycleArchiveWire.Plan(p));foreach(var bad in new[]{raw[..^1]+",\"archiveBytes\":0}",raw[..^1]+",\"extra\":true}"}){using var doc=JsonDocument.Parse(bad);Assert.Throws<InvalidOperationException>(()=>ContainerRecycleArchivePolicy.PlanOutput(doc.RootElement,p.Command));}
        foreach(var number in new[]{"true","1.5","9007199254740993.0"}){var node=JsonNode.Parse(raw)!;node["archiveBytes"]=JsonNode.Parse(number);using var doc=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecycleArchivePolicy.PlanOutput(doc.RootElement,p.Command));}
    }
    [Fact]
    public async Task ArchiveCapabilitiesDeclareSeparateProtocolAndDedicatedCapability()
    {var c=await new AgentGrpcService(null!).GetCapabilities(new(),null!);Assert.Equal(59U,c.ProtocolMinor);Assert.Contains("docker.files.recycle.archive",c.Capabilities);}
}
