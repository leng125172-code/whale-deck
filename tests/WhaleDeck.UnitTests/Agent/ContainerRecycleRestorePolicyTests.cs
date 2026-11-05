using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleRestorePolicyTests
{
    internal static ContainerRecycleRestorePlan Plan()
    {
        var source=ContainerRecyclePolicyTests.Plan();var p=new ContainerRecycleRestorePlan{SchemaVersion=1,Command=new(){Source=new(){OperationId="aa0a8865343e49bdb01a72680131bf39",Owner=new('D',64),Plan=source},HistoryDigest=new('7',64)},
            TargetProof=source.TargetProof,ParentIdentity=source.ParentIdentity.Clone(),ParentVersion=new('B',64),OwnerUid=source.OwnerUid,OwnerGid=source.OwnerGid,PrivateIdentity=new(){Device=0,Inode=303},RetainedIdentity=source.SourceIdentity.Clone(),RetainedChangedAtUnixNs=source.SourceChangedAtUnixNs+100};
        p.Fingerprint=ContainerRecycleRestoreWire.Fingerprint(p);return p;
    }
    internal static ContainerRecycleRestoreRequest Request()=>new(){OperationId="cc0a8865343e49bdb01a72680131bf39",Owner=new('E',64),Plan=Plan(),Context=new(){ActorSubject="synthetic-admin",TraceId="synthetic-trace",JobId="cc0a8865343e49bdb01a72680131bf39",IdempotencyKey="bb0a8865343e49bdb01a72680131bf39"}};
    [Theory]
    [InlineData("source")][InlineData("history")][InlineData("private")][InlineData("ctime")][InlineData("parent-version")]
    public void StructurallyValidRehashedTamperingStillChangesTheSignedRequest(string field)
    {
        var original=Request();var changed=original.Clone();
        switch(field){case "source":changed.Plan.Command.Source.Owner=new('F',64);break;case "history":changed.Plan.Command.HistoryDigest=new('F',64);break;
            case "private":changed.Plan.PrivateIdentity.Inode++;break;case "ctime":changed.Plan.RetainedChangedAtUnixNs++;break;default:changed.Plan.ParentVersion=new('F',64);break;}
        changed.Plan.Fingerprint=ContainerRecycleRestoreWire.Fingerprint(changed.Plan);
        ContainerRecycleRestorePolicy.Request(changed);
        Assert.NotEqual(original.Plan.Fingerprint,changed.Plan.Fingerprint);
        Assert.NotEqual(ContainerRecycleRestoreBinding.Hash(original),ContainerRecycleRestoreBinding.Hash(changed));
    }
    internal static object ReceiptInput(ContainerRecycleRestoreRequest q,string state="Completed",bool claimed=true)=>new
    {
        operationId=q.OperationId,sourceOperationId=q.Plan.Command.Source.OperationId,fingerprint=q.Plan.Fingerprint,targetProof=q.Plan.TargetProof,state,bindingCurrent=state=="Completed",replayed=true,
        historyVerified=claimed,historyDigest=claimed?q.Plan.Command.HistoryDigest:"",historyAvailable=claimed,
        sourceState=state=="Completed"?"Restored":state=="Aborted"&&claimed?"Trashed":null,
        sourceRetained=state=="Unknown"||!claimed?(bool?)null:state=="Aborted",sourceRestored=state=="Unknown"||!claimed?(bool?)null:state=="Completed",restoredByThisOperation=state=="Unknown"?(bool?)null:state=="Completed"
    };
    [Fact]
    public void NativeProjectionRoundTripsAllSourceBytesAndIndependentIdentity()
    {
        var p=Plan();using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleRestoreWire.Plan(p)));
        Assert.Equal(p,ContainerRecycleRestorePolicy.PlanOutput(json.RootElement,p.Command));Assert.Equal(10,json.RootElement.EnumerateObject().Count());
        Assert.Equal("AP8B",json.RootElement.GetProperty("command").GetProperty("source").GetProperty("plan").GetProperty("attributes")[0][1].GetString());
        var q=Request();ContainerRecycleRestorePolicy.Request(q);Assert.NotEqual(q.OperationId,q.Plan.Command.Source.OperationId);
    }
    [Fact]
    public void PythonCanonicalGoldenKeepsControlUnicodeZeroUnsignedMaxAndSignedNanoseconds()
    {
        var p=Plan();var source=p.Command.Source.Plan;source.OwnerUid=source.OwnerGid=0;source.ParentIdentity.Device=ulong.MaxValue;
        source.SourceIdentity.Device=ulong.MaxValue;source.SourceIdentity.OwnerUid=source.SourceIdentity.OwnerGid=0;source.SourceIdentity.ModifiedAtUnixNs=-1;source.SourceChangedAtUnixNs=-1;
        source.Attributes[0].Name="user.\u001a中文\u2028🍚";source.Command.ExpectedVersion="80D1C9EC338DB093BCA7041B3C1BEE36693C36EEEB749D89F037BAC0E0417778";source.Fingerprint="594B5D62564964F20DDF7100AE41B35E31D470132859ED041EAC6DB5C6728659";
        p.ParentIdentity.Device=ulong.MaxValue;p.OwnerUid=p.OwnerGid=0;p.RetainedIdentity=source.SourceIdentity.Clone();p.RetainedChangedAtUnixNs=0;
        p.Fingerprint=ContainerRecycleRestoreWire.Fingerprint(p);Assert.Equal("CBC1CA2E3EF568CA69B84CFF3B86C3AAEA1282EDB6C9A5198EBF0B4B367AA45B",p.Fingerprint);ContainerRecycleRestorePolicy.Plan(p);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleRestoreWire.Plan(p)));Assert.Equal(p,ContainerRecycleRestorePolicy.PlanOutput(json.RootElement,p.Command));
    }
    [Theory]
    [InlineData("schema")][InlineData("source")][InlineData("parent")][InlineData("private")][InlineData("uid")][InlineData("identity")][InlineData("ctime")][InlineData("fingerprint")]
    public void BadPlanCannotReachRoot(string field)
    {
        var p=Plan();switch(field){case "schema":p.SchemaVersion=2;break;case "source":p.Command.Source.OperationId="";break;case "parent":p.ParentIdentity.Inode++;break;case "private":p.PrivateIdentity.Inode=0;break;case "uid":p.OwnerUid++;break;case "identity":p.RetainedIdentity.Inode++;break;case "ctime":p.RetainedChangedAtUnixNs=0;break;case "fingerprint":p.Fingerprint=new('1',64);break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecycleRestorePolicy.Plan(p));
    }
    [Theory]
    [InlineData("source-id")][InlineData("job")][InlineData("capability")][InlineData("key")]
    public void NewOperationAndContextCannotBeReplacedByOriginalTrash(string field)
    {
        var q=Request();switch(field){case "source-id":q.OperationId=q.Context.JobId=q.Plan.Command.Source.OperationId;break;case "job":q.Context.JobId=q.Plan.Command.Source.OperationId;break;case "capability":q.Context.Capability=ByteString.CopyFromUtf8("embedded");break;case "key":q.Context.IdempotencyKey="";break;}
        Assert.Throws<ArgumentException>(()=>ContainerRecycleRestorePolicy.Request(q));
    }
    [Theory]
    [InlineData("Completed",true)][InlineData("Aborted",true)][InlineData("Aborted",false)][InlineData("Unknown",true)]
    public void ReceiptKeepsUnclaimedAndUnknownDistinctFromFalse(string state,bool claimed)
    {
        var q=Request();using var doc=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q,state,claimed)));var r=ContainerRecycleRestorePolicy.Receipt(doc.RootElement,q);
        Assert.Equal(state,r.State);Assert.Equal(claimed,r.HistoryVerified);
        if(state=="Unknown"){Assert.False(r.HasRestoredByThisOperation);Assert.False(r.HasSourceRetained);Assert.False(r.HasSourceRestored);}
        if(state=="Aborted"){Assert.True(r.HasRestoredByThisOperation);Assert.False(r.RestoredByThisOperation);}
    }
    [Theory]
    [InlineData("source")][InlineData("new-id")][InlineData("binding")][InlineData("presence")][InlineData("digest")][InlineData("source-state")]
    public void CorruptReceiptNeverBecomesSuccess(string field)
    {
        var q=Request();using var json=JsonDocument.Parse(JsonSerializer.Serialize(ReceiptInput(q)));var r=ContainerRecycleRestorePolicy.Receipt(json.RootElement,q);
        switch(field){case "source":r.SourceOperationId=q.OperationId;break;case "new-id":r.OperationId=q.Plan.Command.Source.OperationId;break;case "binding":r.State="Aborted";break;case "presence":r.ClearRestoredByThisOperation();break;case "digest":r.HistoryDigest=new('6',64);break;case "source-state":r.SourceState="";break;}
        Assert.Throws<InvalidOperationException>(()=>ContainerRecycleRestoreWire.Receipt(q,r));
    }
    [Fact]
    public void UnknownFieldsAreRejectedRecursivelyAndNativeExtraFieldsRejected()
    {
        var q=Request();q.Plan.Command.Source.Plan=ContainerRecyclePlan.Parser.ParseFrom(q.Plan.Command.Source.Plan.ToByteArray().Concat(new byte[]{0xA0,0x06,0x01}).ToArray());
        Assert.Throws<ArgumentException>(()=>ContainerRecycleRestorePolicy.Request(q));
        var p=Plan();var raw=JsonSerializer.Serialize(ContainerRecycleRestoreWire.Plan(p));using var extra=JsonDocument.Parse(raw[..^1]+",\"hostPath\":\"/etc\"}");
        Assert.Throws<InvalidOperationException>(()=>ContainerRecycleRestorePolicy.PlanOutput(extra.RootElement,p.Command));
    }
    private static T WithUnknown<T>(T value) where T:IMessage<T> => (T)value.Descriptor.Parser.ParseFrom(value.ToByteArray().Concat(new byte[]{0xA0,0x06,0x01}).ToArray());
    [Fact]
    public void RecursiveDiscardParserRejectsUnknownAtEveryNestedMessageDepthDirectly()
    {
        Action<ContainerRecycleRestoreRequest>[] changes=[
            q=>q.Context=WithUnknown(q.Context),q=>q.Plan=WithUnknown(q.Plan),q=>q.Plan.PrivateIdentity=WithUnknown(q.Plan.PrivateIdentity),
            q=>q.Plan.RetainedIdentity=WithUnknown(q.Plan.RetainedIdentity),q=>q.Plan.Command=WithUnknown(q.Plan.Command),q=>q.Plan.Command.Source=WithUnknown(q.Plan.Command.Source),
            q=>q.Plan.Command.Source.Plan=WithUnknown(q.Plan.Command.Source.Plan),q=>q.Plan.Command.Source.Plan.Command.Location=WithUnknown(q.Plan.Command.Source.Plan.Command.Location),
            q=>q.Plan.Command.Source.Plan.Attributes[0]=WithUnknown(q.Plan.Command.Source.Plan.Attributes[0])];
        foreach(var change in changes){var q=Request();ContainerRecycleRestoreWire.RequireKnown(q);change(q);Assert.Throws<ArgumentException>(()=>ContainerRecycleRestoreWire.RequireKnown(q));}
    }
    [Fact]
    public async Task NewDriverWaitsForExactReadyBeforeBodyAndUsesFixedMethods()
    {
        using var stream=new MemoryStream();using var writer=new StreamWriter(stream,leaveOpen:true);
        await FileTransferPipe.OpenBeforeReady(writer,"container-recycle-restore:apply","request",default);Assert.Equal(0,stream.Length);
        using var canceled=new CancellationTokenSource();canceled.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>FileTransferPipe.CommitAfterReady(writer,"container-recycle-restore:apply","request",canceled.Token));Assert.Equal(0,stream.Length);
        await FileTransferPipe.CommitAfterReady(writer,"container-recycle-restore:apply","request",default);Assert.True(stream.Length>0);
        Assert.Equal(StatusCode.NotFound,ContainerRecycleRestorePolicy.Failure("CONTAINER_RECYCLE_RESTORE_NOT_FOUND").StatusCode);
        Assert.Equal(StatusCode.Aborted,ContainerRecycleRestorePolicy.Failure("CONTAINER_RECYCLE_TARGET_EXISTS").StatusCode);
        Assert.Equal(StatusCode.FailedPrecondition,ContainerRecycleRestorePolicy.Failure("FILES_BACKUP_CONFIGURATION_INVALID").StatusCode);
        Assert.Equal("CONTAINER_RECYCLE_RESTORE_UNAVAILABLE",ContainerRecycleRestorePolicy.Failure("private detail").Status.Detail);
    }
}
