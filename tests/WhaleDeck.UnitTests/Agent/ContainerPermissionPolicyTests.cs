using System.Reflection;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerPermissionPolicyTests
{
    internal static ContainerPermissionPlan Plan() => new()
    {
        SchemaVersion = 1, Command = new() { Location = new() { ContainerId = new('a', 64), InstanceId = "777:2026-10-27T01:02:03.123456789Z", Path = "work/中文目录" }, Mode = "0750", ExpectedVersion = new('9', 64) },
        TargetProof = new('A', 64), ParentVersion = new('B', 64), Fingerprint = new('C', 64),
        ParentIdentity = new() { Device = 101, Inode = 202 }, OwnerUid = 1000, OwnerGid = 1001, Kind = "File", AttributesDigest = new('8', 64),
        SourceIdentity = new() { Device = 3, Inode = 4, Mode = 0x81A4, OwnerUid = 1000, OwnerGid = 1001, LinkCount = 1,
            SizeBytes = 10737418247, ModifiedAtUnixNs = 1722222222222222222 }, SourceChangedAtUnixNs = 1733333333333333333, NoChange = false
    };
    internal static ContainerPermissionRequest Request() => new()
    {
        Plan = Plan(), OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('D', 64),
        Context = new() { ActorSubject = "synthetic-admin", TraceId = "synthetic-trace", JobId = "aa0a8865343e49bdb01a72680131bf39", IdempotencyKey = "bb0a8865343e49bdb01a72680131bf39" }
    };
    [Fact]
    public void ExactRootPlanRoundTripBindsUnicodeParentAndKernelOwner()
    {
        var expected = Plan();
        using var value = JsonDocument.Parse(JsonSerializer.Serialize(ContainerPermissionPolicy.PlanInput(expected)));
        Assert.Equal(expected, ContainerPermissionPolicy.PlanOutput(value.RootElement, expected.Command));
        Assert.Equal(12, value.RootElement.EnumerateObject().Count());
        Assert.Equal<ulong>([101UL, 202UL], value.RootElement.GetProperty("parentIdentity").EnumerateArray().Select(n => n.GetUInt64()));
        Assert.DoesNotContain("rootId", value.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("1755")][InlineData("4755")][InlineData("2755")][InlineData("755")][InlineData("0999")]
    public void SpecialOrMalformedModesNeverReachHelper(string mode)
    { var command = Plan().Command; command.Mode = mode; Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Command(command)); }
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
        Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Plan(plan));
    }
    [Fact]
    public void PlanResponseCannotSubstituteTargetOrSmuggleFields()
    {
        var plan = Plan(); var input = JsonSerializer.SerializeToNode(ContainerPermissionPolicy.PlanInput(plan))!;
        input["unexpected"] = "sensitive path";
        using var extra = JsonDocument.Parse(input.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerPermissionPolicy.PlanOutput(extra.RootElement, plan.Command));
        input.AsObject().Remove("unexpected"); input["command"]!["path"] = "work/another";
        using var changed = JsonDocument.Parse(input.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerPermissionPolicy.PlanOutput(changed.RootElement, plan.Command));
    }
    [Fact]
    public void OriginalActorTraceAndJobAreExplicitAndEmbeddedCapabilityIsForbidden()
    {
        var request = Request(); ContainerPermissionPolicy.Request(request);
        request.Context.JobId = "cc0a8865343e49bdb01a72680131bf39";
        Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Request(request));
        request = Request(); request.Context.Capability = Google.Protobuf.ByteString.CopyFromUtf8("never embedded");
        Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Request(request));
        request = Request(); Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Context(request.Context, planning: true));
    }
    [Theory]
    [InlineData("Completed", true)][InlineData("Aborted", false)][InlineData("Unknown", false)]
    public void OnlyBoundTerminalReceiptsCanBeAcknowledged(string state, bool binding)
    {
        var request = Request();
        var node = JsonSerializer.SerializeToNode(new { operationId = request.OperationId, containerId = request.Plan.Command.Location.ContainerId,
            instanceId = request.Plan.Command.Location.InstanceId, fingerprint = request.Plan.Fingerprint, targetProof = request.Plan.TargetProof,
            kind = "File", modeConfirmed = state == "Unknown" ? (bool?)null : state == "Completed", noChange = false,
            historyMayExist = true, historyVerified = true, historyDigest = new string('7', 64), state, version = state == "Completed" ? new string('E', 64) : "", bindingCurrent = binding, replayed = true })!;
        using var good = JsonDocument.Parse(node.ToJsonString()); Assert.Equal(state, ContainerPermissionPolicy.Receipt(good.RootElement, request).State);
        node["fingerprint"] = new string('F', 64);
        using var wrong = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => ContainerPermissionPolicy.Receipt(wrong.RootElement, request));
    }
    [Theory]
    [InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void FixedProcessArgumentsCarryNoPathsAndStopBindsExactInvocation(string action)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string id = "aa0a8865343e49bdb01a72680131bf39", invocation = "bb0a8865343e49bdb01a72680131bf39";
        var start = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-permission:" + action, id, null]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-permissions", action, id }, start.ArgumentList);
        var stop = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-permission:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-permissions-stop", id, invocation }, stop.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal(2, start.Environment.Count);
    }
    [Theory]
    [InlineData("CONTAINER_PERMISSION_NOT_FOUND", StatusCode.NotFound)]
    [InlineData("CONTAINER_FILES_TARGET_MISSING", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_PERMISSION_PRIVATE_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_PERMISSION_TARGET_EXISTS", StatusCode.Aborted)]
    [InlineData("CONTAINER_UPLOAD_PUBLISH_READ_ONLY", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_PERMISSION_arbitrary-sensitive-error", StatusCode.Unavailable)]
    public void ErrorMappingNeverAliasesTargetLossOrLeaksUnknownText(string code, StatusCode status)
    {
        var error = ContainerPermissionPolicy.Failure(code); Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == StatusCode.Unavailable ? "CONTAINER_PERMISSION_UNAVAILABLE" : code, error.Status.Detail);
    }
    [Theory]
    [InlineData("missing")][InlineData("inode")][InlineData("uid")][InlineData("gid")][InlineData("negativeSize")]
    [InlineData("hardlink")][InlineData("symlink")][InlineData("setuid")][InlineData("setgid")][InlineData("sticky")]
    [InlineData("attributes")][InlineData("noChange")][InlineData("kind")][InlineData("commandVersion")]
    public void SourceMetadataAndOrdinaryModeRulesFailBeforeRootInvocation(string change)
    {
        var plan = Plan();
        switch (change)
        {
            case "missing": plan.SourceIdentity = null; break;
            case "inode": plan.SourceIdentity.Inode = 0; break;
            case "uid": plan.SourceIdentity.OwnerUid = uint.MaxValue; break;
            case "gid": plan.SourceIdentity.OwnerGid = uint.MaxValue; break;
            case "negativeSize": plan.SourceIdentity.SizeBytes = -1; break;
            case "hardlink": plan.SourceIdentity.LinkCount = 2; break;
            case "symlink": plan.SourceIdentity.Mode = 0xA1A4; break;
            case "setuid": plan.SourceIdentity.Mode |= 0x800; break;
            case "setgid": plan.SourceIdentity.Mode |= 0x400; break;
            case "sticky": plan.SourceIdentity.Mode |= 0x200; break;
            case "attributes": plan.AttributesDigest = "bad"; break;
            case "noChange": plan.NoChange = true; break;
            case "kind": plan.Kind = "Directory"; break;
            case "commandVersion": plan.Command.ExpectedVersion = "bad"; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerPermissionPolicy.Plan(plan));
    }
    [Fact]
    public void DirectoryIdentityAndTenGibFileDoNotInheritUploadSizeLimit()
    {
        var plan = Plan(); ContainerPermissionPolicy.Plan(plan); Assert.True(plan.SourceIdentity.SizeBytes > 10L * 1024 * 1024 * 1024);
        plan.Kind = "Directory"; plan.SourceIdentity.Mode = 0x41E8; plan.SourceIdentity.LinkCount = 5; plan.NoChange = true;
        ContainerPermissionPolicy.Plan(plan);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ContainerPermissionPolicy.PlanInput(plan)));
        Assert.Equal(plan, ContainerPermissionPolicy.PlanOutput(json.RootElement, plan.Command));
    }
    [Fact]
    public async Task PublishedAgentAdvertisesOnlyTheNewSpecificAdditiveCapability()
    {
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(1U, capabilities.ProtocolMajor); Assert.Equal(48U, capabilities.ProtocolMinor);
        Assert.Contains("docker.files.permission-intents", capabilities.Capabilities);
        Assert.Contains("docker.files.move-intents", capabilities.Capabilities);
    }
}
