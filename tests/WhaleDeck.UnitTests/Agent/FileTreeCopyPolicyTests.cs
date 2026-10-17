using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeCopyPolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileTreeCopyCommand Command() => new() { Source = new() { RootId = "test.shared", Path = "代码/原目录" },
        ExpectedVersion = new('A', 64), Target = new() { RootId = "test.shared", Path = "代码/目标目录" } };
    private static FileTreeCopyPlan Plan() => new() { Command = Command(), ManifestSha256 = new('B', 64), EntryCount = 10000,
        TotalBytes = FileTreeCopyPolicy.MaximumBytes, TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993 },
        SourcePolicyHash = new('C', 64), TargetPolicyHash = new('D', 64), Fingerprint = new('E', 64) };

    [Fact]
    public void PlanRoundTripKeepsUInt64AndLargeDirectoryBudgetWithoutARegularFileIdentity()
    {
        var plan = Plan(); var input = FileTreeCopyPolicy.PlanInput(plan); var result = FileTreeCopyPolicy.Plan(input, plan.Command);
        Assert.Equal(plan, result); Assert.Equal(ulong.MaxValue, result.TargetParent.Device); Assert.Equal(9007199254740993UL, result.TargetParent.Inode);
        Assert.True(result.TotalBytes > FileTransferPolicy.MaximumBytes);
        Assert.Equal(8, input.EnumerateObject().Count()); Assert.False(input.TryGetProperty("entries", out _)); Assert.False(input.TryGetProperty("sourceIdentity", out _));
        Assert.Equal("代码/原目录", input.GetProperty("command").GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(new('A', 64), input.GetProperty("command").GetProperty("source").GetProperty("expectedVersion").GetString());
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a//b")][InlineData("a/../b")][InlineData("a\\b")]
    public void InvalidLocationsNeverReachTheHelper(string path)
    {
        var command = Command(); command.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.CommandJson(command));
    }

    [Theory]
    [InlineData("代码/原目录")][InlineData("代码/原目录/子目录")][InlineData("代码")]
    public void SameLogicalRootOverlapFailsBeforeHelperInvocation(string path)
    {
        var command = Command(); command.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.CommandJson(command));
    }

    [Fact]
    public void PlansRejectWrongCommandLimitsAndUnexpectedFields()
    {
        var plan = Plan(); var other = plan.Command.Clone(); other.Target.Path = "another";
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Plan(FileTreeCopyPolicy.PlanInput(plan), other));
        plan.EntryCount = 0; Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.PlanInput(plan));
        plan = Plan(); plan.EntryCount++; Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.PlanInput(plan));
        plan = Plan(); plan.TotalBytes++; Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.PlanInput(plan));
        plan = Plan(); plan.TargetParent.Inode = 0; Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.PlanInput(plan));
        plan = Plan(); plan.SourcePolicyHash = "bad"; Assert.Throws<ArgumentException>(() => FileTreeCopyPolicy.PlanInput(plan));
        var node = JsonNode.Parse(FileTreeCopyPolicy.PlanInput(Plan()).GetRawText())!; node["unexpected"] = true;
        using var extra = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Plan(extra.RootElement, Command()));
        node.AsObject().Remove("unexpected"); node["command"]!["source"]!["extra"] = "not-permitted";
        using var inner = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Plan(inner.RootElement, Command()));
    }

    private static JsonElement Receipt(FileTreeCopyPlan plan, string state, bool partial = false) => JsonSerializer.SerializeToElement(new
    {
        operationId = Id, state, fingerprint = plan.Fingerprint, entryCount = plan.EntryCount, totalBytes = plan.TotalBytes,
        version = state == "Completed" ? new string('F', 64) : "", partialRetained = partial
    });

    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void ReceiptIsBoundToTheOperationAndOriginalSealedPlan(string state, bool partial)
    {
        var plan = Plan(); Assert.Equal(state, FileTreeCopyPolicy.Receipt(Receipt(plan, state, partial), Id, plan).State);
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Receipt(Receipt(plan, state, partial), new('a', 32), plan));
        var other = plan.Clone(); other.ManifestSha256 = new('1', 64); other.Fingerprint = new('2', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Receipt(Receipt(plan, state, partial), Id, other));
    }

    [Fact]
    public void CompletedCannotClaimPartialRetentionOrUnknownClaimSuccessVersion()
    {
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Receipt(Receipt(Plan(), "Completed", true), Id));
        var node = JsonNode.Parse(Receipt(Plan(), "Unknown").GetRawText())!; node["version"] = new string('A', 64);
        using var invalid = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Receipt(invalid.RootElement, Id));
    }

    [Fact]
    public async Task InvalidUnaryRequestsHaveStableStatusBeforeStartingAnyHelper()
    {
        var service = new FileTreeCopyGrpcService(new FileTransferRunner());
        var missingActor = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal(StatusCode.InvalidArgument, missingActor.StatusCode); Assert.Equal("FILES_ACTOR_REQUIRED", missingActor.Status.Detail);
        var invalidId = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "test-admin" }, OperationId = "bad" }, null!));
        Assert.Equal(StatusCode.InvalidArgument, invalidId.StatusCode); Assert.Equal("FILES_OPERATION_INVALID", invalidId.Status.Detail);
        var invalidOwner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Id, Owner = "bad" }, null!));
        Assert.Equal(StatusCode.InvalidArgument, invalidOwner.StatusCode); Assert.Equal("FILES_HASH_INVALID", invalidOwner.Status.Detail);
    }
}
