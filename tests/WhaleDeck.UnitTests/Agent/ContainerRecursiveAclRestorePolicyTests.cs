using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecursiveAclRestorePolicyTests
{
    internal static ContainerRecursiveAclRestorePlan Plan() => new()
    {
        SchemaVersion = 1, Command = new() { Location = new() { ContainerId = new('a', 64), InstanceId = "777:2026-10-27T01:02:03.123456789Z", Path = "work/中文目录" },
            CaptureId = "dd0a8865343e49bdb01a72680131bf39", ExpectedHistoryDigest = new('7', 64), ExpectedVersion = new('9', 64) },
        RestoreSource = new() { CaptureId = "dd0a8865343e49bdb01a72680131bf39", HistoryDigest = new('7', 64), CreatedAtUnixMs = 0,
            SourceInstanceId = "666:2026-10-26T01:02:03Z", ChangedAclType = "access", ManifestDigest = new('2', 64), EntryCount = 4, FileCount = 2, DirectoryCount = 2, ChangeCount = 1 },
        RestoreSourceFrame = new() { Device = 2049, Inode = 100, Mode = 0x8180, OwnerUid = 0, OwnerGid = 0, LinkCount = 1, SizeBytes = 12345, ModifiedAtUnixNs = -1, ChangedAtUnixNs = 0 },
        RestoreTargetsDigest = new('3', 64),
        TargetProof = new('A', 64), ParentVersion = new('B', 64), Fingerprint = new('C', 64),
        ParentIdentity = new() { Device = 101, Inode = 202 }, OwnerUid = 1000, OwnerGid = 1001,
        RootIdentity = new() { Device = 3, Inode = 4, Mode = 0x41E8, OwnerUid = 2100, OwnerGid = 3100, LinkCount = 2,
            SizeBytes = 4096, ModifiedAtUnixNs = 1722222222222222222 }, RootChangedAtUnixNs = 1733333333333333333,
        ManifestDigest = new('8', 64), EntryCount = 4, FileCount = 2, DirectoryCount = 2, ChangeCount = 2
    };
    internal static ContainerRecursiveAclRestoreRequest Request() => new()
    {
        Plan = Plan(), OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('D', 64),
        Context = new() { ActorSubject = "synthetic-admin", TraceId = "synthetic-trace", JobId = "aa0a8865343e49bdb01a72680131bf39", IdempotencyKey = "bb0a8865343e49bdb01a72680131bf39" }
    };
    internal static JsonObject Receipt(string state = "Completed")
    {
        var request = Request();
        return JsonSerializer.SerializeToNode(new { operationId = request.OperationId, containerId = request.Plan.Command.Location.ContainerId,
            instanceId = request.Plan.Command.Location.InstanceId, fingerprint = request.Plan.Fingerprint, targetProof = request.Plan.TargetProof,
            state, bindingCurrent = state == "Completed", replayed = true, completedCount = state == "Completed" ? 4 : state == "Partial" ? 1 : 0,
            changedCount = state == "Completed" ? 2 : state == "Partial" ? 1 : 0, abortedCount = 0,
            notStartedCount = state == "Completed" ? 0 : state == "Partial" ? 3 : state == "Aborted" ? 4 : 0, unknownCount = state == "Unknown" ? 4 : 0,
            historyMayExist = true, historyVerified = true, historyDigest = new string('7', 64), historyAvailable = true,
            outcomeDigest = new string('6', 64), aclType = "access" })!.AsObject();
    }
    [Fact]
    public void ExactRootPlanRoundTripBindsSmallSourceReferenceTargetsDigestAndBothNineFieldIdentities()
    {
        var expected = Plan();
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecursiveAclRestorePolicy.PlanInput(expected)));
        Assert.Equal(expected, ContainerRecursiveAclRestorePolicy.PlanOutput(value.RootElement, expected.Command));
        Assert.Equal(17, value.RootElement.EnumerateObject().Count());
        Assert.Equal(9, value.RootElement.GetProperty("rootIdentity").GetArrayLength());
        Assert.Equal(6, value.RootElement.GetProperty("command").EnumerateObject().Count());
    }
    [Theory]
    [InlineData("capture")][InlineData("history")][InlineData("source-capture")][InlineData("source-digest")][InlineData("source-instance")][InlineData("source-kind")]
    [InlineData("source-count")][InlineData("source-change")][InlineData("source-presence")][InlineData("frame-presence")][InlineData("frame-mode")]
    [InlineData("frame-owner")][InlineData("frame-link")][InlineData("frame-size")][InlineData("target-digest")][InlineData("current-root")][InlineData("current-count")][InlineData("default-changes")]
    public void InvalidSourceAndCurrentTreeBindingsFailBeforeProcessCreation(string field)
    {
        var plan = Plan();
        switch (field) {
            case "capture": plan.Command.CaptureId="bad"; break;
            case "history": plan.Command.ExpectedHistoryDigest="bad"; break;
            case "source-capture": plan.RestoreSource.CaptureId="ee0a8865343e49bdb01a72680131bf39"; break;
            case "source-digest": plan.RestoreSource.HistoryDigest=new('4',64); break;
            case "source-instance": plan.RestoreSource.SourceInstanceId="bad"; break;
            case "source-kind": plan.RestoreSource.ChangedAclType="other"; break;
            case "source-count": plan.RestoreSource.FileCount++; break;
            case "source-change": plan.RestoreSource.ChangeCount=5; break;
            case "source-presence": plan.RestoreSource.ClearCreatedAtUnixMs(); break;
            case "frame-presence": plan.RestoreSourceFrame.ClearOwnerUid(); break;
            case "frame-mode": plan.RestoreSourceFrame.Mode=0x81A4; break;
            case "frame-owner": plan.RestoreSourceFrame.OwnerUid=1; break;
            case "frame-link": plan.RestoreSourceFrame.LinkCount=2; break;
            case "frame-size": plan.RestoreSourceFrame.SizeBytes=16777217; break;
            case "target-digest": plan.RestoreTargetsDigest="bad"; break;
            case "current-root": plan.RootIdentity.Inode=0; break;
            case "current-count": plan.EntryCount=10001; break;
            case "default-changes": plan.RestoreSource.ChangedAclType="default"; plan.ChangeCount=3; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerRecursiveAclRestorePolicy.Plan(plan));
    }
    [Fact]
    public void HistoricalChangesAreNotUsedAsCurrentRestoreChangeCountAndZeroFrameFieldsRemainPresent()
    {
        var plan = Plan(); Assert.NotEqual(plan.ChangeCount, plan.RestoreSource.ChangeCount);
        ContainerRecursiveAclRestorePolicy.Plan(plan);
        Assert.True(plan.RestoreSourceFrame.HasOwnerUid); Assert.True(plan.RestoreSourceFrame.HasChangedAtUnixNs);
        var hash = WhaleDeck.Contracts.ContainerRecursiveAclRestoreBinding.Hash(plan);
        plan.RestoreSourceFrame.ClearChangedAtUnixNs();
        Assert.NotEqual(hash, WhaleDeck.Contracts.ContainerRecursiveAclRestoreBinding.Hash(plan));
        Assert.Throws<ArgumentException>(() => ContainerRecursiveAclRestorePolicy.Plan(plan));
    }
    [Fact]
    public void ExtraPerInodeAclArraysAndSubstitutedSourceAreRejectedInRootOutput()
    {
        var plan=Plan(); var node=JsonSerializer.SerializeToNode(ContainerRecursiveAclRestorePolicy.PlanInput(plan))!;
        node["entries"]=new JsonArray();
        using(var extra=JsonDocument.Parse(node.ToJsonString())) Assert.Throws<InvalidOperationException>(()=>ContainerRecursiveAclRestorePolicy.PlanOutput(extra.RootElement,plan.Command));
        node.AsObject().Remove("entries"); node["command"]!["captureId"]="ee0a8865343e49bdb01a72680131bf39";
        using var changed=JsonDocument.Parse(node.ToJsonString());Assert.Throws<InvalidOperationException>(()=>ContainerRecursiveAclRestorePolicy.PlanOutput(changed.RootElement,plan.Command));
    }
    [Fact]
    public async Task RestoreAdvertisementRequiresProtocol155AndIndependentCapability()
    {
        var caps=await new AgentGrpcService(null!).GetCapabilities(new(),null!);
        Assert.Equal(58U,caps.ProtocolMinor); Assert.Contains("docker.files.recursive-acl-restore-intents",caps.Capabilities);
    }
    [Theory]
    [InlineData("Completed")][InlineData("Partial")][InlineData("Aborted")][InlineData("Unknown")]
    public void DistinctDurableTerminalStatesRetainTheirCounts(string state)
    {
        using var value = JsonDocument.Parse(Receipt(state).ToJsonString());
        Assert.Equal(state, ContainerRecursiveAclRestorePolicy.Receipt(value.RootElement, Request()).State);
    }
    [Theory]
    [InlineData("completedCount", 3)][InlineData("changedCount", 0)][InlineData("unknownCount", 1)][InlineData("notStartedCount", 1)]
    [InlineData("abortedCount", 2147483647)]
    public void CompletedCannotHideUnfinishedEntriesOrMismatchedCounts(string field, int count)
    {
        var node = Receipt(); node[field] = count;
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveAclRestorePolicy.Receipt(value.RootElement, Request()));
    }
    [Theory]
    [InlineData("aclType")][InlineData("fingerprint")][InlineData("historyDigest")][InlineData("outcomeDigest")][InlineData("state")]
    public void CorruptReceiptBindingOrProofIsNeverAcknowledged(string field)
    {
        var node = Receipt(); node[field] = "invalid";
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveAclRestorePolicy.Receipt(value.RootElement, Request()));
    }
    [Fact]
    public void PartialCannotClaimCurrentWholeTreeOrZeroChanges()
    {
        var node = Receipt("Partial"); node["bindingCurrent"] = true;
        using var binding = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveAclRestorePolicy.Receipt(binding.RootElement, Request()));
        node["bindingCurrent"] = false; node["changedCount"] = 0;
        using var unchanged = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveAclRestorePolicy.Receipt(unchanged.RootElement, Request()));
    }
    [Fact]
    public void UnknownMustIdentifyAtLeastOneUnprovedEntry()
    {
        var node = Receipt(); node["state"] = "Unknown"; node["bindingCurrent"] = false;
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveAclRestorePolicy.Receipt(value.RootElement, Request()));
    }
    [Fact]
    public void HistoricalCompletionCanLoseCurrentHistoryWithoutBecomingAClaimOfCurrentSuccess()
    {
        var node = Receipt(); node["historyAvailable"] = false; node["bindingCurrent"] = false;
        using var value = JsonDocument.Parse(node.ToJsonString()); var result = ContainerRecursiveAclRestorePolicy.Receipt(value.RootElement, Request());
        Assert.Equal("Completed", result.State); Assert.True(result.HistoryVerified); Assert.False(result.HistoryAvailable); Assert.False(result.BindingCurrent);
    }
    [Theory]
    [InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void FixedArgvAndExactInvocationStopNeverCarryRequestOrHostPaths(string action)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string id = "aa0a8865343e49bdb01a72680131bf39", invocation = "bb0a8865343e49bdb01a72680131bf39";
        var start = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-recursive-acl-restore:" + action, id, null]));
        var stop = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-recursive-acl-restore:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-recursive-acl-restore", action, id }, start.ArgumentList);
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-recursive-acl-restore-stop", id, invocation }, stop.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal(2, start.Environment.Count);
    }
    [Theory]
    [InlineData("restore")][InlineData("read")][InlineData("plan;sh")][InlineData("../apply")]
    public void UnknownDriverActionsCannotFallBackToOtherCapabilities(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",BindingFlags.Static|BindingFlags.NonPublic)!;
        var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,["container-recursive-acl-restore:"+action,"aa0a8865343e49bdb01a72680131bf39",null]));
        Assert.IsType<ArgumentException>(error.InnerException);
    }
    [Fact]
    public void OriginalJobAndContextMustMatchAndEmbeddedAuthorityIsForbidden()
    {
        var request = Request(); ContainerRecursiveAclRestorePolicy.Request(request);
        request.Context.JobId = "cc0a8865343e49bdb01a72680131bf39"; Assert.Throws<ArgumentException>(() => ContainerRecursiveAclRestorePolicy.Request(request));
        request = Request(); request.Context.Capability = Google.Protobuf.ByteString.CopyFromUtf8("embedded"); Assert.Throws<ArgumentException>(() => ContainerRecursiveAclRestorePolicy.Request(request));
    }
    [Theory]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_TREE_LIMIT", StatusCode.ResourceExhausted)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_TREE_CHANGED", StatusCode.Aborted)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED", StatusCode.Aborted)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_ROOT_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_FILES_TARGET_MISSING", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_TIMEOUT", StatusCode.DeadlineExceeded)]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED", StatusCode.Cancelled)]
    [InlineData("private error text", StatusCode.Unavailable)]
    public void ErrorsAreFiniteTypedAndDoNotExposeUnrecognizedText(string code, StatusCode status)
    {
        var error = ContainerRecursiveAclRestorePolicy.Failure(code); Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == StatusCode.Unavailable ? "CONTAINER_RECURSIVE_ACL_RESTORE_UNAVAILABLE" : code, error.Status.Detail);
    }
}
