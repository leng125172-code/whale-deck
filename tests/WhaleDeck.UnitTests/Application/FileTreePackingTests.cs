using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreePackingTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-tree-seal-" + Guid.NewGuid().ToString("N"));
    public IFileTreePackingProtector Protector { get; }
    public FileTreePackingTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        Protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreePackingPlan Plan() => new(new(new("test.tree", "source"), new('A', 64), new("test.tree", "target.tar"), "tar"), new('B', 64), 4, 1073741831,
        new(ulong.MaxValue, 9007199254740993UL), new('C', 64), new('D', 64), new('E', 64));
    public FileTreePackingOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("tree-admin", null, [], new('A', 64), "tree-unit", null); var key = Guid.NewGuid().ToString("D"); var id = FileTreePackingValidation.IntentId(actor, key);
        var now = FileTreePackingValidation.Timestamp(DateTimeOffset.UtcNow); var plan = Plan(); var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = Protector.FingerprintRequest(new(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)), SourceHash = new FilePathScope(plan.Command.Source).Hash,
            TargetHash = new FilePathScope(plan.Command.Target).Hash, CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = Protector.FingerprintPlan(new(id, actor.Subject, actor.LoginBinding, plan, ready)), ReadyExpiresAtUtc = ready,
            ExecutionJobId = execution ? FileTreePackingValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? now.AddMinutes(15) : null,
            State = execution ? "PackingQueued" : "Ready", LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }
    [Fact]
    public void IndependentDirectoryPlanPreservesLargeBytesAndExactUnsignedIdentity()
    {
        var plan = Plan(); FileTreePackingValidation.Plan(plan); var roundtrip = JsonSerializer.Deserialize<FileTreePackingPlan>(JsonSerializer.Serialize(plan), FileTreePackingValidation.JsonOptions);
        Assert.Equal(plan, roundtrip); Assert.True(plan.TotalBytes > FileManagementService.MaximumBytes); Assert.Equal(9007199254740993UL, roundtrip!.TargetParent.Inode);
        var row = Operation(); Assert.True(FileTreePackingValidation.HasValidBinding(row, Protector));
        Assert.NotEqual(row.Id, row.ExecutionJobId); Assert.Equal(plan, FileTreePackingValidation.ReadPlan(row));
    }
    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a//b")]
    public void InvalidDirectoryPathsFailBeforeAnyTransport(string path) => Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Command(Plan().Command with { Source = new("test.tree", path) }));
    [Theory]
    [InlineData("source.tar")][InlineData("source.tar/child.tar")]
    public void SameRootDirectoryOverlapIsRejected(string path) => Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Command(Plan().Command with { Source = new("test.tree", "source.tar"), Target = new("test.tree", path) }));
    [Fact]
    public void FormatAndExtensionAreExplicitAndCannotReuseCopyConfirmation()
    {
        var row = Operation();
        var gzip = Plan().Command with { Format = "tar.gz", Target = new("test.tree", "target.tar.gz") };
        FileTreePackingValidation.Command(gzip);
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Command(gzip with { Format = "zip" }));
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Command(gzip with { Format = "tar" }));
        var claims = new FileTreePackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        using var copy = new FileTreeCopyTests();
        var other = copy.Protector.SignConfirmation(new(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc.Value));
        Assert.Throws<ArgumentException>(() => Protector.VerifyPackingConfirmation(other));
        Assert.NotEqual(FileTreeCopyValidation.IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "", null), row.IdempotencyKey), row.Id);
        Assert.Equal(claims, Protector.VerifyPackingConfirmation(Protector.SignConfirmation(claims)));
    }
    [Fact]
    public void LimitsPermitTheFullContractAndRejectOverflowOrPartialIdentity()
    {
        FileTreePackingValidation.Plan(Plan() with { EntryCount = 10000, TotalBytes = FileTreePackingValidation.MaximumBytes });
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Plan(Plan() with { EntryCount = 10001 }));
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Plan(Plan() with { EntryCount = 0 }));
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Plan(Plan() with { TotalBytes = FileTreePackingValidation.MaximumBytes + 1 }));
        Assert.Throws<ArgumentException>(() => FileTreePackingValidation.Plan(Plan() with { TargetParent = new(1, 0) }));
    }
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void ReceiptIsAnOriginalOperationFactNotAnAssertionOfCurrentTargetState(string state, bool partial)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var receipt = new FileTreePackingReceipt(id, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount, plan.TotalBytes, state == "Completed" ? 2048 : 0, state == "Completed" ? new('D', 64) : "", state == "Completed" ? new('F', 64) : "", partial);
        FileTreePackingValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreePackingValidation.Receipt(Guid.NewGuid(), plan, receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreePackingValidation.Receipt(id, plan, receipt with { TotalBytes = 1 }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreePackingValidation.Receipt(id, plan, receipt with { Fingerprint = new('0', 64) }));
        if (state == "Completed") Assert.Throws<FileTransferRejectedException>(() => FileTreePackingValidation.Receipt(id, plan, receipt with { PartialRetained = true }));
        else Assert.Throws<FileTransferRejectedException>(() => FileTreePackingValidation.Receipt(id, plan, receipt with { Version = new('F', 64) }));
    }
    [Theory]
    [InlineData("RequestHash")][InlineData("SourceHash")][InlineData("TargetHash")][InlineData("OwnerHash")][InlineData("LoginBinding")][InlineData("PlanHash")]
    public void StoredIntentCannotBeReboundByChangingItsMetadata(string field)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Operation()))!; node[field] = new string('9', 64);
        Assert.False(FileTreePackingValidation.HasValidBinding(node.Deserialize<FileTreePackingOperation>()!, Protector));
    }
    [Fact]
    public void StoredCommandAndReadyExpiryAreSealedIndependentlyOfOldSignedPayloads()
    {
        var row = Operation(); var node = JsonNode.Parse(JsonSerializer.Serialize(row))!;
        node["ReadyExpiresAtUtc"] = row.ReadyExpiresAtUtc!.Value.AddSeconds(1);
        Assert.False(FileTreePackingValidation.HasValidBinding(node.Deserialize<FileTreePackingOperation>()!, Protector));
        node = JsonNode.Parse(JsonSerializer.Serialize(row))!; node["CommandJson"] = JsonSerializer.Serialize(Plan().Command with { Target = new("test.tree", "other.tar") });
        Assert.False(FileTreePackingValidation.HasValidBinding(node.Deserialize<FileTreePackingOperation>()!, Protector));
        var claims = new FileTreePackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc.Value);
        var token = Protector.SignConfirmation(claims); Assert.Equal(claims, Protector.VerifyPackingConfirmation(token));
        Assert.Throws<ArgumentException>(() => Protector.VerifyPackingConfirmation(token[..^1] + (token[^1] == '0' ? "1" : "0")));
        IFileMutationProtector old = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
        Assert.Throws<ArgumentException>(() => old.VerifyMutation(token));
        Assert.Throws<ArgumentException>(() => Protector.VerifyPackingConfirmation(old.Sign(new("actor", new('A', 64), FileMutationTests.Plan(), DateTimeOffset.UtcNow.AddMinutes(5)))));
    }
}
