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
            var snapshots = db.NotificationReadSnapshots.Where(item => item.ExpiresAtUtc <= now).OrderBy(item => item.ExpiresAtUtc).Take(10).Select(item => item.Id);
            await db.NotificationReadSnapshots.Where(item => snapshots.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return removed;
        });
}
