using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreeRecyclingBindingTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-recycle-seal-" + Guid.NewGuid().ToString("N"));
    public FilePlanProtector Protector { get; }
    public FileTreeRecyclingBindingTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    public static FileTreeRecyclingPlan Plan() => new(new(new("test.tree", "代码/原目录"), new('A', 64)), new(ulong.MaxValue, 9007199254740993),
        new('B', 64), new('C', 64), 4, FileManagementService.MaximumBytes + 7, new('D', 64));
    public FileTreeRecyclingOperation Operation(bool execution = true)
    {
        var plan = Plan(); var actor = new TerminalActor("recycle-admin", null, [], new('E', 64), "test", null); var key = Guid.NewGuid().ToString("D");
        var id = FileTreeRecyclingValidation.IntentId(actor, key); var now = FileTreeRecyclingValidation.Timestamp(DateTimeOffset.UtcNow); var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = Protector.FingerprintRequest(new FileTreeRecyclingRequestBinding(id, actor.Subject, actor.LoginBinding, plan.Command, expiry)),
            SourceHash = new FilePathScope(plan.Command.Source).Hash, CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = Protector.FingerprintPlan(new FileTreeRecyclingPlanBinding(id, actor.Subject, actor.LoginBinding, plan, ready)),
            ReadyExpiresAtUtc = ready, ExecutionJobId = execution ? FileTreeRecyclingValidation.ExecutionId(id) : null, ExecutionExpiresAtUtc = execution ? expiry : null,
            State = execution ? "RecyclingQueued" : "Ready", LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
    }
    [Fact]
    public void WholeTreeProofRoundtripKeepsUInt64AndIndependentIntentExecutionDomains()
    {
        var plan = Plan(); FileTreeRecyclingValidation.Plan(plan);
        var decoded = JsonSerializer.Deserialize<FileTreeRecyclingPlan>(JsonSerializer.Serialize(plan), FileTreeRecyclingValidation.JsonOptions);
        Assert.Equal(plan, decoded); Assert.Equal(ulong.MaxValue, decoded!.SourceParent.Device); Assert.Equal(9007199254740993UL, decoded.SourceParent.Inode);
        var row = Operation(); Assert.True(FileTreeRecyclingValidation.HasValidBinding(row, Protector)); Assert.NotEqual(row.Id, row.ExecutionJobId);
        var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, "", null);
        Assert.NotEqual(FileTreeUnpackingValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreeMoveValidation.ExecutionId(row.Id), row.ExecutionJobId);
    }
    [Fact]
    public void WholeRootCatalogueNeverConstructsOrWeakensAMutatingPathScope()
    {
        var root = new FileLocationDto("test.tree", "");
        Assert.Null(FileTreeRecyclingValidation.CaptureSourceHash(root));
        Assert.Throws<ArgumentException>(() => new FilePathScope(root));
        Assert.Throws<ArgumentException>(() => FileTreeRecyclingValidation.Command(Plan().Command with { Source = root }));
        var source = Plan().Command.Source;
        Assert.Equal(new FilePathScope(source).Hash, FileTreeRecyclingValidation.CaptureSourceHash(source));
    }
    [Theory]
    [InlineData("../escape")][InlineData("/absolute")][InlineData("a//b")][InlineData("a\\b")]
    public void CatalogueStillRejectsInvalidPaths(string path) =>
        Assert.Throws<ArgumentException>(() => FileTreeRecyclingValidation.CaptureSourceHash(new("test.tree", path)));
    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a//b")]
    public void InvalidPathsDoNotBecomeRootRequests(string path) => Assert.Throws<ArgumentException>(() => FileTreeRecyclingValidation.Command(Plan().Command with { Source = new("test.tree", path) }));
    [Fact]
    public void ConfirmationCannotBeReusedFromAnotherOperationFamilyOrChangedPlan()
    {
        var row = Operation(); var claims = new FileTreeRecyclingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        var token = Protector.SignConfirmation(claims);
        var other = Protector.SignConfirmation(new FileTreeUnpackingConfirmation(claims.Id, claims.Actor, claims.LoginBinding, claims.Fingerprint, claims.ExpiresAtUtc));
        Assert.Equal(token.Split('.')[0], other.Split('.')[0]); Assert.NotEqual(token, other);
        Assert.Throws<ArgumentException>(() => Protector.VerifyRecyclingConfirmation(other));
        Assert.Throws<ArgumentException>(() => Protector.VerifyUnpackingConfirmation(token));
        Assert.Equal(claims, Protector.VerifyRecyclingConfirmation(token));
        foreach (var changed in new[] { Plan() with { SourceParent = new(1, 2) }, Plan() with { ManifestSha256 = new('0', 64) }, Plan() with { TotalBytes = 1 } })
        { row.PlanJson = JsonSerializer.Serialize(changed); Assert.False(FileTreeRecyclingValidation.HasValidBinding(row, Protector)); }
        row.PlanJson = JsonSerializer.Serialize(Plan()); row.ExecutionJobId = Guid.NewGuid(); Assert.False(FileTreeRecyclingValidation.HasValidBinding(row, Protector));
    }
    [Theory]
    [InlineData("Completed", true, true)][InlineData("Unknown", true, true)][InlineData("Unknown", false, false)][InlineData("Aborted", false, false)][InlineData("Aborted", true, false)]
    public void CaptureVerificationAndSourceChangesAreDistinctFacts(string state, bool verified, bool changed)
    {
        var id = Guid.NewGuid(); var plan = Plan(); var result = new FileTreeRecyclingReceipt(id, state, plan.Fingerprint, id, plan.ManifestSha256, plan.EntryCount, plan.TotalBytes,
            verified, verified, verified ? 1234 : 0, verified ? new('F', 64) : "", changed, state == "Completed", state == "Completed");
        FileTreeRecyclingValidation.Receipt(id, plan, result);
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Receipt(id, plan, result with { CaptureId = Guid.NewGuid() }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Receipt(id, plan, result with { ManifestSha256 = new('0', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Receipt(id, plan, result with { ArchiveBytes = verified ? 0 : 1 }));
    }
    [Fact]
    public void CatalogueAndOperationProofBindOldOwnerSourceEntireManifestAndArchive()
    {
        var row = Operation(); var plan = Plan(); var created = row.CreatedAtUtc.AddSeconds(1);
        var binding = new FileTreeCaptureBinding(row.ExecutionJobId!.Value, row.Id, row.OwnerHash, row.SourceHash, plan.Command, plan.ManifestSha256, plan.EntryCount, plan.TotalBytes, 1234, new('F', 64), created);
        var proof = Protector.FingerprintCapture(binding);
        var capture = new FileTreeCapture { Id = binding.Id, IntentId = row.Id, OwnerHash = row.OwnerHash, SourceHash = row.SourceHash, SourceJson = row.CommandJson,
            ManifestSha256 = plan.ManifestSha256, EntryCount = plan.EntryCount, TotalBytes = plan.TotalBytes, ArchiveBytes = binding.ArchiveBytes, ArchiveSha256 = binding.ArchiveSha256, ProofHash = proof, CreatedAtUtc = created };
        FileTreeRecyclingValidation.Capture(capture, Protector);
        row.CaptureId = binding.Id; row.CaptureHash = proof; row.CaptureCreatedAtUtc = created; row.CaptureVerified = true; row.CaptureMayBeRetained = true; row.ArchiveBytes = binding.ArchiveBytes; row.ArchiveSha256 = binding.ArchiveSha256;
        Assert.True(FileTreeRecyclingValidation.HasValidBinding(row, Protector));
        row.ArchiveBytes++; Assert.False(FileTreeRecyclingValidation.HasValidBinding(row, Protector)); row.ArchiveBytes--;
        row.CaptureCreatedAtUtc = created.AddSeconds(1); Assert.False(FileTreeRecyclingValidation.HasValidBinding(row, Protector)); row.CaptureCreatedAtUtc = created;
        row.CaptureHash = new('0', 64); Assert.False(FileTreeRecyclingValidation.HasValidBinding(row, Protector));
        using var otherKey = new FileTreeRecyclingBindingTests(); Assert.Throws<FileTransferRejectedException>(() => FileTreeRecyclingValidation.Capture(capture, otherKey.Protector));
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Aborted")][InlineData("Completed")]
    public void CurrentAdministratorRecoveryFreezesOriginalMaterialAndValidatesIndependentReceipt(string state)
    {
        var row = Operation(); row.State = "Unknown"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var material = new FileRecoveryMaterial(Protector, Protector, Protector, Protector, Protector, Protector, Protector, Protector);
        var snapshot = material.Capture(row); var plan = Plan(); var verified = state != "Aborted";
        var receipt = new FileTreeRecyclingReceipt(row.ExecutionJobId!.Value, state, plan.Fingerprint, row.ExecutionJobId.Value, plan.ManifestSha256,
            plan.EntryCount, plan.TotalBytes, verified, verified, verified ? 1234 : 0, verified ? new('F', 64) : "", verified, state == "Completed", state == "Completed");
        FileRecoveryObservation Observation(FileTreeRecyclingReceipt result) => new(result.State,
            FileRecoveryValidation.ReceiptHash(FileRecoveryKinds.TreeRecycling, result.OperationId, result.State, JsonSerializer.Serialize(result)), result.State == "Completed") { ReceiptJson = JsonSerializer.Serialize(result) };
        Assert.Equal(receipt, material.ValidateObservation(snapshot, Observation(receipt)));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observation(receipt with { CaptureId = Guid.NewGuid() })));
        Assert.Throws<FileTransferRejectedException>(() => material.ValidateObservation(snapshot, Observation(receipt with { ArchiveBytes = verified ? 0 : 1 })));
        Assert.Throws<InvalidOperationException>(() => material.Read<FileTreeMoveOperation>(snapshot));
        Assert.Equal(7, FileRecoveryValidation.Rank(FileRecoveryKinds.TreeRecycling));
        Assert.Equal(FileTreeRecyclingValidation.ExecutionId(row.Id), snapshot.Binding.ExecutionJobId);
    }
}
