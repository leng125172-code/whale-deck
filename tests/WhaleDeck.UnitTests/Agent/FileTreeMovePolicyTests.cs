using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeMovePolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileTreeCopyCommand Command() => new() { Source = new() { RootId = "test.shared", Path = "代码/原目录" }, ExpectedVersion = new('A', 64),
        Target = new() { RootId = "test.shared", Path = "代码/目标目录" } };
    private static FileTreeMovePlan Plan() => new() { CopyPlan = new() { Command = Command(), ManifestSha256 = new('B', 64), EntryCount = 10000,
        TotalBytes = FileTreeCopyPolicy.MaximumBytes, TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993 },
        SourcePolicyHash = new('C', 64), TargetPolicyHash = new('D', 64), Fingerprint = new('E', 64) },
        SourceParent = new() { Device = 9007199254740995, Inode = ulong.MaxValue }, Fingerprint = new('F', 64) };

    [Fact]
    public void RoundTripKeepsNestedUInt64AndNoPrivateManifestOrArbitraryIdentity()
    {
        var plan = Plan(); var input = FileTreeMovePolicy.PlanInput(plan); var result = FileTreeMovePolicy.Plan(input, plan.CopyPlan.Command);
        Assert.Equal(plan, result); Assert.Equal(ulong.MaxValue, result.SourceParent.Inode); Assert.Equal(9007199254740995UL, result.SourceParent.Device);
        Assert.Equal(ulong.MaxValue, result.CopyPlan.TargetParent.Device); Assert.True(result.CopyPlan.TotalBytes > FileTransferPolicy.MaximumBytes);
        Assert.Equal(3, input.EnumerateObject().Count()); Assert.DoesNotContain("entries", input.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("retainedIdentity", input.GetRawText(), StringComparison.Ordinal); Assert.DoesNotContain("ownerUid", input.GetRawText(), StringComparison.Ordinal);
    }
    [Fact]
    public void InputRejectsMissingNestedPlanBadParentAndReboundCommand()
    {
        Assert.Throws<ArgumentException>(() => FileTreeMovePolicy.PlanInput(null));
        var plan = Plan(); plan.CopyPlan = null; Assert.Throws<ArgumentException>(() => FileTreeMovePolicy.PlanInput(plan));
        plan = Plan(); plan.SourceParent.Inode = 0; Assert.Throws<ArgumentException>(() => FileTreeMovePolicy.PlanInput(plan));
        plan = Plan(); plan.Fingerprint = "bad"; Assert.Throws<ArgumentException>(() => FileTreeMovePolicy.PlanInput(plan));
        var other = Command(); other.Target.Path = "other";
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Plan(FileTreeMovePolicy.PlanInput(Plan()), other));
    }
    [Theory]
    [InlineData("/etc")][InlineData("a//b")][InlineData("a/../b")][InlineData("a\\b")][InlineData("")]
    public void SourceAndTargetValidationIsStillTheRegisteredTreeLocationPolicy(string path)
    {
        var plan = Plan(); plan.CopyPlan.Command.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeMovePolicy.PlanInput(plan));
    }
    [Fact]
    public void UnknownOuterAndInnerFieldsAndInvalidUInt64AreNotAdopted()
    {
        var node = JsonNode.Parse(FileTreeMovePolicy.PlanInput(Plan()).GetRawText())!; node["hostPath"] = "/etc";
        using var extra = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Plan(extra.RootElement, Command()));
        node.AsObject().Remove("hostPath"); node["copyPlan"]!["command"]!["source"]!["uid"] = 0;
        using var inner = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Plan(inner.RootElement, Command()));
        node["copyPlan"]!["command"]!["source"]!.AsObject().Remove("uid"); node["sourceParent"]![1] = -1;
        using var negative = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<FormatException>(() => FileTreeMovePolicy.Plan(negative.RootElement, Command()));
    }
    private static JsonElement Receipt(FileTreeMovePlan plan, string state, bool published, bool partial) => JsonSerializer.SerializeToElement(new
    {
        operationId = Id, state, fingerprint = plan.Fingerprint, entryCount = plan.CopyPlan.EntryCount, totalBytes = plan.CopyPlan.TotalBytes,
        version = state == "Completed" ? new string('1', 64) : "", destinationPublished = published,
        sourceRemoved = state == "Completed", sourceRetained = state == "Completed", partialRetained = partial
    });
    [Theory]
    [InlineData("Completed", true, false)][InlineData("Aborted", false, false)][InlineData("Aborted", false, true)]
    [InlineData("Unknown", true, true)][InlineData("Unknown", false, false)][InlineData("Unknown", false, true)]
    public void ReceiptFlagsDistinguishCompletedRetentionFromPartialOrUnknownMove(string state, bool published, bool partial)
    {
        var plan = Plan(); var value = Receipt(plan, state, published, partial);
        var parsed = FileTreeMovePolicy.Receipt(value, Id, plan);
        Assert.Equal(state, parsed.State); Assert.Equal(published, parsed.DestinationPublished);
        Assert.Equal(partial, parsed.PartialRetained); Assert.Equal(state == "Completed", parsed.SourceRemoved);
        Assert.Equal(state == "Completed", parsed.SourceRetained); Assert.Equal(plan.CopyPlan.TotalBytes, parsed.TotalBytes);
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(value, new('a', 32), plan));
        var other = plan.Clone(); other.Fingerprint = new('2', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(value, Id, other));
    }
    [Fact]
    public void ContradictoryCompletionAndUnknownFlagsNeverBecomeSuccess()
    {
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(Receipt(Plan(), "Completed", false, false), Id));
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(Receipt(Plan(), "Completed", true, true), Id));
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(Receipt(Plan(), "Aborted", true, true), Id));
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(Receipt(Plan(), "Unknown", true, false), Id));
        foreach (var field in new[] { "sourceRemoved", "sourceRetained", "version" })
        {
            var node = JsonNode.Parse(Receipt(Plan(), "Unknown", false, false).GetRawText())!;
            node[field] = field == "version" ? JsonValue.Create(new string('1', 64)) : JsonValue.Create(true);
            using var malformed = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(malformed.RootElement, Id));
        }
    }
    [Theory]
    [InlineData("entryCount", "0")][InlineData("entryCount", "10001")]
    [InlineData("totalBytes", "-1")][InlineData("totalBytes", "17179869185")]
    public void ReceiptCountAndByteLimitsCannotBeBypassed(string field, string invalid)
    {
        var node = JsonNode.Parse(Receipt(Plan(), "Completed", true, false).GetRawText())!;
        node[field] = JsonNode.Parse(invalid);
        using var malformed = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreeMovePolicy.Receipt(malformed.RootElement, Id));
    }
    [Theory]
    [InlineData("sourceParent", 0)][InlineData("sourceParent", 1)]
    [InlineData("targetParent", 0)][InlineData("targetParent", 1)]
    public void NeitherSourceNorTargetIdentityCanAdoptNegativeOverflowOrText(string field, int index)
    {
        foreach (var invalid in new[] { "-1", "18446744073709551616", "\"1\"", "true" })
        {
            var node = JsonNode.Parse(FileTreeMovePolicy.PlanInput(Plan()).GetRawText())!;
            var parent = field == "sourceParent" ? node[field]! : node["copyPlan"]![field]!;
            parent[index] = JsonNode.Parse(invalid);
            using var malformed = JsonDocument.Parse(node.ToJsonString());
            Assert.ThrowsAny<Exception>(() => FileTreeMovePolicy.Plan(malformed.RootElement, Command()));
        }
    }
    [Fact]
    public async Task InvalidUnaryRequestsFailWithStableStatusBeforeOpeningAnyHelper()
    {
        var service = new FileTreeMoveGrpcService(new FileTransferRunner());
        var actor = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal(StatusCode.InvalidArgument, actor.StatusCode); Assert.Equal("FILES_ACTOR_REQUIRED", actor.Status.Detail);
        var id = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "test-admin" }, OperationId = "bad" }, null!));
        Assert.Equal(StatusCode.InvalidArgument, id.StatusCode); Assert.Equal("FILES_OPERATION_INVALID", id.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Id, Owner = "bad" }, null!));
        Assert.Equal(StatusCode.InvalidArgument, owner.StatusCode); Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
    }
}
