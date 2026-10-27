using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Only original rows under the shared transaction gate can become recovery targets.</summary>
public sealed partial class FileRecoverySources(PlatformDbContext db, FileRecoveryMaterial material, TimeProvider clock)
{
    public async Task<FileRecoverySnapshot> LockAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_RECOVERY_TRANSACTION_REQUIRED");
        FileRecoveryValidation.Kind(kind);
        FileRecoverySnapshot snapshot; DateTimeOffset? lease; string originalActor;
        switch (kind)
        {
            case FileRecoveryKinds.ContainerUpload:
                var container = await db.ContainerFileUploadOperations.FromSqlInterpolated($"SELECT * FROM container_file_upload_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(container); lease = container.LeaseExpiresAtUtc; originalActor = container.ActorSubject; break;
            case FileRecoveryKinds.Upload:
                var upload = await db.FileUploadOperations.FromSqlInterpolated($"SELECT * FROM file_upload_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(upload); lease = upload.LeaseExpiresAtUtc; originalActor = upload.ActorSubject; break;
            case FileRecoveryKinds.Mutation:
                var mutation = await db.FileMutationOperations.FromSqlInterpolated($"SELECT * FROM file_mutation_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(mutation); lease = mutation.LeaseExpiresAtUtc; originalActor = mutation.ActorSubject; break;
            case FileRecoveryKinds.Archive:
                var archive = await db.FileArchiveOperations.FromSqlInterpolated($"SELECT * FROM file_archive_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(archive); lease = archive.LeaseExpiresAtUtc; originalActor = archive.ActorSubject; break;
            case FileRecoveryKinds.TreeCopy:
                var copy = await db.FileTreeCopyOperations.FromSqlInterpolated($"SELECT * FROM file_tree_copy_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(copy); lease = copy.LeaseExpiresAtUtc; originalActor = copy.ActorSubject; break;
            case FileRecoveryKinds.TreePacking:
                var packing = await db.FileTreePackingOperations.FromSqlInterpolated($"SELECT * FROM file_tree_packing_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(packing); lease = packing.LeaseExpiresAtUtc; originalActor = packing.ActorSubject; break;
            case FileRecoveryKinds.TreeUnpacking:
                var unpacking = await db.FileTreeUnpackingOperations.FromSqlInterpolated($"SELECT * FROM file_tree_unpacking_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(unpacking); lease = unpacking.LeaseExpiresAtUtc; originalActor = unpacking.ActorSubject; break;
            case FileRecoveryKinds.TreeRecycling:
                var recycling = await db.FileTreeRecyclingOperations.FromSqlInterpolated($"SELECT * FROM file_tree_recycling_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(recycling); lease = recycling.LeaseExpiresAtUtc; originalActor = recycling.ActorSubject; break;
            case FileRecoveryKinds.TreeRestoring:
                var restoring = await db.FileTreeRestoringOperations.FromSqlInterpolated($"SELECT * FROM file_tree_restoring_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(restoring); lease = restoring.LeaseExpiresAtUtc; originalActor = restoring.ActorSubject; break;
            case FileRecoveryKinds.TreeOverwriteRestoring:
                var overwrite = await db.FileTreeOverwriteRestoringOperations.FromSqlInterpolated($"SELECT * FROM file_tree_overwrite_restoring_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(overwrite); lease = overwrite.LeaseExpiresAtUtc; originalActor = overwrite.ActorSubject; break;
            case FileRecoveryKinds.TreePurging:
                var purging = await db.FileTreePurgingOperations.FromSqlInterpolated($"SELECT * FROM file_tree_purging_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(purging); lease = purging.LeaseExpiresAtUtc; originalActor = purging.ActorSubject; break;
            case FileRecoveryKinds.TreeMove:
                var move = await db.FileTreeMoveOperations.FromSqlInterpolated($"SELECT * FROM file_tree_move_operations WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw Missing();
                snapshot = material.Capture(move); lease = move.LeaseExpiresAtUtc; originalActor = move.ActorSubject; break;
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
        if (lease > clock.GetUtcNow()) throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_BUSY");
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == snapshot.Binding.ExecutionJobId, cancellationToken);
        var expectedType = kind == FileRecoveryKinds.ContainerUpload ? "containers.files.upload" : kind == FileRecoveryKinds.Upload ? "files.upload" : kind == FileRecoveryKinds.Mutation ? "files.mutation" :
            kind == FileRecoveryKinds.Archive ? "files.archive" : kind == FileRecoveryKinds.TreeCopy ? "files.tree-copy" : kind == FileRecoveryKinds.TreePacking ? "files.tree-pack" : kind == FileRecoveryKinds.TreeUnpacking ? "files.tree-unpack" : kind == FileRecoveryKinds.TreeRecycling ? "files.tree-trash" : kind == FileRecoveryKinds.TreeRestoring ? "files.tree-restore" : kind == FileRecoveryKinds.TreeOverwriteRestoring ? "files.tree-overwrite-restore" : kind == FileRecoveryKinds.TreePurging ? "files.tree-purge" : "files.tree-move";
        if (job is null || job.ActorSubject != originalActor || job.JobType != expectedType || job.CompletedAtUtc is not null)
            throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_CHANGED");
        return snapshot;
    }
    public async Task RequireAsync(FileRecoveryOperation operation, CancellationToken cancellationToken)
    {
        var current = await LockAsync(operation.Kind, operation.OriginalIntentId, cancellationToken);
        if (current.Binding != FileRecoveryValidation.Target(operation)) throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_CHANGED");
    }
    private static KeyNotFoundException Missing() => new("FILES_RECOVERY_ORIGINAL_NOT_FOUND");
}
