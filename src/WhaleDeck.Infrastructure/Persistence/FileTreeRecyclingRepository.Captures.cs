using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeRecyclingRepository
{
    internal Task CaptureRecoveryAsync(FileTreeRecyclingOperation row, FileTreeRecyclingReceipt receipt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_RECOVERY_TRANSACTION_REQUIRED");
        if (row.State != "Unknown" || !FileTreeRecyclingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        return StoreReceipt(row, receipt, FileTreeRecyclingValidation.Timestamp(now), cancellationToken);
    }
    private async Task StoreReceipt(FileTreeRecyclingOperation row, FileTreeRecyclingReceipt receipt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var plan = FileTreeRecyclingValidation.ReadPlan(row);
        FileTreeRecyclingValidation.Receipt(row.ExecutionJobId!.Value, plan, receipt);
        if (row.CaptureVerified && (!receipt.CaptureVerified || row.ArchiveBytes != receipt.ArchiveBytes || row.ArchiveSha256 != receipt.ArchiveSha256) ||
            row.SourceMayHaveChanged && !receipt.SourceMayHaveChanged || row.CaptureMayBeRetained && !receipt.CaptureMayBeRetained)
            throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
        if (receipt.CaptureVerified)
        {
            var capturedAt = row.CaptureCreatedAtUtc ?? now;
            var binding = new FileTreeCaptureBinding(receipt.CaptureId, row.Id, row.OwnerHash, row.SourceHash, plan.Command, plan.ManifestSha256,
                plan.EntryCount, plan.TotalBytes, receipt.ArchiveBytes, receipt.ArchiveSha256, capturedAt);
            var proof = protector.FingerprintCapture(binding);
            if (row.CaptureVerified && row.CaptureHash != proof) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
            var previous = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == receipt.CaptureId, cancellationToken);
            if (previous is not null)
            {
                FileTreeRecyclingValidation.Capture(previous, protector);
                if (!row.CaptureVerified || FileTreeRecyclingValidation.CaptureBinding(previous) != binding) throw new InvalidOperationException("FILES_CAPTURE_PROOF_CHANGED");
            }
            else
            {
                if (row.CaptureVerified) throw new InvalidOperationException("FILES_CAPTURE_CATALOGUE_MISSING");
                db.FileTreeCaptures.Add(new() { Id = binding.Id, IntentId = row.Id, OwnerHash = row.OwnerHash, SourceHash = row.SourceHash,
                    SourceJson = JsonSerializer.Serialize(plan.Command), ManifestSha256 = plan.ManifestSha256, EntryCount = plan.EntryCount, TotalBytes = plan.TotalBytes,
                    ArchiveBytes = receipt.ArchiveBytes, ArchiveSha256 = receipt.ArchiveSha256, ProofHash = proof, CreatedAtUtc = capturedAt });
            }
            row.CaptureId = receipt.CaptureId; row.CaptureHash = proof; row.CaptureCreatedAtUtc = capturedAt;
            row.ArchiveBytes = receipt.ArchiveBytes; row.ArchiveSha256 = receipt.ArchiveSha256;
        }
        row.CaptureVerified = receipt.CaptureVerified; row.CaptureMayBeRetained = receipt.CaptureMayBeRetained;
        row.SourceMayHaveChanged = receipt.SourceMayHaveChanged; row.SourceRemoved = receipt.SourceRemoved; row.OriginalsRetained = receipt.OriginalsRetained;
    }

    public async Task<FileTreeCapturePage> CapturesAsync(FileLocationDto location, Guid? after, int take, CancellationToken cancellationToken)
    {
        FileManagementService.Location(location, true);
        if (take is < 1 or > 100 || after == Guid.Empty) throw new ArgumentException("FILES_PAGE_INVALID");
        var sourceHash = FileTreeRecyclingValidation.CaptureSourceHash(location);
        var wholeRoot = sourceHash is null;
        FileTreeCapture[] rows;
        if (after is { } id)
        {
            var cursor = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new ArgumentException("FILES_CURSOR_INVALID");
            var verifiedCursor = FileTreeCaptureValidation.Verify(cursor, protector, overwriteProtector);
            if (wholeRoot ? verifiedCursor.Source.Source.RootId != location.RootId : cursor.SourceHash != sourceHash) throw new ArgumentException("FILES_CURSOR_INVALID");
            rows = wholeRoot
                ? await db.FileTreeCaptures.FromSqlInterpolated($"SELECT * FROM file_tree_captures WHERE source_json->'Source'->>'RootId'={location.RootId} AND (created_at_utc,id)>({cursor.CreatedAtUtc},{cursor.Id}) ORDER BY created_at_utc,id LIMIT {take + 1}").AsNoTracking().ToArrayAsync(cancellationToken)
                : await db.FileTreeCaptures.FromSqlInterpolated($"SELECT * FROM file_tree_captures WHERE source_hash={sourceHash} AND (created_at_utc,id)>({cursor.CreatedAtUtc},{cursor.Id}) ORDER BY created_at_utc,id LIMIT {take + 1}").AsNoTracking().ToArrayAsync(cancellationToken);
        }
        else rows = wholeRoot
            ? await db.FileTreeCaptures.FromSqlInterpolated($"SELECT * FROM file_tree_captures WHERE source_json->'Source'->>'RootId'={location.RootId} ORDER BY created_at_utc,id LIMIT {take + 1}").AsNoTracking().ToArrayAsync(cancellationToken)
            : await db.FileTreeCaptures.Where(item => item.SourceHash == sourceHash).OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).Take(take + 1).AsNoTracking().ToArrayAsync(cancellationToken);
        var items = rows.Take(take).Select(item => FileTreeCaptureValidation.Verify(item, protector, overwriteProtector)).ToArray();
        return new(items, rows.Length > take ? items[^1].Id : null);
    }
}
