using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreeMoveTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-move-seal-" + Guid.NewGuid().ToString("N"));
    private readonly FilePlanProtector _protector;
    public IFileTreeMoveProtector Protector => _protector;
    public FileTreeMoveTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreeMovePlan Plan() => new(FileTreeCopyTests.Plan(), new(9007199254740995UL, ulong.MaxValue), new('F', 64));
    public FileTreeMoveOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("tree-admin", null, [], new('A', 64), "tree-unit", null); var key = Guid.NewGuid().ToString("D"); var id = FileTreeMoveValidation.IntentId(actor, key);
        var now = FileTreeMoveValidation.Timestamp(DateTimeOffset.UtcNow); var plan = Plan(); var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = Protector.FingerprintRequest(new(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)), SourceHash = new FilePathScope(plan.Command.Source).Hash,
            TargetHash = new FilePathScope(plan.Command.Target).Hash, CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = Protector.FingerprintPlan(new(id, actor.Subject, actor.LoginBinding, plan, ready)), ReadyExpiresAtUtc = ready,
            ExecutionJobId = execution ? FileTreeMoveValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? now.AddMinutes(15) : null,
            State = execution ? "MoveQueued" : "Ready", LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }

    [Fact]
    public void ImmutablePlansPreserveAllUnsignedBitsAndIndependentDigest()
    {
        var plan = Plan(); FileTreeMoveValidation.Plan(plan);
        var result = JsonSerializer.Deserialize<FileTreeMovePlan>(JsonSerializer.Serialize(plan), FileTreeCopyValidation.JsonOptions);
        Assert.Equal(plan, result); Assert.Equal(ulong.MaxValue, result!.SourceParent.Inode);
        Assert.NotEqual(plan.Fingerprint, plan.CopyPlan.Fingerprint);
        Assert.Throws<ArgumentException>(() => FileTreeMoveValidation.Plan(plan with { SourceParent = new(1, 0) }));
        Assert.Throws<ArgumentException>(() => FileTreeMoveValidation.Plan(plan with { Fingerprint = "bad" }));
        Assert.Equal(3, JsonSerializer.SerializeToElement(plan).EnumerateObject().Count());
    }
    [Theory]
    [InlineData("RequestHash")][InlineData("SourceHash")][InlineData("TargetHash")][InlineData("OwnerHash")][InlineData("LoginBinding")][InlineData("PlanHash")]
    public void DurableMoveIntentCannotBeReboundThroughMutableMetadata(string field)
    {
        var row = Operation(); Assert.True(FileTreeMoveValidation.HasValidBinding(row, Protector));
        var node = JsonNode.Parse(JsonSerializer.Serialize(row))!; node[field] = new string('9', 64);
        Assert.False(FileTreeMoveValidation.HasValidBinding(node.Deserialize<FileTreeMoveOperation>()!, Protector));
    }
    [Fact]
    public void SourceParentReadyExpiryAndOriginalCommandAreAllSealed()
    {
        var row = Operation(); var node = JsonNode.Parse(JsonSerializer.Serialize(row))!;
        node["PlanJson"] = JsonSerializer.Serialize(Plan() with { SourceParent = new(1, 2) });
        Assert.False(FileTreeMoveValidation.HasValidBinding(node.Deserialize<FileTreeMoveOperation>()!, Protector));
        node = JsonNode.Parse(JsonSerializer.Serialize(row))!; node["ReadyExpiresAtUtc"] = row.ReadyExpiresAtUtc!.Value.AddSeconds(1);
        Assert.False(FileTreeMoveValidation.HasValidBinding(node.Deserialize<FileTreeMoveOperation>()!, Protector));
        node = JsonNode.Parse(JsonSerializer.Serialize(row))!; node["CommandJson"] = JsonSerializer.Serialize(Plan().Command with { Target = new("test.tree", "other") });
        Assert.False(FileTreeMoveValidation.HasValidBinding(node.Deserialize<FileTreeMoveOperation>()!, Protector));
    }
    [Fact]
    public void CopyAndMoveCannotShareRegistrationExecutionOrConfirmationDomains()
    {
        var actor = new TerminalActor("admin", null, [], new('A', 64), "test", null); var key = Guid.NewGuid().ToString("D");
        var move = FileTreeMoveValidation.IntentId(actor, key); var copy = FileTreeCopyValidation.IntentId(actor, key);
        Assert.NotEqual(move, copy); Assert.NotEqual(FileTreeMoveValidation.ExecutionId(move), FileTreeCopyValidation.ExecutionId(move));
        Assert.NotEqual(move, FileTreeMoveValidation.IntentId(actor with { LoginBinding = new('B', 64) }, key));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5); var claims = new FileTreeMoveConfirmation(move, actor.Subject, actor.LoginBinding, Plan().Fingerprint, expiry);
        var moveToken = _protector.SignConfirmation(claims); Assert.Equal(claims, _protector.VerifyMoveConfirmation(moveToken));
        var copyToken = _protector.SignConfirmation(new FileTreeCopyConfirmation(move, actor.Subject, actor.LoginBinding, Plan().Fingerprint, expiry));
        Assert.Throws<ArgumentException>(() => _protector.VerifyMoveConfirmation(copyToken));
        Assert.Throws<ArgumentException>(() => _protector.VerifyConfirmation(moveToken));
        Assert.Throws<ArgumentException>(() => _protector.VerifyMoveConfirmation(moveToken[..^1] + (moveToken[^1] == 'A' ? "B" : "A")));
        Assert.NotEqual(_protector.FingerprintRequest(new FileTreeCopyRequestBinding(move, actor.Subject, actor.LoginBinding, Plan().CopyPlan.Command, expiry)),
            _protector.FingerprintRequest(new FileTreeMoveRequestBinding(move, actor.Subject, actor.LoginBinding, Plan().CopyPlan.Command, expiry)));
        Assert.NotEqual(_protector.FingerprintPlan(new FileTreeMovePlanBinding(move, actor.Subject, actor.LoginBinding, Plan(), expiry)),
            _protector.FingerprintPlan(new FileTreeMovePlanBinding(move, actor.Subject, actor.LoginBinding, Plan() with { SourceParent = new(1, 2) }, expiry)));
    }
    [Theory]
    [InlineData("Completed", true, true, true, false)]
    [InlineData("Aborted", false, false, false, false)][InlineData("Aborted", false, false, false, true)]
    [InlineData("Unknown", false, false, false, false)][InlineData("Unknown", true, false, false, true)]
    public void HistoricalReceiptAcceptsOnlyExactPlanAndKeepsRetentionFacts(string state, bool published, bool removed, bool retained, bool partial)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var receipt = new FileTreeMoveReceipt(id, state, plan.Fingerprint, plan.CopyPlan.EntryCount, plan.CopyPlan.TotalBytes,
            state == "Completed" ? new('1', 64) : "", published, removed, retained, partial);
        FileTreeMoveValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(Guid.NewGuid(), plan, receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(id, plan, receipt with { TotalBytes = 1 }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(id, plan, receipt with { Fingerprint = new('0', 64) }));
        if (state == "Completed")
        {
            Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(id, plan, receipt with { SourceRetained = false }));
            Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(id, plan, receipt with { SourceRemoved = false }));
        }
        else Assert.Throws<FileTransferRejectedException>(() => FileTreeMoveValidation.Receipt(id, plan, receipt with { SourceRemoved = true }));
    }
}
