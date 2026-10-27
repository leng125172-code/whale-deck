using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileRecoverySources
{
    public async Task<string> DisposeAsync(FileRecoveryOperation recovery, FileRecoveryObservation observation,
        FileArchiveRepository archiveCatalog, CancellationToken cancellationToken, FileTreeRecyclingRepository? recyclingCatalog = null,
        FileTreeOverwriteRestoringRepository? overwriteCatalog = null, FileTreePurgingRepository? purgingCatalog = null)
    {
        var snapshot = await LockAsync(recovery.Kind, recovery.OriginalIntentId, cancellationToken);
        if (snapshot.Binding != FileRecoveryValidation.Target(recovery)) throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_CHANGED");
        var receipt = material.ValidateObservation(snapshot, observation);
        var complete = recovery.Disposition == "ReconcileCompleted";
        if (complete != (observation.State == "Completed") || !complete && observation.State is not ("Unknown" or "Aborted"))
            throw new InvalidOperationException("FILES_RECOVERY_RECEIPT_CHANGED");
        var now = clock.GetUtcNow(); var state = complete ? observation.BindingCurrent ? "Succeeded" : "Superseded" : "Acknowledged";
        const string code = "FILES_ADMINISTRATOR_RECOVERY_DISPOSED";
        switch (recovery.Kind)
        {
            case FileRecoveryKinds.ContainerUpload:
                if (complete || receipt is not ContainerUploadRecoveryObservationDto { Sealed: true } ||
                    !ContainerUploadRecoveryValidation.MatchesApprovedOrSealed(FileRecoveryValidation.Snapshot(recovery), observation))
                    throw new InvalidOperationException("FILES_RECOVERY_RECEIPT_CHANGED");
                var container = material.Read<ContainerFileUploadOperation>(snapshot); db.Attach(container);
                // The result remains a failed original upload, independently
                // explained by the current administrator's acknowledgment.
                state = "Failed"; container.State = state; container.CompletedAtUtc = now;
                container.ResultCode = "CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED"; container.PublishBarrier = false;
                container.LeaseToken = null; container.LeaseExpiresAtUtc = null; container.Version++; break;
            case FileRecoveryKinds.Upload:
                var upload = material.Read<FileUploadOperation>(snapshot); db.Attach(upload); var uploaded = receipt as FileUploadReceiptDto;
                // The old upload state machine has no Acknowledged state. Its
                // failed original result is explained by the independent audit.
                if (!complete) state = "Failed";
                upload.State = state; upload.CompletedAtUtc = now; upload.ResultCode = code; upload.PublishedVersion = complete ? uploaded!.Version : null;
                upload.PreviousRetained |= uploaded?.PreviousRetained == true; upload.LeaseToken = null; upload.LeaseExpiresAtUtc = null; upload.Version++; break;
            case FileRecoveryKinds.Mutation:
                var mutation = material.Read<FileMutationOperation>(snapshot); db.Attach(mutation); var mutated = receipt as FileMutationReceipt;
                mutation.State = state; mutation.CompletedAtUtc = now; mutation.ResultCode = code; mutation.PublishedVersion = complete ? mutated!.Version : null;
                mutation.SourceRetained |= mutated?.SourceRetained == true; mutation.LeaseToken = null; mutation.LeaseExpiresAtUtc = null; mutation.Version++; break;
            case FileRecoveryKinds.Archive:
                var archive = material.Read<FileArchiveOperation>(snapshot); db.Attach(archive); var archived = receipt as FileArchiveReceipt;
                archive.State = state; archive.CompletedAtUtc = now; archive.ResultCode = code; archive.PublishedVersion = complete ? archived!.Version : null;
                archive.SourceRetained |= archived?.SourceRetained == true; archive.LeaseToken = null; archive.LeaseExpiresAtUtc = null; archive.Version++;
                if (archived is not null) await archiveCatalog.CaptureRecoveryAsync(archive, archived, cancellationToken); break;
            case FileRecoveryKinds.TreeCopy:
                var copy = material.Read<FileTreeCopyOperation>(snapshot); db.Attach(copy); var copied = receipt as FileTreeCopyReceipt;
                copy.State = state; copy.CompletedAtUtc = now; copy.ResultCode = code; copy.PublishedVersion = complete ? copied!.Version : null;
                copy.PartialRetained = !complete && (copy.PartialRetained || copied?.PartialRetained == true); copy.LeaseToken = null; copy.LeaseExpiresAtUtc = null; copy.Version++; break;
            case FileRecoveryKinds.TreeMove:
                var move = material.Read<FileTreeMoveOperation>(snapshot); db.Attach(move); var moved = receipt as FileTreeMoveReceipt;
                move.State = state; move.CompletedAtUtc = now; move.ResultCode = code; move.PublishedVersion = complete ? moved!.Version : null;
                if (moved is not null) { move.DestinationPublished = moved.DestinationPublished; move.SourceRemoved = moved.SourceRemoved; move.SourceRetained = moved.SourceRetained; }
                move.PartialRetained = !complete && (move.PartialRetained || moved?.PartialRetained == true); move.LeaseToken = null; move.LeaseExpiresAtUtc = null; move.Version++; break;
            case FileRecoveryKinds.TreePacking:
                var packing = material.Read<FileTreePackingOperation>(snapshot); db.Attach(packing); var packed = receipt as FileTreePackingReceipt;
                packing.State = state; packing.CompletedAtUtc = now; packing.ResultCode = code; packing.PublishedVersion = complete ? packed!.Version : null;
                packing.ArchiveBytes = complete ? packed!.ArchiveBytes : null; packing.ArchiveSha256 = complete ? packed!.ArchiveSha256 : null;
                packing.PartialRetained = !complete && (packing.PartialRetained || packed?.PartialRetained == true); packing.LeaseToken = null; packing.LeaseExpiresAtUtc = null; packing.Version++; break;
            case FileRecoveryKinds.TreeUnpacking:
                var unpacking = material.Read<FileTreeUnpackingOperation>(snapshot); db.Attach(unpacking); var unpacked = receipt as FileTreeUnpackingReceipt;
                unpacking.State = state; unpacking.CompletedAtUtc = now; unpacking.ResultCode = code; unpacking.PublishedVersion = complete ? unpacked!.Version : null;
                // Input archive proof survives acknowledgment and aborted/unknown results.
                unpacking.ArchiveBytes = unpacked?.ArchiveBytes ?? unpacking.ArchiveBytes; unpacking.ArchiveSha256 = unpacked?.ArchiveSha256 ?? unpacking.ArchiveSha256;
                unpacking.PartialRetained = !complete && (unpacking.PartialRetained || unpacked?.PartialRetained == true); unpacking.LeaseToken = null; unpacking.LeaseExpiresAtUtc = null; unpacking.Version++; break;
            case FileRecoveryKinds.TreeRecycling:
                var recycling = material.Read<FileTreeRecyclingOperation>(snapshot); db.Attach(recycling); var recycled = receipt as FileTreeRecyclingReceipt;
                if (recycled is not null)
                {
                    if (recyclingCatalog is null) throw new InvalidOperationException("FILES_CAPTURE_CATALOGUE_REQUIRED");
                    await recyclingCatalog.CaptureRecoveryAsync(recycling, recycled, now, cancellationToken);
                }
                recycling.State = state; recycling.CompletedAtUtc = now; recycling.ResultCode = code;
                recycling.LeaseToken = null; recycling.LeaseExpiresAtUtc = null; recycling.Version++; break;
            case FileRecoveryKinds.TreeRestoring:
                var restoring = material.Read<FileTreeRestoringOperation>(snapshot); db.Attach(restoring); var restored = receipt as FileTreeRestoringReceipt;
                restoring.State = state; restoring.CompletedAtUtc = now; restoring.ResultCode = code; restoring.PublishedVersion = complete ? restored!.Version : null;
                restoring.ArchiveBytes = restored?.ArchiveBytes ?? restoring.ArchiveBytes; restoring.ArchiveSha256 = restored?.ArchiveSha256 ?? restoring.ArchiveSha256;
                restoring.PartialRetained = !complete && (restoring.PartialRetained || restored?.PartialRetained == true);
                restoring.LeaseToken = null; restoring.LeaseExpiresAtUtc = null; restoring.Version++; break;
            case FileRecoveryKinds.TreeOverwriteRestoring:
                var overwrite = material.Read<FileTreeOverwriteRestoringOperation>(snapshot); db.Attach(overwrite); var overwritten = receipt as FileTreeOverwriteRestoringReceipt;
                if (overwritten is not null)
                {
                    if (overwriteCatalog is null) throw new InvalidOperationException("FILES_CAPTURE_CATALOGUE_REQUIRED");
                    await overwriteCatalog.CaptureRecoveryAsync(overwrite, overwritten, now, cancellationToken);
                }
                overwrite.State = state; overwrite.CompletedAtUtc = now; overwrite.ResultCode = code; overwrite.PublishedVersion = complete ? overwritten!.Version : null;
                overwrite.ArchiveBytes = overwritten?.ArchiveBytes ?? overwrite.ArchiveBytes; overwrite.ArchiveSha256 = overwritten?.ArchiveSha256 ?? overwrite.ArchiveSha256;
                overwrite.PartialRetained = !complete && (overwrite.PartialRetained || overwritten?.PartialRetained == true);
                overwrite.LeaseToken = null; overwrite.LeaseExpiresAtUtc = null; overwrite.Version++; break;
            case FileRecoveryKinds.TreePurging:
                var purging = material.Read<FileTreePurgingOperation>(snapshot); db.Attach(purging);
                if (purgingCatalog is null || receipt is not FileTreePurgingReceipt purged) throw new InvalidOperationException("FILES_CAPTURE_CATALOGUE_REQUIRED");
                purgingCatalog.DisposeRecovery(purging, purged, now, complete); state = purging.State; break;
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
        var job = await db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={recovery.OriginalExecutionJobId} FOR UPDATE").SingleAsync(cancellationToken);
        job.State = state == "Succeeded" ? "Succeeded" : "Failed"; job.Phase = "AdministratorRecovery"; job.ErrorCode = state == "Succeeded" ? null : code;
        job.CompletedAtUtc = now; job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
        var sequence = (await db.OperationJobEvents.Where(row => row.JobId == job.Id).MaxAsync(row => (long?)row.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
        await FilePathLocks.Release(db, job.Id, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('ContainerFileUploadRequested','ContainerFileUploadCancellationRequested','FileUploadRequested','FileUploadCancellationRequested','FileMutationRequested','FileMutationCancellationRequested','FileArchiveRequested','FileArchiveCancellationRequested','FileTreeCopyRequested','FileTreeCopyCancellationRequested','FileTreeMoveRequested','FileTreeMoveCancellationRequested','FileTreePackingRequested','FileTreePackingCancellationRequested','FileTreeUnpackingRequested','FileTreeUnpackingCancellationRequested','FileTreeRecyclingRequested','FileTreeRecyclingCancellationRequested','FileTreeRestoringRequested','FileTreeRestoringCancellationRequested','FileTreeOverwriteRestoringRequested','FileTreeOverwriteRestoringCancellationRequested','FileTreePurgingRequested','FileTreePurgingCancellationRequested') AND payload_json->>'jobId'={job.Id.ToString("D")}", cancellationToken);
        return state;
    }
}
