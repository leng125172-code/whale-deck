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

public sealed class FileTreeCopyTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-tree-seal-" + Guid.NewGuid().ToString("N"));
    public IFileTreeCopyProtector Protector { get; }
    public FileTreeCopyTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        Protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreeCopyPlan Plan() => new(new(new("test.tree", "source"), new('A', 64), new("test.tree", "target")), new('B', 64), 4, 1073741831,
        new(ulong.MaxValue, 9007199254740993UL), new('C', 64), new('D', 64), new('E', 64));
    public FileTreeCopyOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("tree-admin", null, [], new('A', 64), "tree-unit", null); var key = Guid.NewGuid().ToString("D"); var id = FileTreeCopyValidation.IntentId(actor, key);
        var now = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow); var plan = Plan(); var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = Protector.FingerprintRequest(new(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)), SourceHash = new FilePathScope(plan.Command.Source).Hash,
            TargetHash = new FilePathScope(plan.Command.Target).Hash, CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = Protector.FingerprintPlan(new(id, actor.Subject, actor.LoginBinding, plan, ready)), ReadyExpiresAtUtc = ready,
            ExecutionJobId = execution ? FileTreeCopyValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? now.AddMinutes(15) : null,
            State = execution ? "CopyQueued" : "Ready", LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }
    [Fact]
    public void IndependentDirectoryPlanPreservesLargeBytesAndExactUnsignedIdentity()
    {
        var plan = Plan(); FileTreeCopyValidation.Plan(plan); var roundtrip = JsonSerializer.Deserialize<FileTreeCopyPlan>(JsonSerializer.Serialize(plan), FileTreeCopyValidation.JsonOptions);
        Assert.Equal(plan, roundtrip); Assert.True(plan.TotalBytes > FileManagementService.MaximumBytes); Assert.Equal(9007199254740993UL, roundtrip!.TargetParent.Inode);
        var row = Operation(); Assert.True(FileTreeCopyValidation.HasValidBinding(row, Protector));
        Assert.NotEqual(row.Id, row.ExecutionJobId); Assert.Equal(plan, FileTreeCopyValidation.ReadPlan(row));
    }
    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a//b")]
    public void InvalidDirectoryPathsFailBeforeAnyTransport(string path) => Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Command(Plan().Command with { Source = new("test.tree", path) }));
    [Theory]
    [InlineData("source")][InlineData("source/child")]
    public void SameRootDirectoryOverlapIsRejected(string path) => Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Command(Plan().Command with { Target = new("test.tree", path) }));
    [Fact]
    public void LimitsPermitTheFullContractAndRejectOverflowOrPartialIdentity()
    {
        FileTreeCopyValidation.Plan(Plan() with { EntryCount = 10000, TotalBytes = FileTreeCopyValidation.MaximumBytes });
        Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Plan(Plan() with { EntryCount = 10001 }));
        Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Plan(Plan() with { EntryCount = 0 }));
        Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Plan(Plan() with { TotalBytes = FileTreeCopyValidation.MaximumBytes + 1 }));
        Assert.Throws<ArgumentException>(() => FileTreeCopyValidation.Plan(Plan() with { TargetParent = new(1, 0) }));
    }
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void ReceiptIsAnOriginalOperationFactNotAnAssertionOfCurrentTargetState(string state, bool partial)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var receipt = new FileTreeCopyReceipt(id, state, plan.Fingerprint, plan.EntryCount, plan.TotalBytes, state == "Completed" ? new('F', 64) : "", partial);
        FileTreeCopyValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeCopyValidation.Receipt(Guid.NewGuid(), plan, receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeCopyValidation.Receipt(id, plan, receipt with { TotalBytes = 1 }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeCopyValidation.Receipt(id, plan, receipt with { Fingerprint = new('0', 64) }));
        if (state == "Completed") Assert.Throws<FileTransferRejectedException>(() => FileTreeCopyValidation.Receipt(id, plan, receipt with { PartialRetained = true }));
        else Assert.Throws<FileTransferRejectedException>(() => FileTreeCopyValidation.Receipt(id, plan, receipt with { Version = new('F', 64) }));
    }
    [Theory]
    [InlineData("RequestHash")][InlineData("SourceHash")][InlineData("TargetHash")][InlineData("OwnerHash")][InlineData("LoginBinding")][InlineData("PlanHash")]
    public void StoredIntentCannotBeReboundByChangingItsMetadata(string field)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Operation()))!; node[field] = new string('9', 64);
        Assert.False(FileTreeCopyValidation.HasValidBinding(node.Deserialize<FileTreeCopyOperation>()!, Protector));
    }
    [Fact]
    public void StoredCommandAndReadyExpiryAreSealedIndependentlyOfOldSignedPayloads()
    {
        var row = Operation(); var node = JsonNode.Parse(JsonSerializer.Serialize(row))!;
        node["ReadyExpiresAtUtc"] = row.ReadyExpiresAtUtc!.Value.AddSeconds(1);
        Assert.False(FileTreeCopyValidation.HasValidBinding(node.Deserialize<FileTreeCopyOperation>()!, Protector));
        node = JsonNode.Parse(JsonSerializer.Serialize(row))!; node["CommandJson"] = JsonSerializer.Serialize(Plan().Command with { Target = new("test.tree", "other") });
        Assert.False(FileTreeCopyValidation.HasValidBinding(node.Deserialize<FileTreeCopyOperation>()!, Protector));
        var claims = new FileTreeCopyConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc.Value);
        var token = Protector.SignConfirmation(claims); Assert.Equal(claims, Protector.VerifyConfirmation(token));
        Assert.Throws<ArgumentException>(() => Protector.VerifyConfirmation(token[..^1] + (token[^1] == '0' ? "1" : "0")));
        IFileMutationProtector old = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
        Assert.Throws<ArgumentException>(() => old.VerifyMutation(token));
        Assert.Throws<ArgumentException>(() => Protector.VerifyConfirmation(old.Sign(new("actor", new('A', 64), FileMutationTests.Plan(), DateTimeOffset.UtcNow.AddMinutes(5)))));
    }
}
