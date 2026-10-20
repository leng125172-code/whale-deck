using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeOverwriteRestoringRepository
{
    private async Task<(FileTreeCaptureDto Capture, string Owner)> Capture(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("FILES_CAPTURE_NOT_FOUND");
        return (FileTreeCaptureValidation.Verify(row, captureProtector, protector), row.OwnerHash);
    }

    private async Task<FileTreeOverwriteRestoringCommand> CaptureCommand(CreateFileTreeOverwriteRestoringCommand request, CancellationToken cancellationToken)
    {
        var (capture, owner) = await Capture(request.CaptureId, cancellationToken);
        var command = new FileTreeOverwriteRestoringCommand(capture.Source.Source, capture.Source.ExpectedVersion, request.Target,
            "tar.gz", capture.Id, owner, capture.Kind, capture.ManifestSha256, request.TargetExpectedVersion);
        FileTreeOverwriteRestoringValidation.Command(command); return command;
    }

    private async Task RequireCapture(FileTreeOverwriteRestoringPlan plan, CancellationToken cancellationToken)
    {
        var (capture, _) = await Capture(plan.Command.CaptureId, cancellationToken);
        if (plan.Command != await CaptureCommand(new(capture.Id, plan.Command.Target, plan.Command.TargetExpectedVersion), cancellationToken) ||
            plan.EntryCount != capture.EntryCount || plan.TotalBytes != capture.TotalBytes || plan.ArchiveBytes != capture.ArchiveBytes || plan.ArchiveSha256 != capture.ArchiveSha256 ||
            capture.ContentSha256 is not null && plan.ContentSha256 != capture.ContentSha256) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
    }

    internal Task CaptureRecoveryAsync(FileTreeOverwriteRestoringOperation row, FileTreeOverwriteRestoringReceipt receipt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_RECOVERY_TRANSACTION_REQUIRED");
        if (row.State != "Unknown" || !FileTreeOverwriteRestoringValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return StoreReceipt(row, receipt, FileTreeOverwriteRestoringValidation.Timestamp(now), cancellationToken);
    }

    private async Task StoreReceipt(FileTreeOverwriteRestoringOperation row, FileTreeOverwriteRestoringReceipt receipt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var plan = FileTreeOverwriteRestoringValidation.ReadPlan(row);
        FileTreeOverwriteRestoringValidation.Receipt(row.ExecutionJobId!.Value, plan, receipt);
        if (row.BackupVerified && (!receipt.BackupVerified || row.BackupArchiveBytes != receipt.BackupArchiveBytes || row.BackupArchiveSha256 != receipt.BackupArchiveSha256) ||
            row.BackupMayBeRetained && !receipt.BackupMayBeRetained || row.OriginalsRetained && !receipt.OriginalsRetained || row.TargetMayHaveChanged && !receipt.TargetMayHaveChanged)
            throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
        if (receipt.BackupVerified)
        {
            var capturedAt = row.BackupCapturedAtUtc ?? now;
            var binding = new FileTreeOverwrittenCaptureBinding(receipt.OverwrittenCaptureId!.Value, row.Id, row.OwnerHash, row.TargetHash,
                new(plan.Command.Target, plan.Command.TargetExpectedVersion), plan.TargetManifestSha256, plan.TargetTreeSha256, plan.TargetEntryCount,
                plan.TargetTotalBytes, receipt.BackupArchiveBytes, receipt.BackupArchiveSha256, capturedAt);
            var proof = protector.FingerprintCapture(binding);
            if (row.BackupVerified && row.BackupProofHash != proof) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
            var previous = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == binding.Id, cancellationToken);
            if (previous is not null)
            {
                FileTreeCaptureValidation.Verify(previous, captureProtector, protector);
                if (!row.BackupVerified || FileTreeCaptureValidation.OverwrittenBinding(previous) != binding) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
            }
            else
            {
                if (row.BackupVerified) throw new InvalidOperationException("FILES_CAPTURE_CATALOGUE_MISSING");
                db.FileTreeCaptures.Add(new() { Id = binding.Id, IntentId = row.Id, Kind = "Overwrite", OwnerHash = row.OwnerHash, SourceHash = row.TargetHash,
                    SourceJson = JsonSerializer.Serialize(binding.Command), ManifestSha256 = binding.ManifestSha256, ContentSha256 = binding.ContentSha256,
                    EntryCount = binding.EntryCount, TotalBytes = binding.TotalBytes, ArchiveBytes = binding.ArchiveBytes, ArchiveSha256 = binding.ArchiveSha256,
                    ProofHash = proof, CreatedAtUtc = capturedAt });
            }
            row.OverwrittenCaptureId = binding.Id; row.BackupProofHash = proof; row.BackupCapturedAtUtc = capturedAt;
            row.BackupArchiveBytes = binding.ArchiveBytes; row.BackupArchiveSha256 = binding.ArchiveSha256;
        }
        row.BackupVerified = receipt.BackupVerified; row.BackupMayBeRetained = receipt.BackupMayBeRetained;
        row.OriginalsRetained = receipt.OriginalsRetained; row.TargetMayHaveChanged = receipt.TargetMayHaveChanged;
    }
}
