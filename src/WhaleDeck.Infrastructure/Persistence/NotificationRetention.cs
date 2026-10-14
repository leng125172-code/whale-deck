using Microsoft.EntityFrameworkCore;

namespace WhaleDeck.Infrastructure.Persistence;

public static class NotificationRetention
{
    /// <summary>One bounded expired batch; no Agent dependency, reads cascade with events.</summary>
    public static Task<int> PruneAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s';", cancellationToken);
            var acquired = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(41263102) AS \"Value\"").SingleAsync(cancellationToken);
            if (!acquired) return 0;
            var batch = db.NotificationEvents.Where(item => item.ExpiresAtUtc <= now).OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.Sequence).Take(1000).Select(item => item.Id);
            var removed = await db.NotificationEvents.Where(item => batch.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
            // Lock expired headers against renewal/replacement and FK insertion. Limiting only
            // headers would still cascade arbitrarily many members in one statement.
            var snapshots = await db.NotificationReadSnapshots.FromSqlInterpolated($"""
                SELECT * FROM notification_read_snapshots WHERE expires_at_utc<={now}
                ORDER BY expires_at_utc,id LIMIT 10 FOR UPDATE SKIP LOCKED
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            var snapshotIds = snapshots.Select(item => item.Id).ToArray();
            if (snapshotIds.Length > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM notification_read_snapshot_items
                    WHERE (snapshot_id,notification_id) IN (
                        SELECT snapshot_id,notification_id FROM notification_read_snapshot_items
                        WHERE snapshot_id=ANY({snapshotIds}) ORDER BY snapshot_id,notification_id LIMIT 1000
                    )
                    """, cancellationToken);
                await db.NotificationReadSnapshots.Where(item => snapshotIds.Contains(item.Id) &&
                    !db.NotificationReadSnapshotItems.Any(member => member.SnapshotId == item.Id)).ExecuteDeleteAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return removed;
        });
}
