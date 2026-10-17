using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // Synthetic SQL/transport fixtures in the isolated wdv_dispatch database.
    // They exercise each real disposition adapter without changing the old
    // original-owner regression cases, and do not represent actual file I/O.
    private static async Task<Guid> CloneRecoverySource(DbContextOptions<PlatformDbContext> options, FilePlanProtector protector, string kind, Guid original)
    {
        await using var db = new PlatformDbContext(options); await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341);");
        var material = new FileRecoveryMaterial(protector, protector, protector, protector, protector);
        var snapshot = await new FileRecoverySources(db, material, TimeProvider.System).LockAsync(kind, original, default);
        var node = JsonNode.Parse(snapshot.OriginalJson)!;
        var slug = "native-recovery-clone-" + Guid.NewGuid().ToString("N"); var actor = new TerminalActor("lost-" + slug, null, [], new('A', 64), "native-clone", null);
        var key = Guid.NewGuid().ToString("D"); var now = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow); var expiry = now.AddMinutes(15);
        var id = kind switch { FileRecoveryKinds.TreeCopy => FileTreeCopyValidation.IntentId(actor, key), FileRecoveryKinds.TreeMove => FileTreeMoveValidation.IntentId(actor, key), _ => Guid.NewGuid() };
        node["Id"] = id; node["ActorSubject"] = actor.Subject; node["LoginBinding"] = actor.LoginBinding;
        node["OwnerHash"] = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding);
        node["CreatedAtUtc"] = now; node["RootStartedAtUtc"] = now; node["NextAttemptAtUtc"] = now; node["Version"] = 0;
        node["LeaseToken"] = null; node["LeaseExpiresAtUtc"] = null;
        FileLocationDto Location(string name) => new("test.recovery-clones", slug + "/" + name);
        var locations = new List<FileLocationDto>(); var execution = id;
        switch (kind)
        {
            case FileRecoveryKinds.Upload:
                var upload = material.Read<FileUploadOperation>(snapshot); var target = Location("uploaded.bin"); locations.Add(target);
                node["RootId"] = target.RootId; node["RelativePath"] = target.Path; node["TargetHash"] = FileManagementService.Hash(target.RootId + ":" + target.Path); node["ExpiresAtUtc"] = expiry;
                node["RequestHash"] = protector.Fingerprint(new FileUploadBinding(id, actor.Subject, actor.LoginBinding, new(target.RootId, target.Path, upload.SizeBytes, upload.Sha256, upload.ExpectedVersion), expiry));
                var uploaded = node.Deserialize<FileUploadOperation>()!; material.Capture(uploaded); db.FileUploadOperations.Add(uploaded); break;
            case FileRecoveryKinds.Mutation:
                var mutation = FileMutationService.ReadPlan(material.Read<FileMutationOperation>(snapshot));
                mutation = mutation with { Command = mutation.Command with { Source = mutation.Command.Source is null ? null : Location("source"), Target = mutation.Command.Target is null ? null : Location("target") } };
                if (mutation.Command.Source is not null) locations.Add(mutation.Command.Source); if (mutation.Command.Target is not null) locations.Add(mutation.Command.Target);
                node["PlanJson"] = JsonSerializer.Serialize(mutation); node["ExpiresAtUtc"] = expiry;
                node["RequestHash"] = ((IFileMutationProtector)protector).Fingerprint(new FileMutationBinding(id, actor.Subject, actor.LoginBinding, mutation, expiry));
                var mutated = node.Deserialize<FileMutationOperation>()!; material.Capture(mutated); db.FileMutationOperations.Add(mutated); break;
            case FileRecoveryKinds.Archive:
                var archive = FileArchiveValidation.ReadPlan(material.Read<FileArchiveOperation>(snapshot)); archive = archive with { Command = archive.Command with { Location = Location("archived.bin") } };
                locations.Add(archive.Command.Location); node["PlanJson"] = JsonSerializer.Serialize(archive); node["ExpiresAtUtc"] = expiry;
                node["RootId"] = archive.Command.Location.RootId; node["RelativePath"] = archive.Command.Location.Path; node["TargetHash"] = FileManagementService.Hash(archive.Command.Location.RootId + ":" + archive.Command.Location.Path);
                node["RequestHash"] = ((IFileArchiveProtector)protector).Fingerprint(new FileArchiveBinding(id, actor.Subject, actor.LoginBinding, archive, expiry));
                var archived = node.Deserialize<FileArchiveOperation>()!; material.Capture(archived); db.FileArchiveOperations.Add(archived);
                db.FileArchiveRegistrations.Add(new() { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, RequestHash = archived.RequestHash, PlanJson = archived.PlanJson, CreatedAtUtc = now, ExpiresAtUtc = expiry }); break;
            default:
                var command = new FileTreeCopyCommand(Location("source"), new('B', 64), Location("target")); locations.Add(command.Source); locations.Add(command.Target);
                node["IdempotencyKey"] = key; node["CommandJson"] = JsonSerializer.Serialize(command); node["SourceHash"] = new FilePathScope(command.Source).Hash; node["TargetHash"] = new FilePathScope(command.Target).Hash;
                node["PlanningExpiresAtUtc"] = expiry; node["ExecutionExpiresAtUtc"] = expiry; node["ReadyExpiresAtUtc"] = now.AddMinutes(5);
                if (kind == FileRecoveryKinds.TreeCopy)
                {
                    var plan = FileTreeCopyValidation.ReadPlan(material.Read<FileTreeCopyOperation>(snapshot)) with { Command = command };
                    execution = FileTreeCopyValidation.ExecutionId(id); node["ExecutionJobId"] = execution; node["PlanJson"] = JsonSerializer.Serialize(plan);
                    var copy = node.Deserialize<FileTreeCopyOperation>()!; node["RequestHash"] = protector.FingerprintRequest(FileTreeCopyValidation.RequestBinding(copy)); node["PlanHash"] = protector.FingerprintPlan(FileTreeCopyValidation.PlanBinding(copy));
                    copy = node.Deserialize<FileTreeCopyOperation>()!; material.Capture(copy); db.FileTreeCopyOperations.Add(copy);
                }
                else
                {
                    var plan = FileTreeMoveValidation.ReadPlan(material.Read<FileTreeMoveOperation>(snapshot)); plan = plan with { CopyPlan = plan.CopyPlan with { Command = command } };
                    execution = FileTreeMoveValidation.ExecutionId(id); node["ExecutionJobId"] = execution; node["PlanJson"] = JsonSerializer.Serialize(plan);
                    var move = node.Deserialize<FileTreeMoveOperation>()!; node["RequestHash"] = protector.FingerprintRequest(FileTreeMoveValidation.RequestBinding(move)); node["PlanHash"] = protector.FingerprintPlan(FileTreeMoveValidation.PlanBinding(move));
                    move = node.Deserialize<FileTreeMoveOperation>()!; material.Capture(move); db.FileTreeMoveOperations.Add(move);
                }
                db.OperationJobs.Add(new() { Id = id, ActorSubject = actor.Subject, IdempotencyKey = key, JobType = kind == FileRecoveryKinds.TreeCopy ? "files.tree-copy-plan" : "files.tree-move-plan", State = "Succeeded", CompletedAtUtc = now }); break;
        }
        var jobType = kind == FileRecoveryKinds.Upload ? "files.upload" : kind == FileRecoveryKinds.Mutation ? "files.mutation" :
            kind == FileRecoveryKinds.Archive ? "files.archive" : kind == FileRecoveryKinds.TreeCopy ? "files.tree-copy" : "files.tree-move";
        db.OperationJobs.Add(new() { Id = execution, ActorSubject = actor.Subject, IdempotencyKey = execution.ToString("D"), JobType = jobType, State = "Running" });
        foreach (var location in locations)
            db.FilePathLocks.Add(new() { PathHash = new FilePathScope(location).Hash, ScopeKind = "Path", RootId = location.RootId, RelativePath = location.Path, OperationId = execution });
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return id;
    }
}
