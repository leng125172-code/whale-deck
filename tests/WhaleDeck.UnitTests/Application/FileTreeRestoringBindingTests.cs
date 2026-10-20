using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Business material only; not persistent jobs/authorization/Worker acceptance.</summary>
public sealed class FileTreeRestoringBindingTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-restore-seal-" + Guid.NewGuid().ToString("N"));
    private readonly FilePlanProtector _protector;
    public FilePlanProtector Protector => _protector;
    public FileTreeRestoringBindingTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreeRestoringPlan Plan() => new(new(new("test.tree", "original-directory"), new('A', 64), new("test.tree", "new-directory"), "tar.gz",
        Guid.Parse("4eafca88-f96a-4459-93e9-b26763f1161f"), new('6', 64), "Trash", new('7', 64)),
        new('F', 64), 1073756160, new('B', 64), 4, 1073741831, new(ulong.MaxValue, 9007199254740993UL), new('C', 64), new('D', 64), new('E', 64));
    public FileTreeRestoringOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("restore-admin", null, [], new('A', 64), "restore-unit", null); var key = Guid.NewGuid().ToString("D");
        var id = FileTreeRestoringValidation.IntentId(actor, key); var plan = Plan(); var now = FileTreeRestoringValidation.Timestamp(DateTimeOffset.UtcNow);
        var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, CaptureId = plan.Command.CaptureId, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = _protector.FingerprintRequest(new FileTreeRestoringRequestBinding(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)),
            SourceHash = new FilePathScope(plan.Command.Source).Hash, TargetHash = new FilePathScope(plan.Command.Target).Hash,
            CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = _protector.FingerprintPlan(new FileTreeRestoringPlanBinding(id, actor.Subject, actor.LoginBinding, plan, ready)),
            ReadyExpiresAtUtc = ready, ExecutionJobId = execution ? FileTreeRestoringValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? expiry : null,
            ArchiveBytes = plan.ArchiveBytes, ArchiveSha256 = plan.ArchiveSha256, State = execution ? "RestoringQueued" : "Ready",
            LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }
    [Fact]
    public void FullInputAndContentProofRoundtripPreservesUInt64AndIndependentIds()
    {
        var plan = Plan(); FileTreeRestoringValidation.Plan(plan);
        var decoded = JsonSerializer.Deserialize<FileTreeRestoringPlan>(JsonSerializer.Serialize(plan), FileTreeRestoringValidation.JsonOptions);
        Assert.Equal(plan, decoded); Assert.Equal(9007199254740993UL, decoded!.TargetParent.Inode); Assert.Equal(ulong.MaxValue, decoded.TargetParent.Device);
        var row = Operation(); Assert.True(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        Assert.NotEqual(row.Id, row.ExecutionJobId); Assert.Equal(plan, FileTreeRestoringValidation.ReadPlan(row));
        var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, "", null);
        Assert.NotEqual(FileTreePackingValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreePackingValidation.ExecutionId(row.Id), row.ExecutionJobId);
    }
    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a//b")]
    public void PathsRejectBeforeTransport(string path) => Assert.Throws<ArgumentException>(() => FileTreeRestoringValidation.Command(Plan().Command with { Source = new("test.tree", path) }));
    [Theory]
    [InlineData("original-directory")][InlineData("original-directory/child")]
    public void HistoricalSourceDoesNotRequireLivePathOrInheritCopyOverlapRestriction(string path) => FileTreeRestoringValidation.Command(Plan().Command with { Target = new("test.tree", path) });
    [Theory]
    [InlineData("")][InlineData("zip")][InlineData("TAR")][InlineData("tar")]
    public void FormatIsExplicitWithoutFilenamePermissionAssumptions(string format)
    {
        FileTreeRestoringValidation.Command(Plan().Command with { Format = "tar.gz" });
        Assert.Throws<ArgumentException>(() => FileTreeRestoringValidation.Command(Plan().Command with { Format = format }));
    }
    [Fact]
    public void ConfirmationDomainsRejectEachOtherEvenWithTheExactSameKeyAndPayload()
    {
        var row = Operation(); var claims = new FileTreeRestoringConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        var restoreToken = _protector.SignConfirmation(claims);
        var packToken = _protector.SignConfirmation(new FileTreePackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc.Value));
        Assert.Equal(restoreToken.Split('.')[0], packToken.Split('.')[0]); Assert.NotEqual(restoreToken, packToken);
        Assert.Throws<ArgumentException>(() => _protector.VerifyRestoringConfirmation(packToken));
        Assert.Throws<ArgumentException>(() => _protector.VerifyPackingConfirmation(restoreToken));
        Assert.Equal(claims, _protector.VerifyRestoringConfirmation(restoreToken));
    }
    [Fact]
    public void PlanSealBindsBothInputProofAndExpandedContentAndExecutionIdentity()
    {
        var row = Operation(); var plan = Plan();
        foreach (var changed in new[] { plan with { ArchiveBytes = plan.ArchiveBytes + 1 }, plan with { ArchiveSha256 = new('0', 64) }, plan with { ContentSha256 = new('0', 64) },
            plan with { Command = plan.Command with { CaptureOwner = new('0', 64) } }, plan with { TargetParent = new(1, 2) } })
        {
            row.PlanJson = JsonSerializer.Serialize(changed);
            Assert.False(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        }
        row.PlanJson = JsonSerializer.Serialize(plan); Assert.True(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        row.ExecutionJobId = Guid.NewGuid(); Assert.False(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        row.ExecutionJobId = FileTreeRestoringValidation.ExecutionId(row.Id); row.PlanJson = row.PlanJson[..^1] + ",\"shell\":\"tar\"}";
        Assert.False(FileTreeRestoringValidation.HasValidBinding(row, _protector));
    }
    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void AllStatesCarryExactInputProofButOnlyCompletedHasPublishedVersion(string state, bool partial)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var receipt = new FileTreeRestoringReceipt(id, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount,
            plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('F', 64) : "", partial, "DirectoryRestore", plan.Command.CaptureId);
        FileTreeRestoringValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRestoringValidation.Receipt(Guid.NewGuid(), plan, receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRestoringValidation.Receipt(id, plan, receipt with { ArchiveBytes = 0 }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRestoringValidation.Receipt(id, plan, receipt with { ArchiveSha256 = new('0', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRestoringValidation.Receipt(id, plan, receipt with { Version = state == "Completed" ? "" : new('F', 64) }));
        if (state == "Completed") Assert.Throws<FileTransferRejectedException>(() => FileTreeRestoringValidation.Receipt(id, plan, receipt with { PartialRetained = true }));
    }
    [Fact]
    public void LimitsAndMalformedProofsFailClosed()
    {
        var plan = Plan(); FileTreeRestoringValidation.Plan(plan with { ArchiveBytes = FileTreeRestoringValidation.MaximumArchiveBytes, TotalBytes = FileTreeRestoringValidation.MaximumBytes, EntryCount = 10000 });
        foreach (var changed in new[] { plan with { ArchiveBytes = FileTreeRestoringValidation.MaximumArchiveBytes + 1 }, plan with { TotalBytes = FileTreeRestoringValidation.MaximumBytes + 1 },
            plan with { EntryCount = 10001 }, plan with { EntryCount = 0 }, plan with { ArchiveSha256 = "bad" }, plan with { ContentSha256 = "" }, plan with { TargetParent = new(0, 0) } })
            Assert.Throws<ArgumentException>(() => FileTreeRestoringValidation.Plan(changed));
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Aborted")][InlineData("Completed")]
    public void RecoveryMaterialRequiresTheOriginalArchiveProofInEveryReceipt(string state)
    {
        var row = Operation(); row.State = "Unknown"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var material = new FileRecoveryMaterial(_protector, _protector, _protector, _protector, _protector, _protector, _protector, _protector, _protector);
        var snapshot = material.Capture(row); var plan = Plan();
        var receipt = new FileTreeRestoringReceipt(row.ExecutionJobId!.Value, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount,
            plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('F', 64) : "", state == "Unknown", "DirectoryRestore", plan.Command.CaptureId);
        FileRecoveryObservation Observe(FileTreeRestoringReceipt result)
        {
            var json = JsonSerializer.Serialize(result);
            return new(state, FileRecoveryValidation.ReceiptHash(FileRecoveryKinds.TreeRestoring, result.OperationId, state, json), state == "Completed") { ReceiptJson = json };
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
        Assert.False(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        row.ArchiveBytes = Plan().ArchiveBytes; row.ArchiveSha256 = new('0', 64);
        Assert.False(FileTreeRestoringValidation.HasValidBinding(row, _protector));
        row.ArchiveSha256 = Plan().ArchiveSha256;
        Assert.True(FileTreeRestoringValidation.HasValidBinding(row, _protector));
    }

    [Theory]
    [InlineData("CaptureOwner")][InlineData("Source")][InlineData("ExpectedVersion")][InlineData("ManifestSha256")][InlineData("Format")]
    public void PublicRegistrationRejectsCallerProvidedHistoricalAuthority(string field)
    {
        var json = JsonSerializer.SerializeToNode(new CreateFileTreeRestoringCommand(Plan().Command.CaptureId, Plan().Command.Target))!;
        json[field] = "caller-controlled";
        Assert.Throws<JsonException>(() => json.Deserialize<CreateFileTreeRestoringCommand>());
        Assert.DoesNotContain(typeof(FileTreeRestoringIntentDto).GetProperties(), property => property.PropertyType == typeof(FileTreeRestoringCommand));
        Assert.DoesNotContain(typeof(FileTreeRestoringCommandDto).GetProperties(), property => property.Name is "CaptureOwner" or "LoginBinding" or "OwnerHash");
    }

    [Fact]
    public void ActualGatewayWiresRestoringMaterialAndDoesNotBorrowOriginalActorAuthority()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build();
        using var gateway = new AgentGateway(configuration);
        var material = Assert.IsType<FileRecoveryMaterial>(typeof(AgentGateway).GetField("_recoveryMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(gateway));
        var row = Operation(); row.State = "Unknown";
        var snapshot = material.Capture(row);
        Assert.Equal(FileRecoveryKinds.TreeRestoring, snapshot.Kind); Assert.Equal(row.Id, material.Read<FileTreeRestoringOperation>(snapshot).Id);
        Assert.Equal(8, FileRecoveryValidation.Rank(FileRecoveryKinds.TreeRestoring)); Assert.Equal(7, FileRecoveryValidation.Rank(FileRecoveryKinds.TreeRecycling));
    }
}
