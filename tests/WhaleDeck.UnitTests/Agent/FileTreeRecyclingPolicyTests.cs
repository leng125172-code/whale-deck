using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeRecyclingPolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileTreeRecyclingCommand Command() => new() { Source = new() { RootId = "test.shared", Path = "代码/目录" }, ExpectedVersion = new('A', 64) };
    private static FileTreeRecyclingPlan Plan() => new() { Command = Command(), SourceParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993 },
        SourcePolicyHash = new('B', 64), ManifestSha256 = new('C', 64), EntryCount = 10000, TotalBytes = FileTreeCopyPolicy.MaximumBytes, Fingerprint = new('D', 64) };

    [Fact]
    public void SourceParentAndWholeManifestRoundTripWithoutLegacyFileLimitOrMemberList()
    {
        var plan = Plan(); var value = FileTreeRecyclingPolicy.PlanInput(plan);
        Assert.Equal(plan, FileTreeRecyclingPolicy.Plan(value, Command()));
        Assert.Equal(ulong.MaxValue, value.GetProperty("sourceParent")[0].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("sourceParent")[1].GetUInt64());
        Assert.Equal(7, value.EnumerateObject().Count()); Assert.False(value.TryGetProperty("entries", out _));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
        var changed = Command(); changed.Source.Path = "other";
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(value, changed));
        changed = Command(); changed.ExpectedVersion = new('F', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(value, changed));
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("../directory")][InlineData("a//b")][InlineData("a/../b")]
    public void InvalidSourceNeverReachesHelper(string path)
    {
        var command = Command(); command.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.CommandJson(command));
    }

    [Fact]
    public void PlansRejectMissingExtraDuplicateFieldsInvalidHashCountsAndParent()
    {
        var original = FileTreeRecyclingPolicy.PlanInput(Plan());
        foreach (var field in new[] { "sourcePolicyHash", "manifestSha256", "fingerprint" })
        {
            var node = JsonNode.Parse(original.GetRawText())!; node[field] = "bad";
            Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        }
        var extra = JsonNode.Parse(original.GetRawText())!; extra["shell"] = "rm";
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        extra.AsObject().Remove("shell"); extra["source"]!["ownerUid"] = 0;
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        extra = JsonNode.Parse(original.GetRawText())!; extra.AsObject().Remove("manifestSha256");
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        using var duplicate = JsonDocument.Parse(original.GetRawText().Replace("\"fingerprint\":", "\"fingerprint\":\"" + new string('D', 64) + "\",\"fingerprint\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Plan(duplicate.RootElement, Command()));
        var plan = Plan(); plan.SourceParent.Inode = 0; Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.PlanInput(plan));
        plan = Plan(); plan.EntryCount++; Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.PlanInput(plan));
        plan = Plan(); plan.EntryCount = 0; Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.PlanInput(plan));
        plan = Plan(); plan.TotalBytes++; Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.PlanInput(plan));
        plan = Plan(); plan.TotalBytes = -1; Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.PlanInput(plan));
    }

    private static JsonNode Receipt(string state, bool verified, bool changed = false) => JsonSerializer.SerializeToNode(new {
        operationId = Id, state, fingerprint = new string('D', 64), captureId = Id, manifestSha256 = new string('C', 64),
        entryCount = 10000, totalBytes = FileTreeCopyPolicy.MaximumBytes, captureVerified = verified, captureMayBeRetained = verified,
        archiveBytes = verified ? FileTreeUnpackingPolicy.MaximumArchiveBytes : 0, archiveSha256 = verified ? new string('E', 64) : "",
        sourceMayHaveChanged = changed, sourceRemoved = state == "Completed", originalsRetained = state == "Completed" })!;

    [Theory]
    [InlineData("Completed", true, true)][InlineData("Aborted", false, false)][InlineData("Aborted", true, false)]
    [InlineData("Unknown", true, true)][InlineData("Unknown", true, false)][InlineData("Unknown", false, false)]
    public void TerminalReceiptsKeepCaptureProofSeparateFromSourceEffects(string state, bool verified, bool changed)
    {
        var node = Receipt(state, verified, changed);
        var result = FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id, Plan());
        Assert.Equal(state, result.State); Assert.Equal(verified, result.CaptureVerified); Assert.Equal(changed, result.SourceMayHaveChanged);
        var plan = Plan(); plan.ManifestSha256 = new('F', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id, plan));
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), new('a', 32)));
    }

    [Theory]
    [InlineData("captureVerified")][InlineData("captureMayBeRetained")][InlineData("sourceMayHaveChanged")]
    [InlineData("sourceRemoved")][InlineData("originalsRetained")]
    public void CompletedCannotClaimSuccessWithoutEveryRequiredProof(string field)
    {
        var node = Receipt("Completed", true, true); node[field] = false;
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
    }

    [Fact]
    public void InvalidStateCaptureIdentityAndUnavailableOrOversizedProofFailClosed()
    {
        foreach (var state in new[] { "Running", "completed", "Unknown\n" })
            Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(Receipt(state, false)), Id));
        var node = Receipt("Aborted", false); node["archiveBytes"] = 1;
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node = Receipt("Unknown", true, true); node["archiveBytes"] = FileTreeUnpackingPolicy.MaximumArchiveBytes + 1;
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node = Receipt("Unknown", false); node["sourceMayHaveChanged"] = true;
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node = Receipt("Aborted", true, true);
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node = Receipt("Unknown", false); node["captureId"] = new string('a', 32);
        Assert.Throws<InvalidOperationException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node = Receipt("Unknown", true); node["archiveSha256"] = "";
        Assert.Throws<ArgumentException>(() => FileTreeRecyclingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
    }

    [Fact]
    public async Task InvalidCallsDoNotStartPrivilegedProcessAndCapabilityIsIndependent()
    {
        var service = new FileTreeRecyclingGrpcService(new FileTransferRunner());
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", missing.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Id, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
        var apply = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", apply.Status.Detail);
        var discard = await Assert.ThrowsAsync<RpcException>(() => service.Discard(new() { OperationId = Id, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", discard.Status.Detail);
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(37U, capabilities.ProtocolMinor); Assert.Contains("files.tree-trash-intents", capabilities.Capabilities);
        Assert.Equal("whaledeck.agent.v1.FileTreeRecyclingService", FileTreeRecyclingService.Descriptor.FullName);
    }
}
