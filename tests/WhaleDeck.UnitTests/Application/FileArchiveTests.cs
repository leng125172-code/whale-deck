using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileArchiveTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-archive-test-" + Guid.NewGuid().ToString("N"));
    private readonly FilePlanProtector _signer;
    private readonly IFileArchiveProtector _protector;
    public FileArchiveTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _signer = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
        _protector = _signer;
    }
    public void Dispose() => File.Delete(_key);
    internal static FileArchivePlan Plan(string kind = "trash", bool absent = false) => new(
        new(kind, new("test.files", "source"), kind == "trash" ? null : Guid.Parse("11111111-1111-1111-1111-111111111111"), kind == "purge" || absent ? "ABSENT" : new('A', 64)),
        kind == "purge" ? null : new(ulong.MaxValue, 9007199254740993UL),
        kind == "purge" || absent ? null : new(ulong.MaxValue, 9007199254740993UL, 0x81A4, 1000, 1000, 1, 7, -1),
        kind == "purge" || absent ? "" : new('B', 64),
        kind == "trash" ? null : new(new('C', 64), new(2, 42, 0x8180, 0, 0, 1, 7, long.MaxValue)),
        kind == "trash" ? null : new(ulong.MaxValue, 77, 0x89E8, 1000, 1000, 1, 7, -1), 7, new('B', 64), new('D', 64));

    [Theory]
    [InlineData("trash", false)][InlineData("restore", false)][InlineData("restore", true)][InlineData("purge", true)]
    public void TypedPlansAndSignaturesPreserveExactIdentitiesWithoutChangingOldDomains(string kind, bool absent)
    {
        var plan = Plan(kind, absent); FileArchiveValidation.Plan(plan);
        var claims = new FileArchivePlanClaims("admin", new('E', 64), plan, DateTimeOffset.UtcNow.AddMinutes(5));
        var token = _protector.Sign(claims); Assert.Equal(claims, _protector.VerifyArchive(token));
        Assert.Throws<ArgumentException>(() => _signer.Verify(token));
        Assert.Throws<ArgumentException>(() => ((IFileMutationProtector)_signer).VerifyMutation(token));
        var other = _protector.Sign(claims with { Actor = "other" });
        Assert.Throws<ArgumentException>(() => _protector.VerifyArchive(other.Split('.')[0] + "." + token.Split('.')[1]));
        Assert.Throws<ArgumentException>(() => _protector.VerifyArchive(new('A', 65537)));
    }
    [Theory]
    [InlineData("/etc/passwd")][InlineData("../escape")][InlineData("a//b")][InlineData("a\\b")]
    public void ArchiveCommandsCannotEscapeRegisteredRoots(string path) => Assert.Throws<ArgumentException>(() =>
        FileArchiveValidation.Command(Plan().Command with { Location = new("test.files", path) }));

    [Fact]
    public void IdentityKindSizeAndPrivateBackupBindingAreValidated()
    {
        var trash = Plan(); var restore = Plan("restore"); var purge = Plan("purge");
        var invalid = new[] {
            trash with { Command = trash.Command with { VersionId = Guid.NewGuid() } },
            restore with { Command = restore.Command with { VersionId = Guid.Empty } },
            purge with { Command = purge.Command with { ExpectedVersion = new('A', 64) } },
            trash with { ParentIdentity = new(1, 0) }, trash with { SourceIdentity = trash.SourceIdentity! with { LinkCount = 2 } },
            trash with { SourceIdentity = trash.SourceIdentity! with { OwnerUid = uint.MaxValue } },
            trash with { SourceIdentity = trash.SourceIdentity! with { Mode = 0x41A4 } }, trash with { SizeBytes = 8 },
            trash with { Sha256 = new('F', 64) }, purge with { ParentIdentity = new(1, 2) },
            restore with { ArchiveIdentity = restore.ArchiveIdentity! with { DataIdentity = restore.ArchiveIdentity.DataIdentity with { OwnerUid = 1000 } } },
            restore with { ArchiveIdentity = restore.ArchiveIdentity! with { DataIdentity = restore.ArchiveIdentity.DataIdentity with { Mode = 0x81A4 } } },
            restore with { RestoreMetadata = restore.RestoreMetadata! with { SizeBytes = 8 } },
            restore with { SourceIdentity = null }, restore with { SourceSha256 = "" }
        };
        foreach (var value in invalid) Assert.Throws<ArgumentException>(() => FileArchiveValidation.Plan(value));
    }

    [Fact]
    public void PermanentSealBindsRelationalFieldsAndStrictJsonAsWellAsSignedPayload()
    {
        var plan = Plan(); var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var binding = new FileArchiveBinding(Guid.NewGuid(), "admin", new('E', 64), plan, expiry);
        FileArchiveOperation Row(string? kind = null, string? path = null, string? sha = null, string? json = null) => new() {
            Id = binding.Id, ActorSubject = binding.Actor, LoginBinding = binding.LoginBinding,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + binding.Actor + ":" + binding.LoginBinding), RequestHash = _protector.Fingerprint(binding),
            Kind = kind ?? "trash", RootId = "test.files", RelativePath = path ?? "source", TargetHash = FileManagementService.Hash("test.files:source"),
            ExpectedVersion = plan.Command.ExpectedVersion, Sha256 = sha ?? plan.Sha256, SizeBytes = 7,
            PlanJson = json ?? JsonSerializer.Serialize(plan), ExpiresAtUtc = expiry
        };
        Assert.True(FileArchiveValidation.HasValidBinding(Row(), _protector));
        Assert.False(FileArchiveValidation.HasValidBinding(Row(kind: "purge"), _protector));
        Assert.False(FileArchiveValidation.HasValidBinding(Row(path: "elsewhere"), _protector));
        Assert.False(FileArchiveValidation.HasValidBinding(Row(sha: new('F', 64)), _protector));
        Assert.False(FileArchiveValidation.HasValidBinding(Row(json: JsonSerializer.Serialize(plan)[..^1] + ",\"Unexpected\":1}"), _protector));
        Assert.NotEqual(_protector.Fingerprint(binding), _protector.Fingerprint(binding with { Id = Guid.NewGuid() }));
        Assert.NotEqual(_protector.Fingerprint(binding), _protector.Fingerprint(binding with { ExpiresAtUtc = expiry.AddSeconds(1) }));
    }

    [Theory]
    [InlineData("trash", false)][InlineData("restore", false)][InlineData("restore", true)][InlineData("purge", true)]
    public void CompletedReceiptsRequireExactPlanAndMandatoryOldBytesProof(string kind, bool absent)
    {
        var plan = Plan(kind, absent); var id = Guid.NewGuid(); var retained = kind == "trash" || kind == "restore" && !absent;
        var proof = retained ? new FileHddBackupDto(id.ToString("N"), "database-platform.backup.hdd", plan.Command.ExpectedVersion,
            7, plan.SourceSha256, 1760000000000, false) : null;
        var receipt = new FileArchiveReceipt(kind, "Completed", plan.Fingerprint, new('F', 64), 7, plan.Sha256, retained, true, plan.Command.VersionId ?? id, proof);
        FileArchiveValidation.Receipt(id, plan, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { VersionId = Guid.NewGuid() }));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { Sha256 = new('A', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { SourceRetained = !retained }));
        if (retained) Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { HddBackup = null }));
        if (proof is not null) Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { HddBackup = proof with { OperationId = Guid.NewGuid().ToString("N") } }));
        FileArchiveValidation.Receipt(id, plan, receipt with { State = "Unknown", Version = "", SourceRetained = false, BindingCurrent = false });
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.Receipt(id, plan, receipt with { State = "Unknown" }));
    }

    [Fact]
    public void HistoryPagingRejectsCrossRootDuplicateOutOfOrderCursorAndImpossibleMetadata()
    {
        var command = new FileHistoryCommand("test.files", "", "", 50, "");
        var one = new FileVersionDto(Guid.Parse("11111111-1111-1111-1111-111111111111"), "test.files", "source", new('A', 64), 7, new('B', 64),
            DateTimeOffset.UtcNow, "Overwrite", "Available", "Present", false);
        var page = new FileHistoryPage([one], "", new('C', 64), 1, 0, true); FileArchiveValidation.History(command, page);
        foreach (var row in new[] { one with { RootId = "other" }, one with { CaptureState = "Trashed" }, one with { CaptureKind = "Trash" },
            one with { SizeBytes = -1 }, one with { CapturedAtUtc = DateTimeOffset.UnixEpoch.AddMilliseconds(-1) }, one with { Availability = "Invented" } })
            Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.History(command, page with { Entries = [row] }));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.History(command, page with { Entries = [one, one], Scanned = 2 }));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.History(command, page with { Complete = false, NextCursor = "00000000000000000000000000000001" }));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.History(command with { ExpectedVersion = new('D', 64) }, page));
        Assert.Throws<FileTransferRejectedException>(() => FileArchiveValidation.History(command, page with { Scanned = 501 }));
    }
}
