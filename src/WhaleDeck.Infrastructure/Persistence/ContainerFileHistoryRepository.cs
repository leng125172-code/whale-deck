using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class ContainerFileHistoryRepository(PlatformDbContext db, TimeProvider clock) : IContainerFileHistoryRepository
{
    public async Task ObserveAsync(TerminalActor actor, ContainerHistoryPageDto page, CancellationToken cancellationToken)
    {
        _ = BffSessionValidation.SubjectHash(actor.Subject);
        ContainerFileHistoryValidation.Page(page, page.Location, "", 50, "");
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307342);", cancellationToken);
            foreach (var entry in page.Entries)
            {
                var id = Guid.ParseExact(entry.CaptureId, "N");
                var row = await db.ContainerFileVersions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (row is null)
                {
                    row = new() { Id = id, ContainerId = entry.ContainerId, OriginalInstanceId = entry.OriginalInstanceId,
                        RelativePath = entry.Path, SourceVersion = entry.SourceVersion, Sha256 = entry.Sha256, CaptureHash = entry.CaptureHash,
                        SizeBytes = entry.SizeBytes, CapturedAtUnixMs = entry.CreatedAtUnixMs, DifferentSourceDevice = entry.DifferentSourceDevice,
                        Availability = entry.Availability, LastObservedAtUtc = clock.GetUtcNow() };
                    db.ContainerFileVersions.Add(row);
                }
                else
                {
                    if (row.ContainerId != entry.ContainerId || row.OriginalInstanceId != entry.OriginalInstanceId || row.RelativePath != entry.Path ||
                        row.SourceVersion != entry.SourceVersion || row.SizeBytes != entry.SizeBytes || row.Sha256 != entry.Sha256 ||
                        row.CapturedAtUnixMs != entry.CreatedAtUnixMs || row.DifferentSourceDevice != entry.DifferentSourceDevice ||
                        row.CaptureHash != entry.CaptureHash && row.Availability != "Incomplete")
                        throw new InvalidOperationException("CONTAINER_HISTORY_CAPTURE_CHANGED");
                    row.Availability = entry.Availability; row.CaptureHash = entry.CaptureHash; row.LastObservedAtUtc = clock.GetUtcNow(); row.Version++;
                }
            }
            // A bounded/filtered page is never evidence that an unreturned
            // capture was removed. Keep older durable facts instead of deleting.
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        });
    }
    public async Task<ContainerHistoryEntryDto?> FindAsync(string containerId, string captureId, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Id(containerId); ContainerFileHistoryValidation.CaptureId(captureId);
        var id = Guid.ParseExact(captureId, "N");
        var row = await db.ContainerFileVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.ContainerId == containerId, cancellationToken);
        if (row is null) return null;
        var result = new ContainerHistoryEntryDto(row.Id.ToString("N"), row.ContainerId, row.OriginalInstanceId, row.RelativePath, row.SourceVersion,
            row.SizeBytes, row.Sha256, row.CapturedAtUnixMs, row.DifferentSourceDevice, row.Availability, row.CaptureHash);
        ContainerFileHistoryValidation.Entry(result); return result;
    }
}
