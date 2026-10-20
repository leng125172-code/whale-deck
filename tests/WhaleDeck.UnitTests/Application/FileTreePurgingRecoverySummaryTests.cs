using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Public recovery facts only; no Agent, persistence or physical deletion.</summary>
public sealed class FileTreePurgingRecoverySummaryTests
{
    private static readonly string[] ExpectedPublicFields = ["archiveRemoved", "captureId", "captureKind", "deletionStarted", "originalsRemoved", "scope", "state", "version"];

    [Theory]
    [InlineData("Archive", "Trash")][InlineData("Originals", "Trash")][InlineData("All", "Trash")]
    [InlineData("Archive", "Overwrite")][InlineData("Originals", "Overwrite")][InlineData("All", "Overwrite")]
    public void NoRootAdmissionIsFalseButLostRootResponseLeavesOnlySelectedComponentsUnknown(string scope, string kind)
    {
        using var fixture = new FileTreePurgingBindingTests();
        var row = fixture.Operation(true, scope, kind); row.Version = 17;
        var before = FileTreePurgingValidation.RecoverySummary(row, fixture.Protector);
        Assert.Equal(row.CaptureId, before.CaptureId); Assert.Equal(kind, before.CaptureKind); Assert.Equal(scope, before.Scope);
        Assert.Equal(17, before.Version); Assert.Equal("PurgingQueued", before.State);
        Assert.Equal(false, before.DeletionStarted); Assert.Equal(false, before.ArchiveRemoved); Assert.Equal(false, before.OriginalsRemoved);

        AdmitWithoutResponse(row); row.Version++;
        var lost = FileTreePurgingValidation.RecoverySummary(row, fixture.Protector);
        Assert.Equal(18, lost.Version); Assert.Equal("Unknown", lost.State); Assert.Null(lost.DeletionStarted);
        Assert.Equal(scope == "Originals" ? false : (bool?)null, lost.ArchiveRemoved);
        Assert.Equal(scope == "Archive" ? false : (bool?)null, lost.OriginalsRemoved);
    }

