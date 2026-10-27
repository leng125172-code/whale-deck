using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Frozen material and authenticated observation contracts, not live interrupted-operation recovery.</summary>
public sealed class FileTreeOverwriteRecoveryMaterialTests : IDisposable
{
    private readonly FileTreeOverwriteRestoringBindingTests _fixture = new();
    private FilePlanProtector Protector => _fixture.Protector;

    public void Dispose() => _fixture.Dispose();
    private FileRecoveryMaterial Material(bool includeOverwrite = true) => new(Protector, Protector, Protector, Protector,
        Protector, Protector, Protector, Protector, Protector, includeOverwrite ? Protector : null);

    private FileTreeOverwriteRestoringOperation Original(bool backup = true)
    {
        var row = _fixture.Operation(); row.State = "Unknown"; row.Version = 17;
        row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        if (backup)
        {
            row.OverwrittenCaptureId = row.ExecutionJobId;
            row.BackupMayBeRetained = true; row.BackupVerified = true;
            row.BackupArchiveBytes = 2099200; row.BackupArchiveSha256 = new('2', 64);
            row.BackupCapturedAtUtc = FileTreeOverwriteRestoringValidation.Timestamp(DateTimeOffset.UtcNow);
            row.BackupProofHash = Protector.FingerprintCapture(FileTreeOverwriteRestoringValidation.BackupBinding(row));
        }
        Assert.True(FileTreeOverwriteRestoringValidation.HasValidBinding(row, Protector));
        return row;
    }

    private static FileTreeOverwriteRestoringReceipt Receipt(FileTreeOverwriteRestoringOperation row, string state)
    {
        var plan = FileTreeOverwriteRestoringBindingTests.Plan(); var verified = row.BackupVerified;
        return new(row.ExecutionJobId!.Value, state, plan.Fingerprint, plan.Command.Format, plan.EntryCount, plan.TotalBytes,
            plan.ArchiveBytes, plan.ArchiveSha256, state == "Completed" ? new('F', 64) : "", state == "Unknown",
            "DirectoryOverwriteRestore", plan.Command.CaptureId, verified ? row.ExecutionJobId : null,
            row.BackupMayBeRetained, verified, state == "Completed", state == "Completed" || state == "Unknown" && verified,
            row.BackupArchiveBytes ?? 0, row.BackupArchiveSha256 ?? "");
    }

    private static FileRecoveryObservation Observe(FileRecoverySnapshot snapshot, FileTreeOverwriteRestoringReceipt receipt)
    {
        var json = JsonSerializer.Serialize(receipt);
        return new(receipt.State, FileRecoveryValidation.ReceiptHash(snapshot.Kind, snapshot.Binding.ExecutionJobId, receipt.State, json),
            receipt.State == "Completed") { ReceiptJson = json };
    }

    private static FileRecoveryObservation Missing(FileRecoverySnapshot snapshot) => new("Missing",
        FileRecoveryValidation.ReceiptHash(snapshot.Kind, snapshot.Binding.ExecutionJobId, "Missing", "null"), false) { ReceiptJson = "null" };

    private static FileRecoverySnapshot ChangeOriginal(FileRecoverySnapshot snapshot, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(snapshot.OriginalJson)!.AsObject(); change(node);
        return new(snapshot.Kind, node.ToJsonString(), snapshot.Binding);
    }

