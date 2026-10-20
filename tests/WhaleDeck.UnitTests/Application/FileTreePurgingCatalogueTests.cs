using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Authenticates immutable historical capture material, not a database or filesystem acceptance.</summary>
public sealed class FileTreePurgingCatalogueTests
{
    private static FileTreeCapture Capture(FileTreePurgingBindingTests fixture, string kind)
    {
        var plan = FileTreePurgingBindingTests.Plan(kind: kind); var source = new FileTreeRecyclingCommand(plan.Command.Source, plan.Command.ExpectedVersion);
        var intent = Guid.Parse("4209812d-ea2e-46bb-9363-24fe30013a90");
        var id = kind == "Trash" ? FileTreeRecyclingValidation.ExecutionId(intent) : FileTreeOverwriteRestoringValidation.ExecutionId(intent);
        var scope = new FilePathScope(source.Source).Hash; var created = FileTreePurgingValidation.Timestamp(DateTimeOffset.UtcNow);
        var proof = kind == "Trash" ? fixture.Protector.FingerprintCapture(new FileTreeCaptureBinding(id, intent, plan.Command.CaptureOwner,
            scope, source, plan.Command.ManifestSha256, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, created)) :
            fixture.Protector.FingerprintCapture(new FileTreeOverwrittenCaptureBinding(id, intent, plan.Command.CaptureOwner,
                scope, source, plan.Command.ManifestSha256, plan.ContentSha256, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, plan.ArchiveSha256, created));
        return new() { Id = id, IntentId = intent, Kind = kind, ContentSha256 = kind == "Trash" ? null : plan.ContentSha256,
            OwnerHash = plan.Command.CaptureOwner, SourceHash = scope, SourceJson = JsonSerializer.Serialize(source),
            ManifestSha256 = plan.Command.ManifestSha256, EntryCount = plan.EntryCount, TotalBytes = plan.TotalBytes,
            ArchiveBytes = plan.ArchiveBytes, ArchiveSha256 = plan.ArchiveSha256, ProofHash = proof, CreatedAtUtc = created };
    }
    [Theory]
    [InlineData("Trash", "Archive")][InlineData("Trash", "Originals")][InlineData("Trash", "All")]
    [InlineData("Overwrite", "Archive")][InlineData("Overwrite", "Originals")][InlineData("Overwrite", "All")]
    public void CommandIsResolvedFromAuthenticatedCatalogueNotCurrentOwnerOrSubmittedPaths(string kind, string scope)
    {
        using var fixture = new FileTreePurgingBindingTests(); var capture = Capture(fixture, kind);
        var command = FileTreePurgingValidation.CaptureCommand(new(capture.Id, scope), capture, fixture.Protector, fixture.Protector);
        Assert.Equal(capture.Id, command.CaptureId); Assert.Equal(capture.OwnerHash, command.CaptureOwner);
        Assert.Equal(capture.ProofHash, command.CatalogueProofHash); Assert.Equal(kind, command.CaptureKind); Assert.Equal(scope, command.Scope);
        var original = JsonSerializer.Deserialize<FileTreeRecyclingCommand>(capture.SourceJson)!;
        Assert.Equal(original.Source, command.Source); Assert.Equal(original.ExpectedVersion, command.ExpectedVersion);
        FileTreePurgingValidation.Capture(FileTreePurgingBindingTests.Plan(scope, kind) with { Command = command }, capture, fixture.Protector, fixture.Protector);
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingValidation.CaptureCommand(new(Guid.NewGuid(), scope), capture, fixture.Protector, fixture.Protector));
    }
    [Theory]
    [InlineData("Trash", "OwnerHash")][InlineData("Overwrite", "OwnerHash")][InlineData("Trash", "SourceHash")][InlineData("Overwrite", "SourceHash")]
    [InlineData("Trash", "ManifestSha256")][InlineData("Overwrite", "ContentSha256")][InlineData("Trash", "ArchiveSha256")][InlineData("Overwrite", "ProofHash")]
    public void InvalidHistoricalCatalogueProofCannotAuthorizeCleanup(string kind, string field)
    {
        using var fixture = new FileTreePurgingBindingTests(); var capture = Capture(fixture, kind);
        var json = JsonSerializer.SerializeToNode(capture)!.AsObject(); json[field] = new string('8', 64);
        var changed = json.Deserialize<FileTreeCapture>()!;
        Assert.Throws<FileTransferRejectedException>(() => FileTreePurgingValidation.CaptureCommand(new(changed.Id, "All"), changed, fixture.Protector, fixture.Protector));
    }
    [Theory]
    [InlineData("command")][InlineData("entries")][InlineData("bytes")][InlineData("archiveBytes")][InlineData("archiveSha")][InlineData("content")]
    public void ReadyPlanCannotLoseCatalogueBindingOrArchiveContent(string field)
    {
        using var fixture = new FileTreePurgingBindingTests(); var capture = Capture(fixture, "Overwrite");
        var command = FileTreePurgingValidation.CaptureCommand(new(capture.Id, "All"), capture, fixture.Protector, fixture.Protector);
        var plan = FileTreePurgingBindingTests.Plan(kind: "Overwrite") with { Command = command };
        plan = field switch { "command" => plan with { Command = command with { CatalogueProofHash = new('8', 64) } },
            "entries" => plan with { EntryCount = plan.EntryCount - 1 }, "bytes" => plan with { TotalBytes = plan.TotalBytes - 1 },
            "archiveBytes" => plan with { ArchiveBytes = plan.ArchiveBytes - 1 }, "archiveSha" => plan with { ArchiveSha256 = new('8', 64) },
            _ => plan with { ContentSha256 = new('8', 64) } };
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingValidation.Capture(plan, capture, fixture.Protector, fixture.Protector));
    }
}
