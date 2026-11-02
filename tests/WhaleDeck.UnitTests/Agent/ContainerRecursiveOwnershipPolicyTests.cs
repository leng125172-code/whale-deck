using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecursiveOwnershipPolicyTests
{
    internal static ContainerRecursiveOwnershipPlan Plan() => new()
    {
        SchemaVersion = 1, Command = new() { Location = new() { ContainerId = new('a', 64), InstanceId = "777:2026-10-27T01:02:03.123456789Z", Path = "work/中文目录" }, Uid = 2200, Gid = 3200, ExpectedVersion = new('9', 64) },
        TargetProof = new('A', 64), ParentVersion = new('B', 64), Fingerprint = new('C', 64),
        ParentIdentity = new() { Device = 101, Inode = 202 }, OwnerUid = 1000, OwnerGid = 1001,
        RootIdentity = new() { Device = 3, Inode = 4, Mode = 0x41E8, OwnerUid = 2100, OwnerGid = 3100, LinkCount = 2,
            SizeBytes = 4096, ModifiedAtUnixNs = 1722222222222222222 }, RootChangedAtUnixNs = 1733333333333333333,
        ManifestDigest = new('8', 64), EntryCount = 4, FileCount = 2, DirectoryCount = 2, ChangeCount = 2
    };
    internal static ContainerRecursiveOwnershipRequest Request() => new()
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
            outcomeDigest = new string('6', 64) })!.AsObject();
    }
    [Fact]
    public void ExactRootPlanRoundTripBindsRequestedOwnersManifestCountsAndNineFieldIdentity()
    {
        var expected = Plan();
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecursiveOwnershipPolicy.PlanInput(expected)));
        Assert.Equal(expected, ContainerRecursiveOwnershipPolicy.PlanOutput(value.RootElement, expected.Command));
        Assert.Equal(14, value.RootElement.EnumerateObject().Count());
        Assert.Equal(9, value.RootElement.GetProperty("rootIdentity").GetArrayLength());
        Assert.DoesNotContain("entries", value.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void MissingRequestedOwnerIsNotAnExplicitRootRequest(bool uid)
    {
        var command = Plan().Command; command.Uid = 0; command.Gid = 0;
        ContainerRecursiveOwnershipPolicy.Command(command);
        var original = WhaleDeck.Contracts.ContainerRecursiveOwnershipBinding.Hash(command);
        if (uid) command.ClearUid(); else command.ClearGid();
        Assert.NotEqual(original, WhaleDeck.Contracts.ContainerRecursiveOwnershipBinding.Hash(command));
        Assert.Throws<ArgumentException>(() => ContainerRecursiveOwnershipPolicy.Command(command));
    }
    [Fact]
    public async Task AdvertisedOwnershipCapabilityRequiresProtocol150()
    {
        var caps = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(1U, caps.ProtocolMajor); Assert.Equal(51U, caps.ProtocolMinor);
        Assert.Contains("docker.files.recursive-ownership-intents", caps.Capabilities);
    }
    [Theory]
    [InlineData("entries")][InlineData("files")][InlineData("directories")][InlineData("changes")][InlineData("manifest")]
    [InlineData("rootFile")][InlineData("specialMode")][InlineData("inode")][InlineData("requestedUid")][InlineData("requestedGid")]
    public void ImpossiblePlansFailBeforeProcessCreation(string field)
    {
        var plan = Plan();
        switch (field)
        {
            case "entries": plan.EntryCount = 10001; break;
            case "files": plan.FileCount = 4; break;
            case "directories": plan.DirectoryCount = 0; break;
            case "changes": plan.ChangeCount = 5; break;
            case "manifest": plan.ManifestDigest = "bad"; break;
            case "rootFile": plan.RootIdentity.Mode = 0x81A4; break;
            case "specialMode": plan.RootIdentity.Mode |= 0x800; break;
            case "inode": plan.RootIdentity.Inode = 0; break;
            case "requestedUid": plan.Command.Uid = uint.MaxValue; break;
            case "requestedGid": plan.Command.Gid = uint.MaxValue; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerRecursiveOwnershipPolicy.Plan(plan));
    }
    [Fact]
    public void PlanOutputRejectsExtraFieldsAndTargetSubstitution()
    {
        var plan = Plan(); var node = JsonSerializer.SerializeToNode(ContainerRecursiveOwnershipPolicy.PlanInput(plan))!;
        node["entries"] = new JsonArray();
        using var extra = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.PlanOutput(extra.RootElement, plan.Command));
        node.AsObject().Remove("entries"); node["command"]!["gid"] = 3201;
        using var changed = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.PlanOutput(changed.RootElement, plan.Command));
    }
    [Theory]
    [InlineData("Completed")][InlineData("Partial")][InlineData("Aborted")][InlineData("Unknown")]
    public void DistinctDurableTerminalStatesRetainTheirCounts(string state)
    {
        using var value = JsonDocument.Parse(Receipt(state).ToJsonString());
        Assert.Equal(state, ContainerRecursiveOwnershipPolicy.Receipt(value.RootElement, Request()).State);
    }
    [Theory]
    [InlineData("completedCount", 3)][InlineData("changedCount", 0)][InlineData("unknownCount", 1)][InlineData("notStartedCount", 1)]
    [InlineData("abortedCount", 2147483647)]
    public void CompletedCannotHideUnfinishedEntriesOrMismatchedCounts(string field, int count)
    {
        var node = Receipt(); node[field] = count;
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.Receipt(value.RootElement, Request()));
    }
    [Theory]
    [InlineData("fingerprint")][InlineData("historyDigest")][InlineData("outcomeDigest")][InlineData("state")]
    public void CorruptReceiptBindingOrProofIsNeverAcknowledged(string field)
    {
        var node = Receipt(); node[field] = "invalid";
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.Receipt(value.RootElement, Request()));
    }
    [Fact]
    public void PartialCannotClaimCurrentWholeTreeOrZeroChanges()
    {
        var node = Receipt("Partial"); node["bindingCurrent"] = true;
        using var binding = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.Receipt(binding.RootElement, Request()));
        node["bindingCurrent"] = false; node["changedCount"] = 0;
        using var unchanged = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.Receipt(unchanged.RootElement, Request()));
    }
    [Fact]
    public void UnknownMustIdentifyAtLeastOneUnprovedEntry()
    {
        var node = Receipt(); node["state"] = "Unknown"; node["bindingCurrent"] = false;
        using var value = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerRecursiveOwnershipPolicy.Receipt(value.RootElement, Request()));
    }
    [Fact]
    public void HistoricalCompletionCanLoseCurrentHistoryWithoutBecomingAClaimOfCurrentSuccess()
    {
        var node = Receipt(); node["historyAvailable"] = false; node["bindingCurrent"] = false;
        using var value = JsonDocument.Parse(node.ToJsonString()); var result = ContainerRecursiveOwnershipPolicy.Receipt(value.RootElement, Request());
        Assert.Equal("Completed", result.State); Assert.True(result.HistoryVerified); Assert.False(result.HistoryAvailable); Assert.False(result.BindingCurrent);
    }
    [Theory]
    [InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void FixedArgvAndExactInvocationStopNeverCarryRequestOrHostPaths(string action)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string id = "aa0a8865343e49bdb01a72680131bf39", invocation = "bb0a8865343e49bdb01a72680131bf39";
        var start = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-recursive-ownership:" + action, id, null]));
        var stop = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-recursive-ownership:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-recursive-ownership", action, id }, start.ArgumentList);
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-recursive-ownership-stop", id, invocation }, stop.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal(2, start.Environment.Count);
    }
    [Fact]
    public void OriginalJobAndContextMustMatchAndEmbeddedAuthorityIsForbidden()
    {
        var request = Request(); ContainerRecursiveOwnershipPolicy.Request(request);
        request.Context.JobId = "cc0a8865343e49bdb01a72680131bf39"; Assert.Throws<ArgumentException>(() => ContainerRecursiveOwnershipPolicy.Request(request));
        request = Request(); request.Context.Capability = Google.Protobuf.ByteString.CopyFromUtf8("embedded"); Assert.Throws<ArgumentException>(() => ContainerRecursiveOwnershipPolicy.Request(request));
    }
    [Theory]
    [InlineData("CONTAINER_RECURSIVE_OWNERSHIP_TREE_LIMIT", StatusCode.ResourceExhausted)]
    [InlineData("CONTAINER_RECURSIVE_OWNERSHIP_TREE_CHANGED", StatusCode.Aborted)]
    [InlineData("CONTAINER_RECURSIVE_OWNERSHIP_ROOT_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_FILES_TARGET_MISSING", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECURSIVE_OWNERSHIP_TIMEOUT", StatusCode.DeadlineExceeded)]
    [InlineData("CONTAINER_RECURSIVE_OWNERSHIP_CANCELED", StatusCode.Cancelled)]
    [InlineData("private error text", StatusCode.Unavailable)]
    public void ErrorsAreFiniteTypedAndDoNotExposeUnrecognizedText(string code, StatusCode status)
    {
        var error = ContainerRecursiveOwnershipPolicy.Failure(code); Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == StatusCode.Unavailable ? "CONTAINER_RECURSIVE_OWNERSHIP_UNAVAILABLE" : code, error.Status.Detail);
    }
}
