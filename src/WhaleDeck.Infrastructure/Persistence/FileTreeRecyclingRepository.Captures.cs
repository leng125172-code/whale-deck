using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeRecyclingRepository
{
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
        FileManagementService.Location(location);
        if (take is < 1 or > 100 || after == Guid.Empty) throw new ArgumentException("FILES_PAGE_INVALID");
        var scope = new FilePathScope(location);
        FileTreeCapture[] rows;
        if (after is { } id)
        {
            var cursor = await db.FileTreeCaptures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.SourceHash == scope.Hash, cancellationToken)
                ?? throw new ArgumentException("FILES_CURSOR_INVALID");
            FileTreeRecyclingValidation.Capture(cursor, protector);
            rows = await db.FileTreeCaptures.FromSqlInterpolated($"SELECT * FROM file_tree_captures WHERE source_hash={scope.Hash} AND (created_at_utc,id)>({cursor.CreatedAtUtc},{cursor.Id}) ORDER BY created_at_utc,id LIMIT {take + 1}")
                .AsNoTracking().ToArrayAsync(cancellationToken);
        }
        else rows = await db.FileTreeCaptures.Where(item => item.SourceHash == scope.Hash).OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).Take(take + 1).AsNoTracking().ToArrayAsync(cancellationToken);
        var items = rows.Take(take).Select(item =>
        {
            FileTreeRecyclingValidation.Capture(item, protector); var binding = FileTreeRecyclingValidation.CaptureBinding(item);
            return new FileTreeCaptureDto(item.Id, item.IntentId, binding.Command, item.ManifestSha256, item.EntryCount, item.TotalBytes, item.ArchiveBytes, item.ArchiveSha256, item.CreatedAtUtc);
        }).ToArray();
        return new(items, rows.Length > take ? items[^1].Id : null);
    }
}
