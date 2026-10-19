using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Business material only; not persistent jobs/authorization/Worker acceptance.</summary>
public sealed class FileTreeUnpackingBindingTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-unpack-seal-" + Guid.NewGuid().ToString("N"));
    private readonly FilePlanProtector _protector;
    public FilePlanProtector Protector => _protector;
    public FileTreeUnpackingBindingTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreeUnpackingPlan Plan() => new(new(new("test.tree", "input.archive"), new('A', 64), new("test.tree", "new-directory"), "tar"),
        new('F', 64), 1073756160, new('B', 64), 4, 1073741831, new(ulong.MaxValue, 9007199254740993UL), new('C', 64), new('D', 64), new('E', 64));
    public FileTreeUnpackingOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("unpack-admin", null, [], new('A', 64), "unpack-unit", null); var key = Guid.NewGuid().ToString("D");
        var id = FileTreeUnpackingValidation.IntentId(actor, key); var plan = Plan(); var now = FileTreeUnpackingValidation.Timestamp(DateTimeOffset.UtcNow);
        var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = _protector.FingerprintRequest(new FileTreeUnpackingRequestBinding(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)),
            SourceHash = new FilePathScope(plan.Command.Source).Hash, TargetHash = new FilePathScope(plan.Command.Target).Hash,
            CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = _protector.FingerprintPlan(new FileTreeUnpackingPlanBinding(id, actor.Subject, actor.LoginBinding, plan, ready)),
            ReadyExpiresAtUtc = ready, ExecutionJobId = execution ? FileTreeUnpackingValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? expiry : null,
            ArchiveBytes = plan.ArchiveBytes, ArchiveSha256 = plan.ArchiveSha256, State = execution ? "UnpackingQueued" : "Ready",
            LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }
    [Fact]
    public void FullInputAndContentProofRoundtripPreservesUInt64AndIndependentIds()
    {
        var plan = Plan(); FileTreeUnpackingValidation.Plan(plan);
        var decoded = JsonSerializer.Deserialize<FileTreeUnpackingPlan>(JsonSerializer.Serialize(plan), FileTreeUnpackingValidation.JsonOptions);
        Assert.Equal(plan, decoded); Assert.Equal(9007199254740993UL, decoded!.TargetParent.Inode); Assert.Equal(ulong.MaxValue, decoded.TargetParent.Device);
        var row = Operation(); Assert.True(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        Assert.NotEqual(row.Id, row.ExecutionJobId); Assert.Equal(plan, FileTreeUnpackingValidation.ReadPlan(row));
        var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, "", null);
        Assert.NotEqual(FileTreePackingValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreePackingValidation.ExecutionId(row.Id), row.ExecutionJobId);
    }
    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a//b")]
    public void PathsRejectBeforeTransport(string path) => Assert.Throws<ArgumentException>(() => FileTreeUnpackingValidation.Command(Plan().Command with { Source = new("test.tree", path) }));
    [Theory]
    [InlineData("input.archive")][InlineData("input.archive/child")]
    public void OverlapCannotBeUsedAsAnOverwriteRequest(string path) => Assert.Throws<ArgumentException>(() => FileTreeUnpackingValidation.Command(Plan().Command with { Target = new("test.tree", path) }));
    [Theory]
    [InlineData("")][InlineData("zip")][InlineData("TAR")]
    public void FormatIsExplicitWithoutFilenamePermissionAssumptions(string format)
    {
        FileTreeUnpackingValidation.Command(Plan().Command with { Format = "tar.gz" });
        Assert.Throws<ArgumentException>(() => FileTreeUnpackingValidation.Command(Plan().Command with { Format = format }));
    }
    [Fact]
    public void ConfirmationDomainsRejectEachOtherEvenWithTheExactSameKeyAndPayload()
    {
        var row = Operation(); var claims = new FileTreeUnpackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        var unpackToken = _protector.SignConfirmation(claims);
        var packToken = _protector.SignConfirmation(new FileTreePackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc.Value));
        Assert.Equal(unpackToken.Split('.')[0], packToken.Split('.')[0]); Assert.NotEqual(unpackToken, packToken);
        Assert.Throws<ArgumentException>(() => _protector.VerifyUnpackingConfirmation(packToken));
        Assert.Throws<ArgumentException>(() => _protector.VerifyPackingConfirmation(unpackToken));
        Assert.Equal(claims, _protector.VerifyUnpackingConfirmation(unpackToken));
    }
    [Fact]
    public void PlanSealBindsBothInputProofAndExpandedContentAndExecutionIdentity()
    {
        var row = Operation(); var plan = Plan();
        foreach (var changed in new[] { plan with { ArchiveBytes = plan.ArchiveBytes + 1 }, plan with { ArchiveSha256 = new('0', 64) }, plan with { ContentSha256 = new('0', 64) },
            plan with { Command = plan.Command with { Format = "tar.gz" } }, plan with { TargetParent = new(1, 2) } })
        {
            row.PlanJson = JsonSerializer.Serialize(changed);
            Assert.False(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        }
        row.PlanJson = JsonSerializer.Serialize(plan); Assert.True(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        row.ExecutionJobId = Guid.NewGuid(); Assert.False(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        row.ExecutionJobId = FileTreeUnpackingValidation.ExecutionId(row.Id); row.PlanJson = row.PlanJson[..^1] + ",\"shell\":\"tar\"}";
        Assert.False(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
    }
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void AllStatesCarryExactInputProofButOnlyCompletedHasPublishedVersion(string state, bool partial)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var receipt = new FileTreeUnpackingReceipt(id, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount,
            plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('F', 64) : "", partial);
        FileTreeUnpackingValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeUnpackingValidation.Receipt(Guid.NewGuid(), plan, receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeUnpackingValidation.Receipt(id, plan, receipt with { ArchiveBytes = 0 }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeUnpackingValidation.Receipt(id, plan, receipt with { ArchiveSha256 = new('0', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeUnpackingValidation.Receipt(id, plan, receipt with { Version = state == "Completed" ? "" : new('F', 64) }));
        if (state == "Completed") Assert.Throws<FileTransferRejectedException>(() => FileTreeUnpackingValidation.Receipt(id, plan, receipt with { PartialRetained = true }));
    }
    [Fact]
    public void LimitsAndMalformedProofsFailClosed()
    {
        var plan = Plan(); FileTreeUnpackingValidation.Plan(plan with { ArchiveBytes = FileTreeUnpackingValidation.MaximumArchiveBytes, TotalBytes = FileTreeUnpackingValidation.MaximumBytes, EntryCount = 10000 });
        foreach (var changed in new[] { plan with { ArchiveBytes = FileTreeUnpackingValidation.MaximumArchiveBytes + 1 }, plan with { TotalBytes = FileTreeUnpackingValidation.MaximumBytes + 1 },
            plan with { EntryCount = 10001 }, plan with { EntryCount = 0 }, plan with { ArchiveSha256 = "bad" }, plan with { ContentSha256 = "" }, plan with { TargetParent = new(0, 0) } })
            Assert.Throws<ArgumentException>(() => FileTreeUnpackingValidation.Plan(changed));
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Aborted")][InlineData("Completed")]
    public void RecoveryMaterialRequiresTheOriginalArchiveProofInEveryReceipt(string state)
    {
        var row = Operation(); row.State = "Unknown"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var material = new FileRecoveryMaterial(_protector, _protector, _protector, _protector, _protector, _protector, _protector);
        var snapshot = material.Capture(row); var plan = Plan();
        var receipt = new FileTreeUnpackingReceipt(row.ExecutionJobId!.Value, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount,
            plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('F', 64) : "", state == "Unknown");
        FileRecoveryObservation Observe(FileTreeUnpackingReceipt result)
        {
            var json = JsonSerializer.Serialize(result);
            return new(state, FileRecoveryValidation.ReceiptHash(FileRecoveryKinds.TreeUnpacking, result.OperationId, state, json), state == "Completed") { ReceiptJson = json };
        }
        Assert.Equal(receipt, material.ValidateObservation(snapshot, Observe(receipt)));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observe(receipt with { ArchiveBytes = 0 })));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observe(receipt with { ArchiveSha256 = new('0', 64) })));
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreePackingOperation>(snapshot));
    }
    [Fact]
    public void PersistedInputProofCannotDivergeFromItsSealedPlan()
    {
        var row = Operation(); row.ArchiveBytes++;
        Assert.False(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        row.ArchiveBytes = Plan().ArchiveBytes; row.ArchiveSha256 = new('0', 64);
        Assert.False(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
        row.ArchiveSha256 = Plan().ArchiveSha256;
        Assert.True(FileTreeUnpackingValidation.HasValidBinding(row, _protector));
    }
}