    [Fact]
    public void TenthKindFreezesExactSignedOriginalAndReadsOnlyTheSameOperationType()
    {
        var row = Original(); var material = Material(); var snapshot = material.Capture(row);
        Assert.Equal("tree-overwrite-restore", FileRecoveryKinds.TreeOverwriteRestoring);
        Assert.Equal(FileRecoveryKinds.TreeOverwriteRestoring, snapshot.Kind);
        Assert.Equal(row.Id, snapshot.Binding.IntentId); Assert.Equal(row.ExecutionJobId, snapshot.Binding.ExecutionJobId);
        Assert.Equal(row.Version, snapshot.Binding.OriginalVersion);
        Assert.Equal(JsonSerializer.Serialize(row), snapshot.OriginalJson);
        var frozen = material.Read<FileTreeOverwriteRestoringOperation>(snapshot);
        Assert.NotSame(row, frozen); Assert.Equal(row.PlanJson, frozen.PlanJson);
        Assert.Equal(row.BackupProofHash, frozen.BackupProofHash);
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreeRestoringOperation>(snapshot));
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreeRecyclingOperation>(snapshot));
        Assert.Throws<InvalidOperationException>(() => material.Read<FileUploadOperation>(snapshot));
    }

    [Fact]
    public void MissingOverwriteProtectorCannotFreezeOrReadEvenOtherwiseCorrectMaterial()
    {
        var row = Original(); var snapshot = Material().Capture(row);
        Assert.Throws<InvalidOperationException>(() => Material(false).Capture(row));
        Assert.Throws<InvalidOperationException>(() => Material(false).Read<FileTreeOverwriteRestoringOperation>(snapshot));
    }

    [Theory]
    [InlineData("Completed")][InlineData("Ready")][InlineData("Applying")]
    public void OnlyUnfinishedUnknownIntentCanBeFrozen(string state)
    {
        var row = Original(); row.State = state;
        Assert.Throws<InvalidOperationException>(() => Material().Capture(row));
        row.State = "Unknown"; row.CompletedAtUtc = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() => Material().Capture(row));
    }

    [Theory]
    [InlineData("OriginalVersion")][InlineData("OriginalBindingHash")][InlineData("ExecutionJobId")][InlineData("IntentId")][InlineData("Kind")]
    public void FrozenTargetBindingCannotBeRepointedOrHaveItsDigestReplaced(string field)
    {
        var material = Material(); var snapshot = material.Capture(Original()); var binding = snapshot.Binding;
        var changed = field switch {
            "OriginalVersion" => binding with { OriginalVersion = binding.OriginalVersion + 1 },
            "OriginalBindingHash" => binding with { OriginalBindingHash = new('3', 64) },
            "ExecutionJobId" => binding with { ExecutionJobId = FileTreeRestoringValidation.ExecutionId(binding.IntentId) },
            "IntentId" => binding with { IntentId = Guid.NewGuid() },
            _ => binding with { Kind = FileRecoveryKinds.TreeRestoring } };
        Assert.ThrowsAny<Exception>(() => material.Read<FileTreeOverwriteRestoringOperation>(new(snapshot.Kind, snapshot.OriginalJson, changed)));
    }

    [Theory]
    [InlineData("CommandJson")][InlineData("PlanJson")][InlineData("RequestHash")][InlineData("PlanHash")]
    [InlineData("BackupArchiveBytes")][InlineData("BackupArchiveSha256")][InlineData("BackupProofHash")]
    [InlineData("BackupCapturedAtUtc")][InlineData("OverwrittenCaptureId")][InlineData("OwnerHash")]
    public void EntireFrozenOriginalIncludingBackupProofCannotBeTampered(string field)
    {
        var material = Material(); var snapshot = material.Capture(Original());
        var changed = ChangeOriginal(snapshot, node => {
            switch (field)
            {
                case "CommandJson":
                    var command = JsonNode.Parse(node[field]!.GetValue<string>())!; command["TargetExpectedVersion"] = new string('3', 64);
                    node[field] = command.ToJsonString(); break;
                case "PlanJson":
                    var plan = JsonNode.Parse(node[field]!.GetValue<string>())!; plan["TargetTreeSha256"] = new string('3', 64);
                    node[field] = plan.ToJsonString(); break;
                case "BackupArchiveBytes": node[field] = node[field]!.GetValue<long>() + 1; break;
                case "BackupCapturedAtUtc": node[field] = DateTimeOffset.UnixEpoch; break;
                case "OverwrittenCaptureId": node[field] = Guid.NewGuid(); break;
                default: node[field] = new string('3', 64); break;
            }
        });
        Assert.ThrowsAny<Exception>(() => material.Read<FileTreeOverwriteRestoringOperation>(changed));
    }

    [Fact]
    public void SnapshotUnknownFieldsAndKindSubstitutionAreRejected()
    {
        var material = Material(); var snapshot = material.Capture(Original());
        var changed = ChangeOriginal(snapshot, node => node["Shell"] = "do-not-run");
        Assert.Throws<JsonException>(() => material.Read<FileTreeOverwriteRestoringOperation>(changed));
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreeOverwriteRestoringOperation>(new(FileRecoveryKinds.TreeRestoring, snapshot.OriginalJson, snapshot.Binding)));
    }

    [Theory]
    [InlineData("Completed")][InlineData("Aborted")][InlineData("Unknown")]
    public void RootObservationUsesExactInputAndBackupFactsButOnlyCompletedIsCurrent(string state)
    {
        var material = Material(); var row = Original(); var snapshot = material.Capture(row);
        var receipt = Receipt(row, state); var observation = Observe(snapshot, receipt);
        Assert.Equal(receipt, material.ValidateObservation(snapshot, observation));
        Assert.Equal(state == "Completed", observation.BindingCurrent);
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, observation with { BindingCurrent = !observation.BindingCurrent }));
        if (state == "Unknown")
        {
            Assert.True(receipt.BackupVerified); Assert.True(receipt.BackupMayBeRetained);
            Assert.False(receipt.OriginalsRetained); Assert.Empty(receipt.Version);
            Assert.Equal(row.BackupArchiveSha256, receipt.BackupArchiveSha256);
        }
    }

    [Fact]
    public void MissingRootHasOnlyCanonicalNullReceiptAndCannotClaimCompletion()
    {
        var material = Material(); var snapshot = material.Capture(Original()); var missing = Missing(snapshot);
        Assert.Null(material.ValidateObservation(snapshot, missing));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, missing with { BindingCurrent = true }));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, missing with { ReceiptJson = "{}" }));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, missing with { ReceiptHash = new('3', 64) }));
        Assert.ThrowsAny<Exception>(() => material.ValidateObservation(snapshot,
            missing with { State = "Completed", ReceiptHash = FileRecoveryValidation.ReceiptHash(snapshot.Kind, snapshot.Binding.ExecutionJobId, "Completed", "null") }));
    }

    [Fact]
    public void MissingRootStillCannotBypassTheFrozenOriginalVerification()
    {
        var material = Material(); var snapshot = material.Capture(Original());
        var changed = ChangeOriginal(snapshot, node => node["BackupProofHash"] = new string('3', 64));
        Assert.ThrowsAny<Exception>(() => material.ValidateObservation(changed, Missing(changed)));
    }

    [Theory]
    [InlineData("OperationId")][InlineData("CaptureId")][InlineData("OverwrittenCaptureId")]
    [InlineData("ArchiveSha256")][InlineData("ArchiveBytes")][InlineData("EntryCount")][InlineData("TotalBytes")]
    [InlineData("BackupArchiveSha256")][InlineData("BackupArchiveBytes")][InlineData("Version")]
    public void EvenRehashedObservationCannotSubstituteInputOrDisplacedBackupProof(string field)
    {
        var row = Original(); var material = Material(); var snapshot = material.Capture(row); var receipt = Receipt(row, "Completed");
        var changed = field switch {
            "OperationId" => receipt with { OperationId = Guid.NewGuid() },
            "CaptureId" => receipt with { CaptureId = Guid.NewGuid() },
            "OverwrittenCaptureId" => receipt with { OverwrittenCaptureId = receipt.CaptureId },
            "ArchiveSha256" => receipt with { ArchiveSha256 = new('3', 64) },
            "ArchiveBytes" => receipt with { ArchiveBytes = receipt.ArchiveBytes + 1 },
            "EntryCount" => receipt with { EntryCount = receipt.EntryCount + 1 },
            "TotalBytes" => receipt with { TotalBytes = receipt.TotalBytes + 1 },
            "BackupArchiveSha256" => receipt with { BackupArchiveSha256 = new('3', 64) },
            "BackupArchiveBytes" => receipt with { BackupArchiveBytes = receipt.BackupArchiveBytes + 1 },
            _ => receipt with { Version = "" } };
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observe(snapshot, changed)));
    }

    [Fact]
    public void ObservationHashAndStateCannotBeBorrowedFromOtherKindsOrExecutions()
    {
        var row = Original(); var material = Material(); var snapshot = material.Capture(row);
        var receipt = Receipt(row, "Unknown"); var observation = Observe(snapshot, receipt);
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, observation with {
            ReceiptHash = FileRecoveryValidation.ReceiptHash(FileRecoveryKinds.TreeRestoring, receipt.OperationId, receipt.State, observation.ReceiptJson) }));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, observation with {
            ReceiptHash = FileRecoveryValidation.ReceiptHash(snapshot.Kind, row.Id, receipt.State, observation.ReceiptJson) }));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, observation with { State = "Aborted" }));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, observation with { ReceiptHash = new('3', 64) }));
    }

    [Fact]
    public void OverwriteKindRankCursorAndTargetExecutionRemainIndependent()
    {
        Assert.Equal(12, FileRecoveryKinds.All.Count);
        Assert.Equal(9, FileRecoveryValidation.Rank(FileRecoveryKinds.TreeOverwriteRestoring));
        Assert.Equal(8, FileRecoveryValidation.Rank(FileRecoveryKinds.TreeRestoring));
        var row = Original(); var target = Material().Capture(row).Binding;
        FileRecoveryValidation.Target(target);
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.Target(target with { ExecutionJobId = target.IntentId }));
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.Target(target with { ExecutionJobId = FileTreeRestoringValidation.ExecutionId(target.IntentId) }));
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.Target(target with { ExecutionJobId = FileTreeRecyclingValidation.ExecutionId(target.IntentId) }));
        var cursor = new FileRecoveryCursor(row.CreatedAtUtc.AddTicks(1234567), FileRecoveryKinds.TreeOverwriteRestoring, row.Id);
        var encoded = FileRecoveryValidation.EncodeCursor(cursor);
        Assert.Equal(cursor, FileRecoveryValidation.DecodeCursor(encoded));
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.DecodeCursor(encoded + "="));
    }
}
