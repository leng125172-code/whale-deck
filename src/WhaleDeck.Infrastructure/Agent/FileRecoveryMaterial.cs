using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Agent;

/// <summary>Validates the real old intent MAC and freezes exact material, not an old human's authority.</summary>
public sealed class FileRecoveryMaterial(IFilePlanProtector uploads, IFileMutationProtector mutations,
    IFileArchiveProtector archives, IFileTreeCopyProtector copies, IFileTreeMoveProtector moves, IFileTreePackingProtector packings, IFileTreeUnpackingProtector unpackings,
    IFileTreeRecyclingProtector? recyclings = null, IFileTreeRestoringProtector? restorings = null, IFileTreeOverwriteRestoringProtector? overwrites = null)
{
    public const int MaximumBytes = 262144;
    public object? ValidateObservation(FileRecoverySnapshot original, FileRecoveryObservation observation)
    {
        if (observation.State is not ("Missing" or "Unknown" or "Aborted" or "Completed") || Encoding.UTF8.GetByteCount(observation.ReceiptJson) > 16384)
            throw new FileTransferRejectedException();
        if (observation.State == "Missing")
        {
            // Missing root evidence does not make the signed historical snapshot optional.
            _ = Read<object>(original);
            if (observation.ReceiptJson != "null" || observation.BindingCurrent || observation.ReceiptHash != FileRecoveryValidation.ReceiptHash(original.Kind, original.Binding.ExecutionJobId, "Missing", "null"))
                throw new FileTransferRejectedException();
            return null;
        }
        object value; string state; bool current;
        switch (original.Kind)
        {
            case FileRecoveryKinds.Upload:
                var upload = Read<FileUploadOperation>(original); var uploaded = Decode<FileUploadReceiptDto>();
                if (uploaded.IsInProgress || uploaded.State is not ("Completed" or "Unknown" or "Aborted")) throw new FileRecoveryBusyException();
                if (uploaded.State == "Completed")
                {
                    if (uploaded.SizeBytes != upload.SizeBytes || uploaded.Sha256 != upload.Sha256 || !FileManagementService.Hex(uploaded.Version)) throw new FileTransferRejectedException();
                    FileManagementService.ValidateBackup(upload, uploaded.HddBackup);
                }
                value = uploaded; state = uploaded.State; current = uploaded.BindingCurrent; break;
            case FileRecoveryKinds.Mutation:
                var mutation = Read<FileMutationOperation>(original); var mutated = Decode<FileMutationReceipt>();
                FileMutationService.ValidateReceipt(FileMutationService.ReadPlan(mutation), mutated);
                value = mutated; state = mutated.State; current = mutated.BindingCurrent; break;
            case FileRecoveryKinds.Archive:
                var archive = Read<FileArchiveOperation>(original); var archived = Decode<FileArchiveReceipt>();
                FileArchiveValidation.Receipt(archive.Id, FileArchiveValidation.ReadPlan(archive), archived);
                value = archived; state = archived.State; current = archived.BindingCurrent; break;
            case FileRecoveryKinds.TreeCopy:
                var copy = Read<FileTreeCopyOperation>(original); var copied = Decode<FileTreeCopyReceipt>();
                FileTreeCopyValidation.Receipt(copy.ExecutionJobId!.Value, FileTreeCopyValidation.ReadPlan(copy), copied);
                value = copied; state = copied.State; current = copied.State == "Completed"; break;
            case FileRecoveryKinds.TreeMove:
                var move = Read<FileTreeMoveOperation>(original); var moved = Decode<FileTreeMoveReceipt>();
                FileTreeMoveValidation.Receipt(move.ExecutionJobId!.Value, FileTreeMoveValidation.ReadPlan(move), moved);
                value = moved; state = moved.State; current = moved.State == "Completed"; break;
            case FileRecoveryKinds.TreePacking:
                var packing = Read<FileTreePackingOperation>(original); var packed = Decode<FileTreePackingReceipt>();
                FileTreePackingValidation.Receipt(packing.ExecutionJobId!.Value, FileTreePackingValidation.ReadPlan(packing), packed);
                value = packed; state = packed.State; current = packed.State == "Completed"; break;
            case FileRecoveryKinds.TreeUnpacking:
                var unpacking = Read<FileTreeUnpackingOperation>(original); var unpacked = Decode<FileTreeUnpackingReceipt>();
                FileTreeUnpackingValidation.Receipt(unpacking.ExecutionJobId!.Value, FileTreeUnpackingValidation.ReadPlan(unpacking), unpacked);
                value = unpacked; state = unpacked.State; current = unpacked.State == "Completed"; break;
            case FileRecoveryKinds.TreeRecycling:
                var recycling = Read<FileTreeRecyclingOperation>(original); var recycled = Decode<FileTreeRecyclingReceipt>();
                FileTreeRecyclingValidation.Receipt(recycling.ExecutionJobId!.Value, FileTreeRecyclingValidation.ReadPlan(recycling), recycled);
                value = recycled; state = recycled.State; current = recycled.State == "Completed"; break;
            case FileRecoveryKinds.TreeRestoring:
                var restoring = Read<FileTreeRestoringOperation>(original); var restored = Decode<FileTreeRestoringReceipt>();
                FileTreeRestoringValidation.Receipt(restoring.ExecutionJobId!.Value, FileTreeRestoringValidation.ReadPlan(restoring), restored);
                value = restored; state = restored.State; current = restored.State == "Completed"; break;
            case FileRecoveryKinds.TreeOverwriteRestoring:
                var overwrite = Read<FileTreeOverwriteRestoringOperation>(original); var overwritten = Decode<FileTreeOverwriteRestoringReceipt>();
                FileTreeOverwriteRestoringValidation.Receipt(overwrite.ExecutionJobId!.Value, FileTreeOverwriteRestoringValidation.ReadPlan(overwrite), overwritten);
                if (overwrite.BackupVerified && (!overwritten.BackupVerified || overwrite.BackupArchiveBytes != overwritten.BackupArchiveBytes || overwrite.BackupArchiveSha256 != overwritten.BackupArchiveSha256) ||
                    overwrite.BackupMayBeRetained && !overwritten.BackupMayBeRetained || overwrite.OriginalsRetained && !overwritten.OriginalsRetained ||
                    overwrite.TargetMayHaveChanged && !overwritten.TargetMayHaveChanged) throw new FileTransferRejectedException();
                value = overwritten; state = overwritten.State; current = overwritten.State == "Completed"; break;
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
        var canonical = JsonSerializer.Serialize(value, value.GetType());
        if (state != observation.State || current != observation.BindingCurrent ||
            observation.ReceiptHash != FileRecoveryValidation.ReceiptHash(original.Kind, original.Binding.ExecutionJobId, state, canonical)) throw new FileTransferRejectedException();
        return value;
        T Decode<T>() => JsonSerializer.Deserialize<T>(observation.ReceiptJson, FileTreeCopyValidation.JsonOptions) ?? throw new FileTransferRejectedException();
    }
    public FileRecoverySnapshot Capture(FileUploadOperation row) => Freeze(FileRecoveryKinds.Upload, row.Id, row.Id, row.Version,
        FileManagementService.HasValidBinding(row, uploads), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileMutationOperation row) => Freeze(FileRecoveryKinds.Mutation, row.Id, row.Id, row.Version,
        FileMutationService.HasValidBinding(row, mutations), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileArchiveOperation row) => Freeze(FileRecoveryKinds.Archive, row.Id, row.Id, row.Version,
        FileArchiveValidation.HasValidBinding(row, archives), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeCopyOperation row) => Freeze(FileRecoveryKinds.TreeCopy, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        FileTreeCopyValidation.HasValidBinding(row, copies), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeMoveOperation row) => Freeze(FileRecoveryKinds.TreeMove, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        FileTreeMoveValidation.HasValidBinding(row, moves), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreePackingOperation row) => Freeze(FileRecoveryKinds.TreePacking, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        FileTreePackingValidation.HasValidBinding(row, packings), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeUnpackingOperation row) => Freeze(FileRecoveryKinds.TreeUnpacking, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        FileTreeUnpackingValidation.HasValidBinding(row, unpackings), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeRecyclingOperation row) => Freeze(FileRecoveryKinds.TreeRecycling, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        recyclings is not null && FileTreeRecyclingValidation.HasValidBinding(row, recyclings), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeRestoringOperation row) => Freeze(FileRecoveryKinds.TreeRestoring, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        restorings is not null && FileTreeRestoringValidation.HasValidBinding(row, restorings), row.State, row.CompletedAtUtc, row);
    public FileRecoverySnapshot Capture(FileTreeOverwriteRestoringOperation row) => Freeze(FileRecoveryKinds.TreeOverwriteRestoring, row.Id, row.ExecutionJobId ?? Guid.Empty, row.Version,
        overwrites is not null && FileTreeOverwriteRestoringValidation.HasValidBinding(row, overwrites), row.State, row.CompletedAtUtc, row);
    public T Read<T>(FileRecoverySnapshot snapshot) where T : class
    {
        FileRecoveryValidation.Target(snapshot.Binding);
        if (snapshot.Kind != snapshot.Binding.Kind || Encoding.UTF8.GetByteCount(snapshot.OriginalJson) > MaximumBytes)
            throw new InvalidOperationException("FILES_RECOVERY_SNAPSHOT_INVALID");
        object row; FileRecoverySnapshot verified;
        switch (snapshot.Kind)
        {
            case FileRecoveryKinds.Upload: var upload = Decode<FileUploadOperation>(); row = upload; verified = Capture(upload); break;
            case FileRecoveryKinds.Mutation: var mutation = Decode<FileMutationOperation>(); row = mutation; verified = Capture(mutation); break;
            case FileRecoveryKinds.Archive: var archive = Decode<FileArchiveOperation>(); row = archive; verified = Capture(archive); break;
            case FileRecoveryKinds.TreeCopy: var copy = Decode<FileTreeCopyOperation>(); row = copy; verified = Capture(copy); break;
            case FileRecoveryKinds.TreeMove: var move = Decode<FileTreeMoveOperation>(); row = move; verified = Capture(move); break;
            case FileRecoveryKinds.TreePacking: var packing = Decode<FileTreePackingOperation>(); row = packing; verified = Capture(packing); break;
            case FileRecoveryKinds.TreeUnpacking: var unpacking = Decode<FileTreeUnpackingOperation>(); row = unpacking; verified = Capture(unpacking); break;
            case FileRecoveryKinds.TreeRecycling: var recycling = Decode<FileTreeRecyclingOperation>(); row = recycling; verified = Capture(recycling); break;
            case FileRecoveryKinds.TreeRestoring: var restoring = Decode<FileTreeRestoringOperation>(); row = restoring; verified = Capture(restoring); break;
            case FileRecoveryKinds.TreeOverwriteRestoring: var overwrite = Decode<FileTreeOverwriteRestoringOperation>(); row = overwrite; verified = Capture(overwrite); break;
            default: throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
        }
        if (verified.Binding != snapshot.Binding || row is not T expected) throw new InvalidOperationException("FILES_RECOVERY_SNAPSHOT_CHANGED");
        return expected;
        TRow Decode<TRow>() => JsonSerializer.Deserialize<TRow>(snapshot.OriginalJson, FileTreeCopyValidation.JsonOptions)
            ?? throw new InvalidOperationException("FILES_RECOVERY_SNAPSHOT_INVALID");
    }
    private static FileRecoverySnapshot Freeze<T>(string kind, Guid id, Guid execution, long version, bool valid,
        string state, DateTimeOffset? completed, T row)
    {
        if (!valid) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (state != "Unknown" || completed is not null) throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_NOT_UNKNOWN");
        var json = JsonSerializer.Serialize(row);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidOperationException("FILES_RECOVERY_SNAPSHOT_TOO_LARGE");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("files-recovery-original-snapshot-v1\n" + kind + "\n" + json)));
        var binding = new FileRecoveryTargetBinding(kind, id, execution, version, hash); FileRecoveryValidation.Target(binding);
        return new(kind, json, binding);
    }
}
