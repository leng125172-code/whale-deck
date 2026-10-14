using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Internal transactional outbox. Transport I/O must never run inside these retryable transactions.</summary>
public sealed class NotificationDispatchStore(PlatformDbContext db)
{
    public Task<int> RouteBatchAsync(DateTimeOffset now, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        var events = await db.NotificationEvents.FromSqlInterpolated($"""
            SELECT n.* FROM notification_events n
            WHERE n.expires_at_utc>{now} AND NOT EXISTS
              (SELECT 1 FROM notification_dispatch_receipts r WHERE r.notification_id=n.id)
            ORDER BY n.sequence LIMIT 100 FOR UPDATE OF n SKIP LOCKED
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var channels = await db.NotificationChannels.AsNoTracking().Where(item => item.IsEnabled).Take(33).ToArrayAsync(cancellationToken);
        if (channels.Length > 32) throw new InvalidOperationException("Notification channel budget exceeded.");
        var routes = await db.NotificationRoutes.AsNoTracking().Where(item => item.IsEnabled).Take(513).ToArrayAsync(cancellationToken);
        if (routes.Length > 512) throw new InvalidOperationException("Notification route budget exceeded.");
        var processed = 0;
        foreach (var item in events)
        {
            // Recheck after acquiring the fact lock: another transaction may have committed
            // a receipt after this SELECT's statement snapshot but before the row lock.
            if (await db.NotificationDispatchReceipts.AnyAsync(receipt => receipt.NotificationId == item.Id, cancellationToken)) continue;
            var targets = channels.Where(channel => routes.Any(route => NotificationDispatchPolicy.Matches(item, route, channel))).ToArray();
            foreach (var channel in targets)
            {
                NotificationDispatchPolicy.Validate(channel);
                var id = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO notification_deliveries
                      (id,notification_id,channel_id,channel_version,provider,destination_reference,credential_reference,
                       state,attempt_count,created_at_utc,next_attempt_at_utc)
                    VALUES ({id},{item.Id},{channel.Id},{channel.Version},{channel.Provider},{channel.DestinationReference},
                      {channel.CredentialReference},'Pending',0,{now},{now})
                    """, cancellationToken);
            }
            var outcome = item.Audience == "Subject" ? "Personal" : targets.Length > 0 ? "Matched" : "NoRoute";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_dispatch_receipts (notification_id,routed_at_utc,outcome,delivery_count)
                VALUES ({item.Id},{now},{outcome},{targets.Length})
                """, cancellationToken);
            processed++;
        }
        return processed;
    }, cancellationToken);

    public Task<NotificationDelivery?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        var token = Guid.NewGuid();
        var expires = now.AddSeconds(30);
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
              SELECT d.id FROM notification_deliveries d
              JOIN notification_channels c ON c.id=d.channel_id
              JOIN notification_events n ON n.id=d.notification_id
              WHERE d.state IN ('Pending','RetryWait') AND d.next_attempt_at_utc<={now}
                AND d.attempt_count<5 AND c.is_enabled AND c.version=d.channel_version AND n.expires_at_utc>{now}
              ORDER BY d.next_attempt_at_utc,d.id LIMIT 1 FOR UPDATE OF d SKIP LOCKED
            ) UPDATE notification_deliveries d SET state='Leased',lease_token={token},lease_expires_at_utc={expires}
              FROM candidate WHERE d.id=candidate.id RETURNING d.id AS "Value"
            """).ToArrayAsync(cancellationToken);
        return ids.Length == 0 ? null : await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
    }, cancellationToken);

    public Task<bool> BeginSendAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        var item = await LockAsync(id, cancellationToken);
        if (item is null || !await CurrentAsync(item, now, cancellationToken) || !NotificationDispatchPolicy.BeginSend(item, token, now)) return false;
        await PersistAsync(item, cancellationToken); return true;
    }, cancellationToken);

    public Task<bool> FinishAsync(Guid id, Guid token, DateTimeOffset now, NotificationTransportResult result, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        var item = await LockAsync(id, cancellationToken);
        if (item is null || !NotificationDispatchPolicy.Finish(item, token, now, result)) return false;
        await PersistAsync(item, cancellationToken); return true;
    }, cancellationToken);

    public Task<int> RecoverBatchAsync(DateTimeOffset now, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        var batch = await db.NotificationDeliveries.FromSqlInterpolated($"""
            SELECT d.* FROM notification_deliveries d
            WHERE (d.state IN ('Leased','Sending') AND d.lease_expires_at_utc<={now})
              OR (d.state IN ('Pending','RetryWait','Leased') AND
                (NOT EXISTS (SELECT 1 FROM notification_channels c WHERE c.id=d.channel_id AND c.is_enabled AND c.version=d.channel_version)
                 OR NOT EXISTS (SELECT 1 FROM notification_events n WHERE n.id=d.notification_id AND n.expires_at_utc>{now})))
            ORDER BY d.created_at_utc,d.id LIMIT 100 FOR UPDATE OF d SKIP LOCKED
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        foreach (var item in batch)
        {
            var channelCurrent = await db.NotificationChannels.AnyAsync(channel => channel.Id == item.ChannelId && channel.IsEnabled && channel.Version == item.ChannelVersion, cancellationToken);
            var eventCurrent = await db.NotificationEvents.AnyAsync(source => source.Id == item.NotificationId && source.ExpiresAtUtc > now, cancellationToken);
            NotificationDispatchPolicy.Recover(item, now, channelCurrent, eventCurrent);
            await PersistAsync(item, cancellationToken);
        }
        return batch.Length;
    }, cancellationToken);

    private Task<NotificationDelivery?> LockAsync(Guid id, CancellationToken cancellationToken) =>
        db.NotificationDeliveries.FromSqlInterpolated($"SELECT * FROM notification_deliveries WHERE id={id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> CurrentAsync(NotificationDelivery item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var channels = await db.NotificationChannels.FromSqlInterpolated($"""
            SELECT * FROM notification_channels WHERE id={item.ChannelId} AND is_enabled AND version={item.ChannelVersion} FOR SHARE
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (channels.Length == 0) return false;
        var events = await db.NotificationEvents.FromSqlInterpolated($"""
            SELECT * FROM notification_events WHERE id={item.NotificationId} AND expires_at_utc>{now} FOR KEY SHARE
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        return events.Length > 0;
    }

    private Task<int> PersistAsync(NotificationDelivery item, CancellationToken cancellationToken) => db.NotificationDeliveries.Where(row => row.Id == item.Id)
        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.State, item.State)
            .SetProperty(row => row.AttemptCount, item.AttemptCount).SetProperty(row => row.NextAttemptAtUtc, item.NextAttemptAtUtc)
            .SetProperty(row => row.LeaseToken, item.LeaseToken).SetProperty(row => row.LeaseExpiresAtUtc, item.LeaseExpiresAtUtc)
            .SetProperty(row => row.LastAttemptAtUtc, item.LastAttemptAtUtc).SetProperty(row => row.CompletedAtUtc, item.CompletedAtUtc)
            .SetProperty(row => row.LastResultCode, item.LastResultCode), cancellationToken);

    private Task<T> InTransaction<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s';", cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken); return result;
    });
}
