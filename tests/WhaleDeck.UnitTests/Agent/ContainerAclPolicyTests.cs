using System.Reflection;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerAclPolicyTests
{
    internal static ContainerAclEntry[] Entries() => [new() { Tag="user_obj", Permissions=6 }, new() { Tag="user", Id=2201, Permissions=6 },
        new() { Tag="group_obj", Permissions=0 }, new() { Tag="mask", Permissions=4 }, new() { Tag="other", Permissions=0 }];
    internal static ContainerAclPlan Plan() => new()
    {
        SchemaVersion=1, Command=new() { Location=new() { ContainerId=new('a',64), InstanceId="777:2026-10-27T01:02:03.123456789Z", Path="work/中文目录" }, AclType="access", Entries={Entries()}, ExpectedVersion=new('9',64) },
        TargetProof=new('A',64), ParentVersion=new('B',64), Fingerprint=new('C',64), ParentIdentity=new() {Device=101,Inode=202}, OwnerUid=1000,OwnerGid=1001,
        Kind="File",AttributesDigest=new('8',64), SourceIdentity=new() {Device=3,Inode=4,Mode=0x81A4,OwnerUid=1000,OwnerGid=1001,LinkCount=1,SizeBytes=10737418247,ModifiedAtUnixNs=1722222222222222222},
        SourceChangedAtUnixNs=1733333333333333333, TargetMode=0x1A0,
        AccessAcl={new ContainerAclEntry {Tag="user_obj",Permissions=6},new ContainerAclEntry {Tag="group_obj",Permissions=4},new ContainerAclEntry {Tag="other",Permissions=4}}
    };
    internal static ContainerAclRequest Request() => new() {Plan=Plan(),OperationId="aa0a8865343e49bdb01a72680131bf39",Owner=new('D',64),
        Context=new() {ActorSubject="synthetic-admin",TraceId="synthetic-trace",JobId="aa0a8865343e49bdb01a72680131bf39",IdempotencyKey="bb0a8865343e49bdb01a72680131bf39"}};
    [Fact] public void FullPlanJsonRoundTripBindsBothAclListsAndDerivedMaskMode()
    {
        var plan=Plan(); using var json=JsonDocument.Parse(JsonSerializer.Serialize(ContainerAclPolicy.PlanInput(plan)));
        Assert.Equal(plan,ContainerAclPolicy.PlanOutput(json.RootElement,plan.Command));Assert.Equal(15,json.RootElement.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("accessAcl")[0].GetProperty("id").ValueKind);
    }
    [Theory][InlineData("missing-id")][InlineData("sentinel")][InlineData("missing-permissions")][InlineData("permissions")][InlineData("mask")][InlineData("duplicate")][InlineData("order")][InlineData("base-id")][InlineData("tag")][InlineData("type")][InlineData("budget")]
    public void MissingMaskIdPresenceCanonicalOrderAndBudgetFailBeforeHelper(string change)
    {
        var command=Plan().Command;
        switch(change) {
            case "missing-id":command.Entries[1].ClearId();break; case "sentinel":command.Entries[1].Id=uint.MaxValue;break;
            case "missing-permissions":command.Entries[0].ClearPermissions();break; case "permissions":command.Entries[1].Permissions=8;break;
            case "mask":command.Entries.RemoveAt(3);break; case "duplicate":command.Entries.Add(command.Entries[^1].Clone());break;
            case "order":var entry=command.Entries[0];command.Entries.RemoveAt(0);command.Entries.Add(entry);break;
            case "base-id":command.Entries[0].Id=0;break; case "tag":command.Entries[1].Tag="shell";break;case "type":command.AclType="inherit-recursive";break;
            case "budget":for(var i=0;i<64;i++)command.Entries.Add(new ContainerAclEntry {Tag="user",Id=(uint)i,Permissions=4});break;
        }
        Assert.Throws<ArgumentException>(()=>ContainerAclPolicy.Command(command));
    }
    [Fact] public void NumericZeroAndZeroPermissionsAreExplicitNotMissing()
    {
        var command=Plan().Command;command.Entries[1].Id=0;command.Entries[1].Permissions=0;ContainerAclPolicy.Command(command);
        var hash=ContainerAclBinding.Hash(command);command.Entries[1].ClearId();Assert.NotEqual(hash,ContainerAclBinding.Hash(command));
    }
    [Theory][InlineData("mode")][InlineData("old-access")][InlineData("old-default")][InlineData("no-change")][InlineData("special")][InlineData("hardlink")][InlineData("directory-default")]
    public void DerivedPlanCannotLieAboutEffectiveModeOrOriginalAcl(string change)
    {
        var plan=Plan();switch(change) {case "mode":plan.TargetMode=511;break;case "old-access":plan.AccessAcl[0].Permissions=0;break;
            case "old-default":plan.DefaultAcl.Add(Entries());break;case "no-change":plan.NoChange=true;break;case "special":plan.SourceIdentity.Mode|=0x800;break;
            case "hardlink":plan.SourceIdentity.LinkCount=2;break;case "directory-default":plan.Command.AclType="default";break;}
        Assert.Throws<ArgumentException>(()=>ContainerAclPolicy.Plan(plan));
    }
    [Theory][InlineData("true")][InlineData("\"6\"")][InlineData("-1")][InlineData("1.5")][InlineData("{}")][InlineData("4294967295")]
    public void RootJsonAclNumbersCannotBeImplicitlyCoerced(string encoded)
    {
        var plan=Plan();var node=JsonSerializer.SerializeToNode(ContainerAclPolicy.PlanInput(plan))!;node["command"]!["entries"]![1]!["id"]=System.Text.Json.Nodes.JsonNode.Parse(encoded);
        using var json=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerAclPolicy.PlanOutput(json.RootElement,plan.Command));
    }
    [Theory][InlineData("Completed",true)][InlineData("Aborted",false)][InlineData("Unknown",false)]
    public void OnlyBoundTerminalReceiptsAndAclModeAreAccepted(string state,bool binding)
    {
        var request=Request(); var node=JsonSerializer.SerializeToNode(new {operationId=request.OperationId,containerId=request.Plan.Command.Location.ContainerId,instanceId=request.Plan.Command.Location.InstanceId,
            fingerprint=request.Plan.Fingerprint,targetProof=request.Plan.TargetProof,kind="File",aclConfirmed=state=="Unknown"?(bool?)null:state=="Completed",noChange=false,
            historyMayExist=true,historyVerified=true,historyDigest=new string('7',64),state,version=state=="Completed"?new string('E',64):"",bindingCurrent=binding,replayed=true,aclType="access",targetMode=416})!;
        using(var json=JsonDocument.Parse(node.ToJsonString()))Assert.Equal(state,ContainerAclPolicy.Receipt(json.RootElement,request).State);
        node["targetMode"]=0;using var corrupt=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerAclPolicy.Receipt(corrupt.RootElement,request));
    }
    [Fact] public void ContextMustBindOriginalJobAndNeverAcceptEmbeddedAuthority()
    {
        var request=Request();ContainerAclPolicy.Request(request);request.Context.JobId=new('a',32);Assert.Throws<ArgumentException>(()=>ContainerAclPolicy.Request(request));
        request=Request();request.Context.Capability=Google.Protobuf.ByteString.CopyFromUtf8("not authority");Assert.Throws<ArgumentException>(()=>ContainerAclPolicy.Request(request));
    }
    [Fact] public void RunnerOnlyUsesFixedPrivateStdinAction()
    {
        var start=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(var action in new[]{"plan","apply","inspect","discard"}) {
            var value=(System.Diagnostics.ProcessStartInfo)start.Invoke(null,["container-acl:"+action,new string('a',32),null])!;
            Assert.Equal("/usr/bin/sudo",value.FileName);Assert.Contains("container-file-acl",value.ArgumentList);Assert.Contains(action,value.ArgumentList);Assert.False(value.UseShellExecute);
        }
        var bad=Assert.Throws<TargetInvocationException>(()=>start.Invoke(null,["container-acl:shell",new string('a',32),null]));Assert.IsType<ArgumentException>(bad.InnerException);
    }
    [Fact] public async Task DedicatedCapabilityRequires151AndKeepsOlderCapabilities()
    {
        var caps=await new AgentGrpcService(null!).GetCapabilities(new(),null!);Assert.Equal(1U,caps.ProtocolMajor);Assert.Equal(56U, caps.ProtocolMinor);
        Assert.Contains("docker.files.acl-intents",caps.Capabilities);Assert.Contains("docker.files.ownership-intents",caps.Capabilities);Assert.Contains("docker.files.recursive-ownership-intents",caps.Capabilities);
    }
    [Fact] public void SignatureNegativeProbeMustRemainSemanticallyValidBeforeBindingCheck()
    {
        var original=Request();var changed=original.Clone();changed.Plan.SourceChangedAtUnixNs++;
        ContainerAclPolicy.Request(changed);
        Assert.NotEqual(ContainerAclBinding.Hash(original),ContainerAclBinding.Hash(changed));
    }

}