    [Theory]
    [InlineData("Archive", true, false)]
    [InlineData("Originals", false, true)]
    [InlineData("All", false, true)]
    [InlineData("All", true, false)]
    [InlineData("All", true, true)]
    public void SignedUnknownPreservesEachEstablishedPositiveWithoutInventingSelectedNegativeFacts(string scope, bool archiveRemoved, bool originalsRemoved)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(true, scope);
        AttachReceipt(row, fixture, "Unknown", archiveRemoved, originalsRemoved);
        var summary = FileTreePurgingValidation.RecoverySummary(row, fixture.Protector);
        Assert.Equal(true, summary.DeletionStarted); Assert.Equal("Unknown", summary.State);
        Assert.Equal(archiveRemoved ? true : scope == "Originals" ? false : (bool?)null, summary.ArchiveRemoved);
        Assert.Equal(originalsRemoved ? true : scope == "Archive" ? false : (bool?)null, summary.OriginalsRemoved);
    }

    [Theory]
    [InlineData("Archive", "Trash")][InlineData("Originals", "Trash")][InlineData("All", "Trash")]
    [InlineData("Archive", "Overwrite")][InlineData("Originals", "Overwrite")][InlineData("All", "Overwrite")]
    public void CompletedCurrentFactsAdvanceBeyondTheFrozenUnknownSnapshot(string scope, string kind)
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation(true, scope, kind);
        AdmitWithoutResponse(original); original.Version = 17;
        var current = Clone(original); current.Version++;
        AttachReceipt(current, fixture, "Completed", Selects(scope, "Archive"), Selects(scope, "Originals"));
        var summary = FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original);
        Assert.Equal("Succeeded", summary.State); Assert.Equal(18, summary.Version); Assert.Equal(true, summary.DeletionStarted);
        Assert.Equal(Selects(scope, "Archive"), summary.ArchiveRemoved); Assert.Equal(Selects(scope, "Originals"), summary.OriginalsRemoved);
        Assert.Equal("Unknown", original.State); Assert.Null(original.ReceiptJson);
    }

    [Theory]
    [InlineData("Archive")][InlineData("Originals")][InlineData("All")]
    public void AcknowledgmentAdvancesCurrentStateWithoutRewritingTheSignedUnknownFacts(string scope)
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation(true, scope);
        AttachReceipt(original, fixture, "Unknown", false, Selects(scope, "Originals")); original.Version = 17;
        var current = Clone(original); current.State = "Acknowledged"; current.Version++;
        current.CompletedAtUtc = current.ReceiptObservedAtUtc;
        var summary = FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original);
        Assert.Equal("Acknowledged", summary.State); Assert.Equal(18, summary.Version); Assert.Equal(true, summary.DeletionStarted);
        Assert.Equal(scope == "Originals" ? false : (bool?)null, summary.ArchiveRemoved);
        Assert.Equal(Selects(scope, "Originals"), summary.OriginalsRemoved);
        Assert.Equal(original.ReceiptHash, current.ReceiptHash); Assert.Equal(original.ReceiptObservedAtUtc, current.ReceiptObservedAtUtc);
    }

    [Theory]
    [InlineData("IntentId")][InlineData("Actor")][InlineData("CaptureId")][InlineData("CaptureKind")]
    [InlineData("Scope")][InlineData("Source")][InlineData("RequestTime")][InlineData("PlanFingerprint")]
    [InlineData("ReadyExpiry")][InlineData("ExecutionExpiry")][InlineData("RootStarted")]
    public void IndependentlyValidButDifferentImmutableSnapshotsCannotSupplyCurrentFacts(string field)
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation();
        AdmitWithoutResponse(original); original.Version = 17;
        var current = Clone(original); current.Version++;
        var command = FileTreePurgingValidation.ReadCommand(current); var plan = FileTreePurgingValidation.ReadPlan(current);
        switch (field)
        {
            case "IntentId":
                current = Change(current, nameof(current.IdempotencyKey), Guid.NewGuid().ToString("D")); current = Reidentify(current); break;
            case "Actor":
                current = Change(current, nameof(current.ActorSubject), "another-valid-admin"); current = Reidentify(current); break;
            case "CaptureId":
                current = Change(current, nameof(current.CaptureId), Guid.NewGuid()); command = command with { CaptureId = current.CaptureId }; break;
            case "CaptureKind": command = command with { CaptureKind = "Overwrite" }; break;
            case "Scope": command = command with { Scope = "Originals" }; break;
            case "Source": command = command with { Source = new("test.shared", "another-directory") }; break;
            case "RequestTime":
                current = Change(current, nameof(current.CreatedAtUtc), current.CreatedAtUtc.AddMicroseconds(1));
                current = Change(current, nameof(current.PlanningExpiresAtUtc), current.CreatedAtUtc.AddMinutes(15)); break;
            case "PlanFingerprint": plan = plan with { Fingerprint = new('8', 64) }; break;
            case "ReadyExpiry": current.ReadyExpiresAtUtc = current.ReadyExpiresAtUtc!.Value.AddMicroseconds(1); break;
            case "ExecutionExpiry": current.ExecutionExpiresAtUtc = current.ExecutionExpiresAtUtc!.Value.AddMicroseconds(-1); break;
            case "RootStarted": current.RootStartedAtUtc = current.RootStartedAtUtc!.Value.AddMicroseconds(1); break;
        }
        current = Change(current, nameof(current.CommandJson), JsonSerializer.Serialize(command));
        current.PlanJson = JsonSerializer.Serialize(plan with { Command = command });
        current = Change(current, nameof(current.SourceHash), new FilePathScope(command.Source).Hash);
        current = Change(current, nameof(current.OwnerHash), FileManagementService.Hash("file-owner-v1:" + current.ActorSubject + ":" + current.LoginBinding));
        current = Change(current, nameof(current.RequestHash), fixture.Protector.FingerprintRequest(FileTreePurgingValidation.RequestBinding(current)));
        current.PlanHash = fixture.Protector.FingerprintPlan(FileTreePurgingValidation.PlanBinding(current));
        Assert.True(FileTreePurgingValidation.HasValidBinding(current, fixture.Protector));
        Assert.Equal("FILES_RECOVERY_ORIGINAL_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original)).Message);
    }

    [Fact]
    public void ChangedValidFactsCannotKeepTheFrozenVersion()
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation();
        AdmitWithoutResponse(original); original.Version = 17; var current = Clone(original);
        AttachReceipt(current, fixture, "Unknown", false, true);
        Assert.True(FileTreePurgingValidation.HasValidBinding(current, fixture.Protector));
        Assert.Equal("FILES_RECOVERY_ORIGINAL_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original)).Message);
    }
    [Fact]
    public void AnOtherwiseValidCurrentVersionCannotGoBackwards()
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation();
        AdmitWithoutResponse(original); original.Version = 17;
        var current = Clone(original); current.Version = 16;
        Assert.True(FileTreePurgingValidation.HasValidBinding(current, fixture.Protector));
        Assert.Equal("FILES_RECOVERY_ORIGINAL_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original)).Message);
    }

    [Theory]
    [InlineData("Facts")][InlineData("ObservationTime")][InlineData("MissingReceipt")]
    public void AnAlreadyObservedReceiptCannotBeReplacedEvenByAnotherValidSignedObservation(string change)
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation();
        AttachReceipt(original, fixture, "Unknown", false, true); original.Version = 17;
        var current = Clone(original); current.Version++;
        if (change == "Facts") AttachReceipt(current, fixture, "Unknown", true, true);
        else if (change == "ObservationTime")
        {
            current.ReceiptObservedAtUtc = current.ReceiptObservedAtUtc!.Value.AddMicroseconds(1);
            current.ReceiptHash = fixture.Protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(current));
        }
        else
        {
            current.ReceiptJson = null; current.ReceiptHash = null; current.ReceiptObservedAtUtc = null;
            current.DeletionStarted = false; current.ArchiveRemoved = false; current.OriginalsRemoved = false;
        }
        Assert.True(FileTreePurgingValidation.HasValidBinding(current, fixture.Protector));
        Assert.Equal("FILES_RECOVERY_ORIGINAL_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original)).Message);
    }

    [Theory]
    [InlineData("RequestHash")][InlineData("PlanHash")][InlineData("ReceiptHash")]
    public void TamperedCurrentMacsCannotBecomePublicFacts(string field)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation();
        AttachReceipt(row, fixture, "Unknown", false, true);
        if (field == "RequestHash") row = Change(row, nameof(row.RequestHash), new string('8', 64));
        else if (field == "PlanHash") row.PlanHash = new('8', 64);
        else row.ReceiptHash = new('8', 64);
        Assert.Equal("FILES_INTENT_BINDING_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(row, fixture.Protector)).Message);
    }

    [Fact]
    public void ACurrentValidRowStillRejectsAnUnauthenticatedOriginalSnapshot()
    {
        using var fixture = new FileTreePurgingBindingTests(); var original = fixture.Operation();
        AdmitWithoutResponse(original); var current = Clone(original); current.Version++;
        original = Change(original, nameof(original.RequestHash), new string('8', 64));
        Assert.Equal("FILES_RECOVERY_ORIGINAL_CHANGED", Assert.Throws<InvalidOperationException>(() =>
            FileTreePurgingValidation.RecoverySummary(current, fixture.Protector, original)).Message);
    }

    [Fact]
    public void CandidateAndIntentNestOnlyTheEightSafeCurrentFieldsAndOmitAbsentPurge()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(true, "All", "Overwrite");
        AttachReceipt(row, fixture, "Unknown", false, true); row.Version = 17;
        var summary = FileTreePurgingValidation.RecoverySummary(row, fixture.Protector);
        var candidate = new FileRecoveryCandidateDto(FileRecoveryKinds.TreePurging, row.Id, row.ExecutionJobId,
            row.Version, row.CreatedAtUtc, null, true, "Verified", null, null, null, summary);
        var intent = new FileRecoveryIntentDto(Guid.NewGuid(), Guid.NewGuid(), null, FileRecoveryKinds.TreePurging, row.Id,
            row.ExecutionJobId!.Value, row.Version, "Planning", "AcknowledgeUnknown", "OriginalSessionLost", null,
            null, 0, null, null, null, summary);
        foreach (var json in new[] { JsonSerializer.Serialize(candidate, JsonSerializerOptions.Web), JsonSerializer.Serialize(intent, JsonSerializerOptions.Web) })
        {
            using var document = JsonDocument.Parse(json); var purge = document.RootElement.GetProperty("purge");
            Assert.Equal(ExpectedPublicFields,
                purge.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal(row.CaptureId, purge.GetProperty("captureId").GetGuid()); Assert.Equal("Overwrite", purge.GetProperty("captureKind").GetString());
            Assert.Equal(17, purge.GetProperty("version").GetInt64()); Assert.Equal("Unknown", purge.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, purge.GetProperty("archiveRemoved").ValueKind); Assert.True(purge.GetProperty("originalsRemoved").GetBoolean());
            Assert.DoesNotContain(row.ActorSubject, json, StringComparison.Ordinal);
            Assert.DoesNotContain(row.OwnerHash, json, StringComparison.Ordinal);
            Assert.DoesNotContain(row.RequestHash, json, StringComparison.Ordinal);
            Assert.DoesNotContain(FileTreePurgingValidation.ReadCommand(row).CaptureOwner, json, StringComparison.Ordinal);
        }
        using var ordinaryCandidate = JsonDocument.Parse(JsonSerializer.Serialize(candidate with { Purge = null }, JsonSerializerOptions.Web));
        using var ordinaryIntent = JsonDocument.Parse(JsonSerializer.Serialize(intent with { Purge = null }, JsonSerializerOptions.Web));
        Assert.False(ordinaryCandidate.RootElement.TryGetProperty("purge", out _));
        Assert.False(ordinaryIntent.RootElement.TryGetProperty("purge", out _));
    }

    private static bool Selects(string scope, string component) => scope == "All" || scope == component;
    private static FileTreePurgingOperation Clone(FileTreePurgingOperation row) => JsonSerializer.Deserialize<FileTreePurgingOperation>(JsonSerializer.Serialize(row))!;
    private static FileTreePurgingOperation Change<T>(FileTreePurgingOperation row, string property, T value)
    {
        var node = JsonSerializer.SerializeToNode(row)!; node[property] = JsonSerializer.SerializeToNode(value);
        return node.Deserialize<FileTreePurgingOperation>()!;
    }
    private static void AdmitWithoutResponse(FileTreePurgingOperation row)
    { row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.State = "Unknown"; }
    private static FileTreePurgingOperation Reidentify(FileTreePurgingOperation row)
    {
        var next = Change(row, nameof(row.Id), FileTreePurgingValidation.IntentId(new(row.ActorSubject, null, [], row.LoginBinding, "summary-test", null), row.IdempotencyKey));
        next.ExecutionJobId = FileTreePurgingValidation.ExecutionId(next.Id); return next;
    }
    private static void AttachReceipt(FileTreePurgingOperation row, FileTreePurgingBindingTests fixture, string state, bool archiveRemoved, bool originalsRemoved)
    {
        var plan = FileTreePurgingValidation.ReadPlan(row);
        var receipt = new FileTreePurgingReceipt(row.ExecutionJobId!.Value, "DirectoryPurge", state, plan.Fingerprint, row.CaptureId, plan.Command.Scope,
            plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, true, archiveRemoved, originalsRemoved);
        row.RootStartedAtUtc ??= row.CreatedAtUtc.AddSeconds(1); row.State = state == "Completed" ? "Succeeded" : "Unknown";
        row.DeletionStarted = true; row.ArchiveRemoved = archiveRemoved; row.OriginalsRemoved = originalsRemoved;
        row.ReceiptJson = JsonSerializer.Serialize(receipt); row.ReceiptObservedAtUtc = row.CreatedAtUtc.AddSeconds(2);
        row.ReceiptHash = fixture.Protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
    }
}
