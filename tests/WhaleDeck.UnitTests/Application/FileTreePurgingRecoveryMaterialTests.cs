using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreePurgingRecoveryMaterialTests : IDisposable
{
    private readonly FileTreePurgingBindingTests _fixture = new();
    public void Dispose() => _fixture.Dispose();
    private FileRecoveryMaterial Material(bool authorized = true) => new(_fixture.Protector, _fixture.Protector, _fixture.Protector, _fixture.Protector,
        _fixture.Protector, _fixture.Protector, _fixture.Protector, _fixture.Protector, _fixture.Protector, _fixture.Protector, authorized ? _fixture.Protector : null);
    private FileTreePurgingOperation Original(string scope = "All")
    {
        var row = _fixture.Operation(scope: scope); row.State = "Unknown"; row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.Version = 17;
        Assert.True(FileTreePurgingValidation.HasValidBinding(row, _fixture.Protector)); return row;
    }
    private static FileTreePurgingReceipt Receipt(FileTreePurgingOperation row, string state)
    {
        var plan = FileTreePurgingValidation.ReadPlan(row);
        return new(row.ExecutionJobId!.Value, "DirectoryPurge", state, plan.Fingerprint, row.CaptureId, plan.Command.Scope, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes,
            state != "Aborted", state == "Completed" && FileTreePurgingValidation.Selects(plan.Command.Scope, "Archive"),
            state != "Aborted" && FileTreePurgingValidation.Selects(plan.Command.Scope, "Originals"));
    }
    private static FileRecoveryObservation Observe(FileRecoverySnapshot snapshot, FileTreePurgingReceipt receipt)
    {
        var json = JsonSerializer.Serialize(receipt);
        return new(receipt.State, FileRecoveryValidation.ReceiptHash(snapshot.Kind, snapshot.Binding.ExecutionJobId, receipt.State, json), receipt.State == "Completed") { ReceiptJson = json };
    }
    private static FileRecoveryObservation Missing(FileRecoverySnapshot snapshot) => new("Missing",
        FileRecoveryValidation.ReceiptHash(snapshot.Kind, snapshot.Binding.ExecutionJobId, "Missing", "null"), false) { ReceiptJson = "null" };

    [Theory]
    [InlineData("Archive")][InlineData("Originals")][InlineData("All")]
    public void EleventhKindFreezesExactOriginalWithoutGrantingOldHumanAuthority(string scope)
    {
        var row = Original(scope); var material = Material(); var snapshot = material.Capture(row);
        Assert.Equal(FileRecoveryKinds.TreePurging, snapshot.Kind); Assert.Equal(10, FileRecoveryValidation.Rank(snapshot.Kind));
        Assert.Equal(JsonSerializer.Serialize(row), snapshot.OriginalJson); Assert.Equal(row.Version, snapshot.Binding.OriginalVersion);
        Assert.Equal(row.PlanJson, material.Read<FileTreePurgingOperation>(snapshot).PlanJson);
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreeRestoringOperation>(snapshot));
        Assert.Throws<InvalidOperationException>(() => Material(false).Capture(row));
        Assert.Throws<InvalidOperationException>(() => Material(false).Read<FileTreePurgingOperation>(snapshot));
    }
    [Theory]
    [InlineData("Completed")][InlineData("Aborted")][InlineData("Unknown")]
    public void RootObservationIsExactAndOnlyCompletedCanReconcileAsSuccess(string state)
    {
        var row = Original(); var material = Material(); var snapshot = material.Capture(row); var receipt = Receipt(row, state);
        Assert.Equal(receipt, material.ValidateObservation(snapshot, Observe(snapshot, receipt)));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observe(snapshot, receipt) with { BindingCurrent = state != "Completed" }));
        Assert.Null(material.ValidateObservation(snapshot, Missing(snapshot)));
    }
    [Fact]
    public void PreviouslySignedUnknownReceiptCannotVanishChangeOrTurnIntoCompletion()
    {
        var row = Original(); var unknown = Receipt(row, "Unknown"); row.ReceiptJson = JsonSerializer.Serialize(unknown);
        row.ReceiptObservedAtUtc = row.CreatedAtUtc.AddSeconds(2); row.DeletionStarted = unknown.DeletionStarted;
        row.ArchiveRemoved = unknown.ArchiveRemoved; row.OriginalsRemoved = unknown.OriginalsRemoved;
        row.ReceiptHash = _fixture.Protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
        var material = Material(); var snapshot = material.Capture(row);
        Assert.Equal(unknown, material.ValidateObservation(snapshot, Observe(snapshot, unknown)));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Missing(snapshot)));
        Assert.Throws<InvalidOperationException>(() => material.ValidateObservation(snapshot, Observe(snapshot, Receipt(row, "Completed"))));
        Assert.Throws<InvalidOperationException>(() => material.ValidateObservation(snapshot, Observe(snapshot, unknown with { OriginalsRemoved = false })));
    }
    [Theory]
    [InlineData("RequestHash")][InlineData("PlanHash")][InlineData("OwnerHash")][InlineData("CaptureId")][InlineData("ReceiptHash")]
    public void FrozenOriginalCannotBeReboundEvenIfTargetDigestIsRecomputed(string field)
    {
        var material = Material(); var snapshot = material.Capture(Original()); var node = JsonNode.Parse(snapshot.OriginalJson)!.AsObject();
        node[field] = field == "CaptureId" ? Guid.NewGuid().ToString("D") : new string('9', 64);
        Assert.ThrowsAny<Exception>(() => material.Read<FileTreePurgingOperation>(new(snapshot.Kind, node.ToJsonString(), snapshot.Binding)));
    }
    [Theory]
    [InlineData("Operation")][InlineData("Capture")][InlineData("Scope")][InlineData("Kind")][InlineData("Fingerprint")][InlineData("Count")][InlineData("Size")]
    public void RehashedRootReceiptStillCannotSubstituteAnotherResourceOrPlan(string field)
    {
        var material = Material(); var row = Original(); var snapshot = material.Capture(row); var receipt = Receipt(row, "Completed");
        var changed = field switch {
            "Operation" => receipt with { OperationId = Guid.NewGuid() }, "Capture" => receipt with { CaptureId = Guid.NewGuid() },
            "Scope" => receipt with { Scope = "Originals" }, "Kind" => receipt with { Kind = "DirectoryRestore" },
            "Fingerprint" => receipt with { Fingerprint = new('9', 64) }, "Count" => receipt with { EntryCount = receipt.EntryCount - 1 },
            _ => receipt with { TotalBytes = receipt.TotalBytes - 1 } };
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observe(snapshot, changed)));
    }
}
