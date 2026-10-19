using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreePackingPolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileTreePackingCommand Command() => new() { Tree = new() { Source = new() { RootId = "test.shared", Path = "代码/源目录" },
        ExpectedVersion = new('A', 64), Target = new() { RootId = "test.shared", Path = "归档.tar.gz" } }, Format = "tar.gz" };
    private static FileTreePackingPlan Plan() => new() { Command = Command(), ManifestSha256 = new('B', 64), EntryCount = 10000,
        TotalBytes = FileTreeCopyPolicy.MaximumBytes, TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993 },
        SourcePolicyHash = new('C', 64), TargetPolicyHash = new('D', 64), Fingerprint = new('E', 64) };
    [Theory]
    [InlineData("tar")][InlineData("tar.gz")]
    public void IndependentPlanKeepsFormatUInt64AndManifestWithoutLargeFrames(string format)
    {
        var plan = Plan(); plan.Command.Format = format; plan.Command.Tree.Target.Path = "归档." + format;
        var root = FileTreePackingPolicy.PlanInput(plan); var result = FileTreePackingPolicy.Plan(root, plan.Command);
        Assert.Equal(plan, result); Assert.Equal(ulong.MaxValue, result.TargetParent.Device);
        Assert.Equal(9007199254740993UL, result.TargetParent.Inode); Assert.Equal(8, root.EnumerateObject().Count());
        Assert.False(root.TryGetProperty("entries", out _)); Assert.Equal(format, root.GetProperty("command").GetProperty("format").GetString());
        Assert.Throws<InvalidOperationException>(() => FileTreeCopyPolicy.Plan(root, plan.Command.Tree));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
        Assert.Equal(17L * 1024 * 1024 * 1024, FileTreePackingPolicy.MaximumArchiveBytes);
    }
    [Theory]
    [InlineData("")][InlineData("zip")][InlineData("TAR")][InlineData("tar.gz\n")]
    public void UnapprovedFormatsNeverReachHelper(string format)
    {
        var command = Command(); command.Format = format;
        Assert.Throws<ArgumentException>(() => FileTreePackingPolicy.CommandJson(command));
    }
    [Theory]
    [InlineData("/etc/archive.tar.gz")][InlineData("../a.tar.gz")][InlineData("a//b.tar.gz")][InlineData("代码/源目录/nested.tar.gz")][InlineData("wrong.zip")]
    public void InvalidOrNestedTargetsNeverReachHelper(string path)
    {
        var command = Command(); command.Tree.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreePackingPolicy.CommandJson(command));
    }
    [Fact]
    public void SealedPlanRejectsWrongFormatTargetLimitsAndExtraFields()
    {
        var plan = Plan(); var root = FileTreePackingPolicy.PlanInput(plan); var other = plan.Command.Clone(); other.Tree.Target.Path = "other.tar.gz";
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Plan(root, other));
        other.Format = "tar"; other.Tree.Target.Path = "other.tar";
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Plan(root, other));
        plan.EntryCount++; Assert.Throws<ArgumentException>(() => FileTreePackingPolicy.PlanInput(plan));
        plan = Plan(); plan.TotalBytes++; Assert.Throws<ArgumentException>(() => FileTreePackingPolicy.PlanInput(plan));
        var node = JsonNode.Parse(root.GetRawText())!; node["command"]!["shell"] = "tar";
        using var extra = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Plan(extra.RootElement, Command()));
        node["command"]!.AsObject().Remove("shell"); node["entries"] = new JsonArray();
        using var manifest = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Plan(manifest.RootElement, Command()));
    }
    private static JsonElement Receipt(string state, bool partial = false) => JsonSerializer.SerializeToElement(new
    {
        operationId = Id, state, fingerprint = new string('E', 64), format = "tar.gz", entryCount = 10000,
        totalBytes = FileTreeCopyPolicy.MaximumBytes, archiveBytes = state == "Completed" ? FileTreePackingPolicy.MaximumArchiveBytes : 0,
        archiveSha256 = state == "Completed" ? new string('F', 64) : "", version = state == "Completed" ? new string('1', 64) : "", partialRetained = partial
    });
    [Fact]
    public void DedicatedDownloadCeilingDoesNotExpandOrdinaryDownloadOrAllowUnboundedBudgets()
    {
        var metadata = JsonSerializer.SerializeToElement(new { name = "archive.tar", kind = "File", sizeBytes = FileTransferPolicy.MaximumBytes + 1,
            modifiedAtUnixMs = 0, version = new string('A', 64), mode = "640", isProtected = false });
        Assert.Throws<InvalidOperationException>(() => FileTransferPolicy.DownloadEntry(metadata, FileTransferPolicy.MaximumBytes));
        Assert.Equal(FileTransferPolicy.MaximumBytes + 1, FileTransferPolicy.DownloadEntry(metadata, FileTreePackingPolicy.MaximumArchiveBytes).SizeBytes);
        Assert.Throws<InvalidOperationException>(() => FileTransferPolicy.DownloadEntry(metadata, FileTreePackingPolicy.MaximumArchiveBytes + 1));
    }
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void ReceiptBindsOperationFormatFingerprintCountsAndCompletion(string state, bool partial)
    {
        var receipt = Receipt(state, partial); Assert.Equal(state, FileTreePackingPolicy.Receipt(receipt, Id, Plan()).State);
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Receipt(receipt, new('a', 32)));
        var plan = Plan(); plan.Command.Format = "tar"; plan.Command.Tree.Target.Path = "archive.tar";
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Receipt(receipt, Id, plan));
    }
    [Fact]
    public void UnknownCannotClaimBytesOrDigestAndCompletedCannotBePartial()
    {
        Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Receipt(Receipt("Completed", true), Id));
        foreach (var field in new[] { "archiveBytes", "archiveSha256", "version" })
        {
            var node = JsonNode.Parse(Receipt("Unknown").GetRawText())!;
            node[field] = field == "archiveBytes" ? JsonValue.Create(1) : JsonValue.Create(new string('F', 64));
            using var document = JsonDocument.Parse(node.ToJsonString());
            Assert.Throws<InvalidOperationException>(() => FileTreePackingPolicy.Receipt(document.RootElement, Id));
        }
    }
    [Fact]
    public async Task InvalidUnaryAndDownloadRequestsRejectBeforeStartingHelper()
    {
        var service = new FileTreePackingGrpcService(new FileTransferRunner());
        var noActor = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal(StatusCode.InvalidArgument, noActor.StatusCode); Assert.Equal("FILES_ACTOR_REQUIRED", noActor.Status.Detail);
        var badOwner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Id, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", badOwner.Status.Detail);
        var badDownload = await Assert.ThrowsAsync<RpcException>(() => service.Download(new() { OperationId = Id, Owner = new('A', 64) }, null!, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", badDownload.Status.Detail);
        var badApply = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", badApply.Status.Detail);
    }
}
