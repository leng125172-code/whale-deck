using System.Reflection;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerDirectoryPolicyTests
{
    internal static ContainerDirectoryPlan Plan() => new()
    {
        SchemaVersion = 1, Command = new() { Location = new() { ContainerId = new('a', 64), InstanceId = "777:2026-10-27T01:02:03.123456789Z", Path = "work/中文目录" }, Mode = "0750" },
        TargetProof = new('A', 64), ParentVersion = new('B', 64), Fingerprint = new('C', 64),
        ParentIdentity = new() { Device = 101, Inode = 202 }, OwnerUid = 1000, OwnerGid = 1001
    };
    internal static ContainerDirectoryRequest Request() => new()
    {
        Plan = Plan(), OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('D', 64),
        Context = new() { ActorSubject = "synthetic-admin", TraceId = "synthetic-trace", JobId = "aa0a8865343e49bdb01a72680131bf39", IdempotencyKey = "bb0a8865343e49bdb01a72680131bf39" }
    };
    [Fact]
    public void ExactRootPlanRoundTripBindsUnicodeParentAndKernelOwner()
    {
        var expected = Plan();
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(ContainerDirectoryPolicy.PlanInput(expected)));
        Assert.Equal(expected, ContainerDirectoryPolicy.PlanOutput(value.RootElement, expected.Command));
        Assert.Equal(8, value.RootElement.EnumerateObject().Count());
        Assert.Equal<ulong>([101UL, 202UL], value.RootElement.GetProperty("parentIdentity").EnumerateArray().Select(n => n.GetUInt64()));
        Assert.DoesNotContain("rootId", value.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("1755")][InlineData("4755")][InlineData("2755")][InlineData("755")][InlineData("0999")]
    public void SpecialOrMalformedModesNeverReachHelper(string mode)
    { var command = Plan().Command; command.Mode = mode; Assert.Throws<ArgumentException>(() => ContainerDirectoryPolicy.Command(command)); }
    [Theory]
    [InlineData("schema")][InlineData("uid")][InlineData("gid")][InlineData("parent")][InlineData("proof")]
    public void MalformedPlansFailBeforeAnyProcess(string change)
    {
        var plan = Plan();
        switch (change)
        {
            case "schema": plan.SchemaVersion = 2; break;
            case "uid": plan.OwnerUid = uint.MaxValue; break;
            case "gid": plan.OwnerGid = uint.MaxValue; break;
            case "parent": plan.ParentIdentity.Inode = 0; break;
            case "proof": plan.TargetProof = "bad"; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerDirectoryPolicy.Plan(plan));
    }
    [Fact]
    public void PlanResponseCannotSubstituteTargetOrSmuggleFields()
    {
        var plan = Plan(); var input = JsonSerializer.SerializeToNode(ContainerDirectoryPolicy.PlanInput(plan))!;
        input["unexpected"] = "sensitive path";
        using var extra = JsonDocument.Parse(input.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerDirectoryPolicy.PlanOutput(extra.RootElement, plan.Command));
        input.AsObject().Remove("unexpected"); input["command"]!["path"] = "work/another";
        using var changed = JsonDocument.Parse(input.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerDirectoryPolicy.PlanOutput(changed.RootElement, plan.Command));
    }
    [Fact]
    public void OriginalActorTraceAndJobAreExplicitAndEmbeddedCapabilityIsForbidden()
    {
        var request = Request(); ContainerDirectoryPolicy.Request(request);
        request.Context.JobId = "cc0a8865343e49bdb01a72680131bf39";
        Assert.Throws<ArgumentException>(() => ContainerDirectoryPolicy.Request(request));
        request = Request(); request.Context.Capability = Google.Protobuf.ByteString.CopyFromUtf8("never embedded");
        Assert.Throws<ArgumentException>(() => ContainerDirectoryPolicy.Request(request));
        request = Request(); Assert.Throws<ArgumentException>(() => ContainerDirectoryPolicy.Context(request.Context, planning: true));
    }
    [Theory]
    [InlineData("Completed", true)][InlineData("Aborted", false)][InlineData("Unknown", false)]
    public void OnlyBoundTerminalReceiptsCanBeAcknowledged(string state, bool binding)
    {
        var request = Request();
        var node = JsonSerializer.SerializeToNode(new { operationId = request.OperationId, containerId = request.Plan.Command.Location.ContainerId,
            instanceId = request.Plan.Command.Location.InstanceId, fingerprint = request.Plan.Fingerprint, targetProof = request.Plan.TargetProof,
            state, version = state == "Completed" ? new string('E', 64) : "", bindingCurrent = binding, replayed = true })!;
        using var good = JsonDocument.Parse(node.ToJsonString()); Assert.Equal(state, ContainerDirectoryPolicy.Receipt(good.RootElement, request).State);
        node["fingerprint"] = new string('F', 64);
        using var wrong = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerDirectoryPolicy.Receipt(wrong.RootElement, request));
    }
    [Theory]
    [InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void FixedProcessArgumentsCarryNoPathsAndStopBindsExactInvocation(string action)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string id = "aa0a8865343e49bdb01a72680131bf39", invocation = "bb0a8865343e49bdb01a72680131bf39";
        var start = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-directory:" + action, id, null]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-directories", action, id }, start.ArgumentList);
        var stop = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-directory:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-directories-stop", id, invocation }, stop.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal(2, start.Environment.Count);
    }
    [Theory]
    [InlineData("CONTAINER_DIRECTORY_NOT_FOUND", StatusCode.NotFound)]
    [InlineData("CONTAINER_FILES_TARGET_MISSING", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_DIRECTORY_PRIVATE_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_DIRECTORY_TARGET_EXISTS", StatusCode.Aborted)]
    [InlineData("CONTAINER_UPLOAD_PUBLISH_READ_ONLY", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_DIRECTORY_arbitrary-sensitive-error", StatusCode.Unavailable)]
    public void ErrorMappingNeverAliasesTargetLossOrLeaksUnknownText(string code, StatusCode status)
    {
        var error = ContainerDirectoryPolicy.Failure(code); Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == StatusCode.Unavailable ? "CONTAINER_DIRECTORY_UNAVAILABLE" : code, error.Status.Detail);
    }
}
