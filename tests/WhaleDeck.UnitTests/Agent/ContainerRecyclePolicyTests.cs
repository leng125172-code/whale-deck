using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecyclePolicyTests
{
    internal static ContainerRecyclePlan Plan() => new()
    {
        SchemaVersion=1, Command=new() { Location=new() { ContainerId=new('a',64), InstanceId="777:2026-10-27T01:02:03.123456789Z", Path="work/中文.bin" }, ExpectedVersion=new('9',64) },
        TargetProof=new('A',64), ParentVersion=new('B',64), Fingerprint=new('C',64), ParentIdentity=new() { Device=101, Inode=202 }, OwnerUid=1000, OwnerGid=1001,
        SourceIdentity=new() { Device=3, Inode=4, Mode=0x81A0, OwnerUid=2100, OwnerGid=3100, LinkCount=1, SizeBytes=1048583, ModifiedAtUnixNs=1722222222222222222 },
        SourceChangedAtUnixNs=1733333333333333333, Sha256=new('8',64),
        Attributes={new ContainerRecycleAttribute { Name="user.note", Value=ByteString.CopyFrom([0,255,1]) }}
    };
    internal static ContainerRecycleRequest Request() => new()
    {
        Plan=Plan(), OperationId="aa0a8865343e49bdb01a72680131bf39", Owner=new('D',64),
        Context=new() { ActorSubject="synthetic-admin", TraceId="synthetic-trace", JobId="aa0a8865343e49bdb01a72680131bf39", IdempotencyKey="bb0a8865343e49bdb01a72680131bf39" }
    };
    internal static object ReceiptInput(ContainerRecycleRequest r,string state="Trashed") => new
    {
        operationId=r.OperationId,containerId=r.Plan.Command.Location.ContainerId,instanceId=r.Plan.Command.Location.InstanceId,fingerprint=r.Plan.Fingerprint,targetProof=r.Plan.TargetProof,
        state,bindingCurrent=state is "Trashed" or "Restored",replayed=true,historyMayExist=true,historyVerified=true,historyDigest=new string('7',64),historyAvailable=true,
        sourceRetained=state=="Unknown"?(bool?)null:state=="Trashed",sourceRestored=state=="Unknown"?(bool?)null:state=="Restored"
    };
    [Fact]
    public void RootPlanRoundTripKeepsRawAttributesAndNanoseconds()
    {
        var expected=Plan();using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecyclePolicy.PlanInput(expected)));
        Assert.Equal(expected,ContainerRecyclePolicy.PlanOutput(json.RootElement,expected.Command));Assert.Equal(11,json.RootElement.EnumerateObject().Count());
        Assert.Equal("AP8B",json.RootElement.GetProperty("attributes")[0][1].GetString());Assert.DoesNotContain("rootId",json.RootElement.GetRawText(),StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("schema")][InlineData("mode")][InlineData("size")][InlineData("link")][InlineData("uid")][InlineData("gid")][InlineData("hash")][InlineData("attribute")][InlineData("duplicate")][InlineData("oversize")]
    public void InvalidPlanCannotReachRoot(string field)
    {
        var p=Plan();
        switch(field)
        {
            case "schema":p.SchemaVersion=2;break;case "mode":p.SourceIdentity.Mode|=0x800;break;case "size":p.SourceIdentity.SizeBytes=1073741825;break;
            case "link":p.SourceIdentity.LinkCount=2;break;case "uid":p.OwnerUid=uint.MaxValue;break;case "gid":p.SourceIdentity.OwnerGid=uint.MaxValue;break;case "hash":p.Sha256="bad";break;
            case "attribute":p.Attributes[0].Name="security.capability";break;case "duplicate":p.Attributes.Add(p.Attributes[0].Clone());break;
            case "oversize":p.Attributes[0].Value=ByteString.CopyFrom(new byte[4096]);break;
        }
        Assert.Throws<ArgumentException>(()=>ContainerRecyclePolicy.Plan(p));
    }
    [Fact]
    public void UnicodeAttributeOrderMatchesKernelCodepointOrderNotUtf16Order()
    {
        var p=Plan();p.Attributes.Clear();p.Attributes.Add(new ContainerRecycleAttribute {Name="user.\uE000"});p.Attributes.Add(new ContainerRecycleAttribute {Name="user.😀"});ContainerRecyclePolicy.Plan(p);
        var a=p.Attributes[0];p.Attributes[0]=p.Attributes[1];p.Attributes[1]=a;Assert.Throws<ArgumentException>(()=>ContainerRecyclePolicy.Plan(p));
    }
    [Fact]
    public void RestoreUsesNewJobWhileInspectReferencesOriginalAndEmbeddedCapabilityIsForbidden()
    {
        var r=Request();ContainerRecyclePolicy.Request(r);Assert.Throws<ArgumentException>(()=>ContainerRecyclePolicy.Request(r,restoring:true));
        r.Context.JobId="cc0a8865343e49bdb01a72680131bf39";ContainerRecyclePolicy.Request(r,restoring:true);Assert.Throws<ArgumentException>(()=>ContainerRecyclePolicy.Request(r));
        r.Context.Capability=ByteString.CopyFromUtf8("not embedded");Assert.Throws<ArgumentException>(()=>ContainerRecyclePolicy.Request(r,restoring:true));
    }
    [Theory]
    [InlineData("Trashed")][InlineData("Restored")][InlineData("Aborted")][InlineData("Unknown")][InlineData("Purged")]
    public void StateFactsRemainDistinctAndUnknownIsNotFalse(string state)
    {
        var r=Request();var node=JsonSerializer.SerializeToNode(ReceiptInput(r,state))!;using var good=JsonDocument.Parse(node.ToJsonString());
        var result=ContainerRecyclePolicy.Receipt(good.RootElement,r);Assert.Equal(state,result.State);Assert.Equal(state!="Unknown",result.HasSourceRetained);
        node["sourceRetained"]=state=="Unknown"?false:(bool?)null;using var bad=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePolicy.Receipt(bad.RootElement,r));
    }
    [Fact]
    public void OutputRejectsSubstitutionExtraFieldsAndNoncanonicalAttributeEncoding()
    {
        var p=Plan();var node=JsonSerializer.SerializeToNode(ContainerRecyclePolicy.PlanInput(p))!;node["hostPath"]="/etc";
        using var extra=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePolicy.PlanOutput(extra.RootElement,p.Command));
        node.AsObject().Remove("hostPath");node["attributes"]![0]![1]="AP8B\n";using var encoded=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePolicy.PlanOutput(encoded.RootElement,p.Command));
        var r=Request();node=JsonSerializer.SerializeToNode(ReceiptInput(r))!;node["historyAvailable"]=true;node["historyVerified"]=false;
        using var wrong=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecyclePolicy.Receipt(wrong.RootElement,r));
    }
    [Theory]
    [InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")][InlineData("restore")]
    public void ProcessArgumentsCarryOnlyFixedTransportAndInvocation(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.NonPublic|BindingFlags.Static)!;
        const string id="aa0a8865343e49bdb01a72680131bf39",inv="bb0a8865343e49bdb01a72680131bf39";
        var start=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle:"+action,id,null]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycling",action,id},start.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle:"+action,id,inv]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycling-stop",id,inv},stop.ArgumentList);
        Assert.False(start.UseShellExecute);Assert.Equal(2,start.Environment.Count);
    }
    [Fact]
    public async Task UnknownActionIsRejectedWithoutProcessStart()
    {await Assert.ThrowsAsync<ArgumentException>(()=>new FileTransferRunner().ContainerRecycleOnceAsync("shell","{}",default));}
    [Fact]
    public void FailureCodesStayBoundedAndPrivateDetailsAreNotReturned()
    {
        Assert.Equal(StatusCode.NotFound,ContainerRecyclePolicy.Failure("CONTAINER_RECYCLE_NOT_FOUND").StatusCode);
        Assert.Equal(StatusCode.Aborted,ContainerRecyclePolicy.Failure("CONTAINER_RECYCLE_TARGET_EXISTS").StatusCode);
        Assert.Equal(StatusCode.FailedPrecondition,ContainerRecyclePolicy.Failure("CONTAINER_RECYCLE_RESTORE_CLAIMED").StatusCode);
        Assert.Equal("CONTAINER_RECYCLE_UNAVAILABLE",ContainerRecyclePolicy.Failure("private arbitrary detail").Status.Detail);
    }
    [Fact]
    public async Task RecycleInputWaitsForBoundReadyAndCanceledCommitCannotReleaseAnOperation()
    {
        using var bytes=new MemoryStream();using var writer=new StreamWriter(bytes,leaveOpen:true);
        await FileTransferPipe.OpenBeforeReady(writer,"container-recycle:apply","private request",default);
        Assert.Equal(0,bytes.Length);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>FileTransferPipe.CommitAfterReady(writer,"container-recycle:apply","private request",canceled.Token));
        Assert.Equal(0,bytes.Length);
        await FileTransferPipe.CommitAfterReady(writer,"container-recycle:apply","private request",default);
        Assert.True(bytes.Length>0);
    }
}
