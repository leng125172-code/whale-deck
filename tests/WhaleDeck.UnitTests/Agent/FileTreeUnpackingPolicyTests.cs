using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeUnpackingPolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileTreeUnpackingCommand Command() => new() { Tree = new() { Source = new() { RootId = "test.shared", Path = "输入.archive" },
        ExpectedVersion = new('A', 64), Target = new() { RootId = "test.shared", Path = "代码/新目录" } }, Format = "tar.gz" };
    private static FileTreeUnpackingPlan Plan() => new() { Command = Command(), ArchiveSha256 = new('F', 64), ArchiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes,
        ContentSha256 = new('B', 64), EntryCount = 10000, TotalBytes = FileTreeCopyPolicy.MaximumBytes,
        TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993 }, SourcePolicyHash = new('C', 64), TargetPolicyHash = new('D', 64), Fingerprint = new('E', 64) };

    [Theory]
    [InlineData("tar")][InlineData("tar.gz")]
    public void InputProofAndUInt64ParentsRemainBoundWithoutLargeFramesOrSuffixAssumptions(string format)
    {
        var plan = Plan(); plan.Command.Format = format;
        var root = FileTreeUnpackingPolicy.PlanInput(plan);
        Assert.Equal(plan, FileTreeUnpackingPolicy.Plan(root, plan.Command)); Assert.Equal(10, root.EnumerateObject().Count());
        Assert.Equal(ulong.MaxValue, root.GetProperty("targetParent")[0].GetUInt64());
        Assert.Equal(9007199254740993UL, root.GetProperty("targetParent")[1].GetUInt64());
        Assert.False(root.TryGetProperty("entries", out _)); Assert.False(root.TryGetProperty("manifestSha256", out _));
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Plan(root, plan.Command.Tree));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
    }
    [Theory]
    [InlineData("")][InlineData("zip")][InlineData("TAR")][InlineData("tar.gz\n")]
    public void UnapprovedFormatDoesNotReachHelper(string format)
    {
        var command = Command(); command.Format = format;
        Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.CommandJson(command));
    }
    [Theory]
    [InlineData("/etc/output")][InlineData("../output")][InlineData("a//b")][InlineData("输入.archive/child")][InlineData("输入.archive")]
    public void InvalidOrOverlappingTargetsAreRejectedBeforeHelper(string path)
    {
        var command = Command(); command.Tree.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.CommandJson(command));
    }
    [Fact]
    public void PlansRejectExtraOrMissingFieldsInputProofChangesAndLimits()
    {
        var plan = Plan(); var root = FileTreeUnpackingPolicy.PlanInput(plan);
        foreach (var field in new[] { "archiveSha256", "contentSha256", "sourcePolicyHash", "targetPolicyHash", "fingerprint" })
        {
            var node = JsonNode.Parse(root.GetRawText())!; node[field] = "bad";
            Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.Plan(JsonSerializer.SerializeToElement(node), plan.Command));
        }
        foreach (var size in new[] { 0L, -1L, FileTreeUnpackingPolicy.MaximumArchiveBytes + 1 })
        {
            plan.ArchiveBytes = size; Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.PlanInput(plan));
        }
        plan = Plan(); plan.EntryCount++; Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.PlanInput(plan));
        plan = Plan(); plan.TotalBytes++; Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.PlanInput(plan));
        var extra = JsonNode.Parse(root.GetRawText())!; extra["shell"] = "tar";
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        extra.AsObject().Remove("shell"); extra["command"]!["ownerUid"] = 0;
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        extra["command"]!.AsObject().Remove("ownerUid"); extra.AsObject().Remove("archiveSha256");
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Plan(JsonSerializer.SerializeToElement(extra), Command()));
        var other = Command(); other.Tree.Target.Path = "other";
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Plan(root, other));
        other = Command(); other.Format = "tar";
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Plan(root, other));
    }
    private static JsonElement Receipt(string state, bool partial = false) => JsonSerializer.SerializeToElement(new {
        operationId = Id, state, fingerprint = new string('E', 64), format = "tar.gz", entryCount = 10000,
        totalBytes = FileTreeCopyPolicy.MaximumBytes, archiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes,
        archiveSha256 = new string('F', 64), version = state == "Completed" ? new string('1', 64) : "", partialRetained = partial });
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void AllStatesBindImmutableInputProofRatherThanClaimingNewArchiveOutput(string state, bool partial)
    {
        var receipt = Receipt(state, partial);
        Assert.Equal(state, FileTreeUnpackingPolicy.Receipt(receipt, Id, Plan()).State);
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(receipt, new('a', 32)));
        var plan = Plan(); plan.ArchiveBytes--;
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(receipt, Id, plan));
        plan = Plan(); plan.ArchiveSha256 = new('0', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(receipt, Id, plan));
    }
    [Fact]
    public void IncompleteCannotClaimPublishedVersionCompletedCannotBePartialAndInputProofIsMandatory()
    {
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(Receipt("Completed", true), Id));
        var node = JsonNode.Parse(Receipt("Unknown").GetRawText())!; node["version"] = new string('1', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node["version"] = ""; node["archiveBytes"] = 0;
        Assert.Throws<InvalidOperationException>(() => FileTreeUnpackingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
        node["archiveBytes"] = 1; node["archiveSha256"] = "";
        Assert.Throws<ArgumentException>(() => FileTreeUnpackingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Id));
    }
    [Fact]
    public async Task InvalidCallsAreRejectedWithoutStartingRootHelper()
    {
        var service = new FileTreeUnpackingGrpcService(new FileTransferRunner());
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal(StatusCode.InvalidArgument, missing.StatusCode); Assert.Equal("FILES_ACTOR_REQUIRED", missing.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Id, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
        var apply = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", apply.Status.Detail);
        var discard = await Assert.ThrowsAsync<RpcException>(() => service.Discard(new() { OperationId = Id, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", discard.Status.Detail);
    }
}
