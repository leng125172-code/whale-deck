using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Pure business contracts and signatures; no privileged process, RPC, persistence or workstation acceptance.</summary>
public sealed class FileTreeOverwriteRestoringBindingTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "wd-overwrite-binding-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly FilePlanProtector _protector;
    public FilePlanProtector Protector => _protector;

    public FileTreeOverwriteRestoringBindingTests()
    {
        File.WriteAllBytes(_keyPath, _key);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Agent:CapabilityKeyFile"] = _keyPath }).Build());
    }

    public void Dispose()
    {
        File.Delete(_keyPath);
        CryptographicOperations.ZeroMemory(_key);
    }

    public static FileTreeOverwriteRestoringPlan Plan() => new(
        new(new("test.tree", "历史/原目录"), new('A', 64), new("test.tree", "当前/现有目录"), "tar.gz",
            Guid.Parse("4eafca88-f96a-4459-93e9-b26763f1161f"), new('6', 64), "Trash", new('7', 64), new('8', 64)),
        new('F', 64), 1073756160, new('B', 64), 4, 1073741831,
        new(ulong.MaxValue, 9007199254740993UL), new('C', 64), new('D', 64), new('E', 64),
        new('9', 64), new('0', 64), 3, 2097159, new(9007199254740993UL, ulong.MaxValue));

    public FileTreeOverwriteRestoringOperation Operation(bool execution = true)
    {
        var actor = new TerminalActor("overwrite-unit-admin", null, [], new('A', 64), "overwrite-unit", null);
        var key = Guid.NewGuid().ToString("D"); var plan = Plan();
        var id = FileTreeOverwriteRestoringValidation.IntentId(actor, key);
        var now = FileTreeOverwriteRestoringValidation.Timestamp(DateTimeOffset.UtcNow);
        var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() {
            Id = id, CaptureId = plan.Command.CaptureId, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = _protector.FingerprintRequest(new FileTreeOverwriteRestoringRequestBinding(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)),
            SourceHash = new FilePathScope(plan.Command.Source).Hash, TargetHash = new FilePathScope(plan.Command.Target).Hash,
            CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = _protector.FingerprintPlan(new FileTreeOverwriteRestoringPlanBinding(id, actor.Subject, actor.LoginBinding, plan, ready)),
            ReadyExpiresAtUtc = ready, ExecutionJobId = execution ? FileTreeOverwriteRestoringValidation.ExecutionId(id) : null,
            ExecutionExpiresAtUtc = execution ? expiry : null, ArchiveBytes = plan.ArchiveBytes, ArchiveSha256 = plan.ArchiveSha256,
            State = execution ? "OverwriteRestoringQueued" : "Ready" };
    }

    private void AddBackup(FileTreeOverwriteRestoringOperation row)
    {
        row.OverwrittenCaptureId = row.ExecutionJobId;
        row.BackupMayBeRetained = true; row.BackupVerified = true;
        row.BackupArchiveBytes = 2099200; row.BackupArchiveSha256 = new('2', 64);
        row.BackupCapturedAtUtc = FileTreeOverwriteRestoringValidation.Timestamp(DateTimeOffset.UtcNow);
        row.BackupProofHash = _protector.FingerprintCapture(FileTreeOverwriteRestoringValidation.BackupBinding(row));
    }

    private string Mac<T>(T payload, string domain) => Convert.ToHexString(
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(domain + "\n" + JsonSerializer.Serialize(payload))));

    [Fact]
    public void BothVersionAndFullUInt64DirectoryIdentitiesRoundTripWithDistinctExecution()
    {
        var plan = Plan(); FileTreeOverwriteRestoringValidation.Plan(plan);
        var roundtrip = JsonSerializer.Deserialize<FileTreeOverwriteRestoringPlan>(JsonSerializer.Serialize(plan), FileTreeOverwriteRestoringValidation.JsonOptions);
        Assert.Equal(plan, roundtrip);
        Assert.Equal(ulong.MaxValue, roundtrip!.TargetParent.Device);
        Assert.Equal(9007199254740993UL, roundtrip.TargetParent.Inode);
        Assert.Equal(9007199254740993UL, roundtrip.TargetIdentity.Device);
        Assert.Equal(ulong.MaxValue, roundtrip.TargetIdentity.Inode);
        Assert.NotEqual(plan.Command.ExpectedVersion, plan.Command.TargetExpectedVersion);
        var row = Operation(); Assert.True(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        Assert.Equal(plan, FileTreeOverwriteRestoringValidation.ReadPlan(row));
        Assert.NotEqual(row.Id, row.ExecutionJobId); Assert.NotEqual(row.CaptureId, row.ExecutionJobId);
    }

    [Theory]
    [InlineData("Source")][InlineData("ExpectedVersion")][InlineData("Target")][InlineData("TargetExpectedVersion")]
    [InlineData("CaptureId")][InlineData("CaptureOwner")][InlineData("CaptureKind")][InlineData("ManifestSha256")][InlineData("Format")]
    public void RequestSealBindsEveryHistoricalAndCurrentTargetFact(string field)
    {
        var binding = FileTreeOverwriteRestoringValidation.RequestBinding(Operation()); var command = binding.Command;
        var changed = field switch {
            "Source" => command with { Source = new("test.tree", "other-source") },
            "ExpectedVersion" => command with { ExpectedVersion = new('0', 64) },
            "Target" => command with { Target = new("test.tree", "other-target") },
            "TargetExpectedVersion" => command with { TargetExpectedVersion = new('0', 64) },
            "CaptureId" => command with { CaptureId = Guid.NewGuid() },
            "CaptureOwner" => command with { CaptureOwner = new('0', 64) },
            "CaptureKind" => command with { CaptureKind = "Overwrite" },
            "ManifestSha256" => command with { ManifestSha256 = new('0', 64) },
            _ => command with { Format = "tar" } };
        Assert.NotEqual(_protector.FingerprintRequest(binding), _protector.FingerprintRequest(binding with { Command = changed }));
    }

    [Theory]
    [InlineData("ArchiveSha256")][InlineData("ArchiveBytes")][InlineData("ContentSha256")][InlineData("EntryCount")][InlineData("TotalBytes")]
    [InlineData("TargetParent")][InlineData("SourcePolicyHash")][InlineData("TargetPolicyHash")][InlineData("Fingerprint")]
    [InlineData("TargetManifestSha256")][InlineData("TargetTreeSha256")][InlineData("TargetEntryCount")][InlineData("TargetTotalBytes")][InlineData("TargetIdentity")]
    public void PlanSealBindsEveryOriginalArchiveAndDisplacedTreeProof(string field)
    {
        var row = Operation(); var plan = Plan();
        var changed = field switch {
            "ArchiveSha256" => plan with { ArchiveSha256 = new('3', 64) },
            "ArchiveBytes" => plan with { ArchiveBytes = plan.ArchiveBytes + 1 },
            "ContentSha256" => plan with { ContentSha256 = new('3', 64) },
            "EntryCount" => plan with { EntryCount = plan.EntryCount + 1 },
            "TotalBytes" => plan with { TotalBytes = plan.TotalBytes + 1 },
            "TargetParent" => plan with { TargetParent = new(1, 2) },
            "SourcePolicyHash" => plan with { SourcePolicyHash = new('3', 64) },
            "TargetPolicyHash" => plan with { TargetPolicyHash = new('3', 64) },
            "Fingerprint" => plan with { Fingerprint = new('3', 64) },
            "TargetManifestSha256" => plan with { TargetManifestSha256 = new('3', 64) },
            "TargetTreeSha256" => plan with { TargetTreeSha256 = new('3', 64) },
            "TargetEntryCount" => plan with { TargetEntryCount = plan.TargetEntryCount + 1 },
            "TargetTotalBytes" => plan with { TargetTotalBytes = plan.TargetTotalBytes + 1 },
            _ => plan with { TargetIdentity = new(1, 2) } };
        FileTreeOverwriteRestoringValidation.Plan(changed);
        row.PlanJson = JsonSerializer.Serialize(changed);
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row.PlanJson = JsonSerializer.Serialize(plan);
        Assert.True(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void BothExpandedTreesAndArchivesUseSixteenAndSeventeenGiBLimitsIndependently()
    {
        Assert.Equal(16L * 1024 * 1024 * 1024, FileTreeOverwriteRestoringValidation.MaximumBytes);
        Assert.Equal(17L * 1024 * 1024 * 1024, FileTreeOverwriteRestoringValidation.MaximumArchiveBytes);
        var edge = Plan() with { TotalBytes = FileTreeOverwriteRestoringValidation.MaximumBytes,
            TargetTotalBytes = FileTreeOverwriteRestoringValidation.MaximumBytes, EntryCount = 10000, TargetEntryCount = 10000,
            ArchiveBytes = FileTreeOverwriteRestoringValidation.MaximumArchiveBytes };
        FileTreeOverwriteRestoringValidation.Plan(edge);
        FileTreeOverwriteRestoringValidation.Plan(edge with { TotalBytes = 0, TargetTotalBytes = 0 });
        foreach (var changed in new[] { edge with { TotalBytes = edge.TotalBytes + 1 }, edge with { TotalBytes = -1 },
            edge with { TargetTotalBytes = edge.TargetTotalBytes + 1 }, edge with { TargetTotalBytes = -1 },
            edge with { ArchiveBytes = edge.ArchiveBytes + 1 }, edge with { ArchiveBytes = 0 },
            edge with { EntryCount = 10001 }, edge with { TargetEntryCount = 10001 }, edge with { EntryCount = 0 }, edge with { TargetEntryCount = 0 } })
            Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringValidation.Plan(changed));
    }

    [Theory]
    [InlineData("Source")][InlineData("ExpectedVersion")][InlineData("CaptureOwner")][InlineData("CaptureKind")]
    [InlineData("ManifestSha256")][InlineData("Format")][InlineData("OwnerHash")][InlineData("LoginBinding")][InlineData("TargetIdentity")]
    public void HttpRegistrationCannotSupplyHistoricalAuthorityOrHostFacts(string field)
    {
        var plan = Plan();
        var request = new CreateFileTreeOverwriteRestoringCommand(plan.Command.CaptureId, plan.Command.Target, plan.Command.TargetExpectedVersion);
        FileTreeOverwriteRestoringValidation.Request(request);
        var node = JsonSerializer.SerializeToNode(request)!; node[field] = "caller-controlled";
        Assert.Throws<JsonException>(() => node.Deserialize<CreateFileTreeOverwriteRestoringCommand>());
        Assert.DoesNotContain(typeof(FileTreeOverwriteRestoringCommandDto).GetProperties(), property => property.Name is "CaptureOwner" or "LoginBinding" or "OwnerHash" or "ManifestSha256");
        Assert.DoesNotContain(typeof(FileTreeOverwriteRestoringIntentDto).GetProperties(), property => property.PropertyType == typeof(FileTreeOverwriteRestoringCommand));
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a//b")][InlineData("a\\b")]
    public void InvalidRegisteredPathsRejectWithoutAnyPrivilegedWork(string path)
    {
        var command = Plan().Command;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringValidation.Command(command with { Source = new("test.tree", path) }));
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringValidation.Command(command with { Target = new("test.tree", path) }));
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringValidation.Request(new(command.CaptureId, new("test.tree", path), command.TargetExpectedVersion)));
    }

    [Fact]
    public void CaptureLockIsSharedButDeterministicIntentAndExecutionDomainsAreDistinct()
    {
        var row = Operation(); var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, "unit", null);
        Assert.Equal(FileTreeRestoringValidation.CaptureToken(row.CaptureId), FileTreeOverwriteRestoringValidation.CaptureToken(row.CaptureId));
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringValidation.CaptureToken(Guid.Empty));
        Assert.NotEqual(FileTreeRestoringValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreeRecyclingValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreeRestoringValidation.ExecutionId(row.Id), row.ExecutionJobId);
        Assert.NotEqual(FileTreeRecyclingValidation.ExecutionId(row.Id), row.ExecutionJobId);
        Assert.Equal(row.Id, FileTreeOverwriteRestoringValidation.IntentId(actor, row.IdempotencyKey));
        Assert.NotEqual(row.Id, FileTreeOverwriteRestoringValidation.IntentId(actor with { LoginBinding = new('B', 64) }, row.IdempotencyKey));
    }

    [Fact]
    public void IdenticalConfirmationPayloadCannotCrossOverwriteRestoreRecycleOrPackDomain()
    {
        var row = Operation(); var expiry = row.ReadyExpiresAtUtc!.Value; var fingerprint = Plan().Fingerprint;
        var claims = new FileTreeOverwriteRestoringConfirmation(row.Id, row.ActorSubject, row.LoginBinding, fingerprint, expiry);
        var token = _protector.SignConfirmation(claims);
        var restore = _protector.SignConfirmation(new FileTreeRestoringConfirmation(row.Id, row.ActorSubject, row.LoginBinding, fingerprint, expiry));
        var recycle = _protector.SignConfirmation(new FileTreeRecyclingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, fingerprint, expiry));
        var pack = _protector.SignConfirmation(new FileTreePackingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, fingerprint, expiry));
        foreach (var other in new[] { restore, recycle, pack })
        {
            Assert.Equal(token.Split('.')[0], other.Split('.')[0]); Assert.NotEqual(token, other);
            Assert.Throws<ArgumentException>(() => _protector.VerifyOverwriteRestoringConfirmation(other));
        }
        Assert.Throws<ArgumentException>(() => _protector.VerifyRestoringConfirmation(token));
        Assert.Throws<ArgumentException>(() => _protector.VerifyRecyclingConfirmation(token));
        Assert.Throws<ArgumentException>(() => _protector.VerifyPackingConfirmation(token));
        Assert.Equal(claims, _protector.VerifyOverwriteRestoringConfirmation(token));
    }

    [Fact]
    public void RequestPlanAndBackupUseIndependentHmacDomainsForTheirExactSerializedPayload()
    {
        var row = Operation(); AddBackup(row);
        var request = FileTreeOverwriteRestoringValidation.RequestBinding(row);
        var plan = FileTreeOverwriteRestoringValidation.PlanBinding(row);
        var capture = FileTreeOverwriteRestoringValidation.BackupBinding(row);
        Assert.Equal(Mac(request, "whaledeck-tree-overwrite-restore-request-v1"), _protector.FingerprintRequest(request));
        Assert.Equal(Mac(plan, "whaledeck-tree-overwrite-restore-ready-v1"), _protector.FingerprintPlan(plan));
        Assert.Equal(Mac(capture, "whaledeck-tree-overwrite-capture-v1"), _protector.FingerprintCapture(capture));
        Assert.NotEqual(Mac(request, "whaledeck-tree-restore-request-v1"), _protector.FingerprintRequest(request));
        Assert.NotEqual(Mac(plan, "whaledeck-tree-restore-ready-v1"), _protector.FingerprintPlan(plan));
        Assert.NotEqual(Mac(capture, "whaledeck-tree-trash-capture-v1"), _protector.FingerprintCapture(capture));
        Assert.NotEqual(Mac(capture, "whaledeck-tree-overwrite-restore-ready-v1"), _protector.FingerprintCapture(capture));
    }

    [Theory]
    [InlineData("Id")][InlineData("IntentId")][InlineData("OwnerHash")][InlineData("SourceHash")][InlineData("Command")]
    [InlineData("ManifestSha256")][InlineData("ContentSha256")][InlineData("EntryCount")][InlineData("TotalBytes")]
    [InlineData("ArchiveBytes")][InlineData("ArchiveSha256")][InlineData("CreatedAtUtc")]
    public void DisplacedCaptureSignatureBindsEveryIdentityTargetAndArchiveFact(string field)
    {
        var row = Operation(); AddBackup(row); var binding = FileTreeOverwriteRestoringValidation.BackupBinding(row);
        Assert.Equal(row.ExecutionJobId, binding.Id); Assert.Equal(row.Id, binding.IntentId);
        Assert.Equal(Plan().Command.Target, binding.Command.Source);
        Assert.Equal(Plan().Command.TargetExpectedVersion, binding.Command.ExpectedVersion);
        var changed = field switch {
            "Id" => binding with { Id = Guid.NewGuid() }, "IntentId" => binding with { IntentId = Guid.NewGuid() },
            "OwnerHash" => binding with { OwnerHash = new('3', 64) }, "SourceHash" => binding with { SourceHash = new('3', 64) },
            "Command" => binding with { Command = binding.Command with { ExpectedVersion = new('3', 64) } },
            "ManifestSha256" => binding with { ManifestSha256 = new('3', 64) }, "ContentSha256" => binding with { ContentSha256 = new('3', 64) },
            "EntryCount" => binding with { EntryCount = binding.EntryCount + 1 }, "TotalBytes" => binding with { TotalBytes = binding.TotalBytes + 1 },
            "ArchiveBytes" => binding with { ArchiveBytes = binding.ArchiveBytes + 1 }, "ArchiveSha256" => binding with { ArchiveSha256 = new('3', 64) },
            _ => binding with { CreatedAtUtc = binding.CreatedAtUtc.AddMicroseconds(1) } };
        Assert.NotEqual(_protector.FingerprintCapture(binding), _protector.FingerprintCapture(changed));
        row.BackupProofHash = _protector.FingerprintCapture(changed);
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void PersistedBackupProofCannotBeRemovedOrDivergeFromSealedTargetCapture()
    {
        var row = Operation(); AddBackup(row);
        Assert.True(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row.BackupArchiveBytes++;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); AddBackup(row); row.BackupVerified = false;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); AddBackup(row); row.BackupMayBeRetained = false;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); AddBackup(row); row.OverwrittenCaptureId = row.Id;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); AddBackup(row); row.BackupCapturedAtUtc = null;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); row.OriginalsRetained = true;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); row.TargetMayHaveChanged = true;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
    }

    private static FileTreeOverwriteRestoringReceipt Receipt(Guid id, string state, bool verified, bool changed = false, bool retained = false, bool partial = false)
    {
        var plan = Plan();
        return new(id, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256,
            state == "Completed" ? new('F', 64) : "", partial, "DirectoryOverwriteRestore", plan.Command.CaptureId,
            verified ? id : null, verified, verified, retained, changed, verified ? 2099200 : 0, verified ? new('2', 64) : "");
    }

    [Theory]
    [InlineData("Completed", true, true, true, false)][InlineData("Aborted", false, false, false, false)]
    [InlineData("Aborted", true, false, false, true)][InlineData("Unknown", false, false, false, true)]
    [InlineData("Unknown", true, false, false, true)][InlineData("Unknown", true, true, false, true)][InlineData("Unknown", true, true, true, true)]
    public void ReceiptAllowsUncertaintyWithoutInventingCompletedEffects(string state, bool verified, bool changed, bool retained, bool partial)
    {
        var id = Guid.NewGuid(); var receipt = Receipt(id, state, verified, changed, retained, partial);
        FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(Guid.NewGuid(), Plan(), receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), receipt with { CaptureId = id }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), receipt with { ArchiveSha256 = new('3', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), receipt with { Version = state == "Completed" ? "" : new('3', 64) }));
    }

    [Theory]
    [InlineData("BackupVerified")][InlineData("BackupMayBeRetained")][InlineData("OriginalsRetained")][InlineData("TargetMayHaveChanged")]
    [InlineData("OverwrittenCaptureId")][InlineData("BackupArchiveBytes")][InlineData("BackupArchiveSha256")][InlineData("PartialRetained")]
    public void CompletedReceiptRequiresEveryBackupAndOriginalRetentionProof(string field)
    {
        var id = Guid.NewGuid(); var receipt = Receipt(id, "Completed", true, true, true);
        var changed = field switch {
            "BackupVerified" => receipt with { BackupVerified = false }, "BackupMayBeRetained" => receipt with { BackupMayBeRetained = false },
            "OriginalsRetained" => receipt with { OriginalsRetained = false }, "TargetMayHaveChanged" => receipt with { TargetMayHaveChanged = false },
            "OverwrittenCaptureId" => receipt with { OverwrittenCaptureId = receipt.CaptureId }, "BackupArchiveBytes" => receipt with { BackupArchiveBytes = 0 },
            "BackupArchiveSha256" => receipt with { BackupArchiveSha256 = "" }, _ => receipt with { PartialRetained = true } };
        Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), changed));
    }

    [Fact]
    public void UnknownAndAbortedCannotSmuggleBackupProofOrUnsupportedEffects()
    {
        var id = Guid.NewGuid(); var empty = Receipt(id, "Unknown", false);
        FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), empty with { BackupMayBeRetained = true });
        foreach (var changed in new[] { empty with { OverwrittenCaptureId = id }, empty with { BackupArchiveBytes = 1 },
            empty with { BackupArchiveSha256 = new('2', 64) }, empty with { TargetMayHaveChanged = true }, empty with { OriginalsRetained = true },
            Receipt(id, "Aborted", true, true), Receipt(id, "Unknown", true, false, true),
            Receipt(id, "Unknown", true) with { BackupArchiveBytes = FileTreeOverwriteRestoringValidation.MaximumArchiveBytes + 1 } })
            Assert.Throws<FileTransferRejectedException>(() => FileTreeOverwriteRestoringValidation.Receipt(id, Plan(), changed));
    }

    [Fact]
    public void PersistedPlanInputProofAndExecutionIdentityCannotChangeIndependently()
    {
        var row = Operation(); row.ArchiveBytes++;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); row.ArchiveSha256 = new('3', 64);
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); row.ExecutionJobId = Guid.NewGuid();
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        row = Operation(); row.PlanJson = row.PlanJson![..^1] + ",\"shell\":\"tar\"}";
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
    }

    [Theory]
    [InlineData("null")][InlineData("{}")][InlineData("[]")][InlineData("not-json")]
    public void MalformedPersistedPlansFailClosedEvenWhenBackupWasMarkedVerified(string malformed)
    {
        var row = Operation(); AddBackup(row); row.PlanJson = malformed;
        Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void NullPlanCommandAndLocationsCannotReachBackupBindingAsNullReference()
    {
        var row = Operation(); AddBackup(row);
        foreach (var malformed in new[] { Plan() with { Command = null! },
            Plan() with { Command = Plan().Command with { Source = null! } },
            Plan() with { Command = Plan().Command with { Target = null! } },
            Plan() with { TargetIdentity = null! }, Plan() with { TargetParent = null! } })
        {
            row.PlanJson = JsonSerializer.Serialize(malformed);
            Assert.False(FileTreeOverwriteRestoringValidation.HasValidBinding(row, _protector));
        }
    }
}
