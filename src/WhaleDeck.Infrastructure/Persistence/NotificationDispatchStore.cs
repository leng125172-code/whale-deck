using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Internal transactional outbox. Transport I/O must never run inside these retryable transactions.</summary>
public sealed class NotificationDispatchStore(PlatformDbContext db) : INotificationDispatchRepository
{
    public async Task<IReadOnlyCollection<NotificationChannel>> EnabledChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = await db.NotificationChannels.AsNoTracking().Where(item => item.IsEnabled).Take(33).ToArrayAsync(cancellationToken);
        if (channels.Length > 32) throw new InvalidOperationException("Notification channel budget exceeded.");
        return channels;
    }
    public Task<NotificationEvent?> MessageAsync(Guid id, CancellationToken cancellationToken) =>
        db.NotificationEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<int> RouteBatchAsync(DateTimeOffset now, CancellationToken cancellationToken, IReadOnlyDictionary<Guid, string>? revisions = null) => InTransaction(async () =>
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
                var revision = revisions is not null && revisions.TryGetValue(channel.Id, out var binding) ? binding : null;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO notification_deliveries
                      (id,notification_id,channel_id,channel_version,provider,destination_reference,credential_reference,configuration_revision,
                       state,attempt_count,created_at_utc,next_attempt_at_utc)
                    VALUES ({id},{item.Id},{channel.Id},{channel.Version},{channel.Provider},{channel.DestinationReference},
                      {channel.CredentialReference},{revision},'Pending',0,{now},{now})
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

    public Task<NotificationDelivery?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return InTransaction(async () =>
        {
            // Stable per invocation, including a retry after an unknown commit outcome.
            var prior = await db.NotificationDeliveries.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken);
            if (prior is not null) return prior;
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
    }

    public Task<bool> BeginSendAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken, string? configurationRevision = null, int minimumIntervalSeconds = 0, string? targetIdentity = null)
    {
        if (minimumIntervalSeconds is < 0 or > 300) throw new ArgumentOutOfRangeException(nameof(minimumIntervalSeconds));
        var began = false;
        return InTransaction(async () =>
        {
            var item = await LockAsync(id, cancellationToken);
            if (began && item is { State: "Sending" } && item.LeaseToken == token && item.LeaseExpiresAtUtc > now) return true;
            if (item is null || item.State != "Leased" || item.LeaseToken != token || item.LeaseExpiresAtUtc <= now) return false;
            if (configurationRevision is not null && item.ConfigurationRevision != configurationRevision)
            { await Reject(item, now, "ConfigurationChanged", cancellationToken); return false; }
            // FOR UPDATE in CurrentAsync serializes the rate gate across Worker instances.
            if (!await CurrentAsync(item, now, cancellationToken)) return false;
            var channel = await db.NotificationChannels.AsNoTracking().SingleAsync(value => value.Id == item.ChannelId, cancellationToken);
            var next = channel.NextDeliveryAtUtc;
            if (targetIdentity is not null && minimumIntervalSeconds > 0)
            {
                if (targetIdentity.Length != 64 || targetIdentity.Any(value => value is not (>= 'A' and <= 'F' or >= '0' and <= '9')))
                    throw new ArgumentException("Invalid notification target identity.");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_target_rate_gates (id,next_at_utc) VALUES ({targetIdentity},{now}) ON CONFLICT DO NOTHING", cancellationToken);
                var gate = await db.NotificationTargetRateGates.FromSqlInterpolated($"SELECT * FROM notification_target_rate_gates WHERE id={targetIdentity} FOR UPDATE").AsNoTracking().SingleAsync(cancellationToken);
                if (next is null || gate.NextAtUtc > next) next = gate.NextAtUtc;
            }
            if (minimumIntervalSeconds > 0 && next > now)
            {
                item.State = "RetryWait"; item.NextAttemptAtUtc = next.Value;
                item.LastResultCode = "RateDeferred"; item.LeaseToken = null; item.LeaseExpiresAtUtc = null;
                await PersistAsync(item, cancellationToken); return false;
            }
            if (!NotificationDispatchPolicy.BeginSend(item, token, now)) return false;
            await PersistAsync(item, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_delivery_attempts (delivery_id,attempt_number,started_at_utc)
            VALUES ({item.Id},{item.AttemptCount},{now})
            """, cancellationToken);
            if (minimumIntervalSeconds > 0)
            {
                await db.NotificationChannels.Where(value => value.Id == item.ChannelId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.NextDeliveryAtUtc, now.AddSeconds(minimumIntervalSeconds)), cancellationToken);
                if (targetIdentity is not null) await db.NotificationTargetRateGates.Where(value => value.Id == targetIdentity)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.NextAtUtc, now.AddSeconds(minimumIntervalSeconds)), cancellationToken);
            }
            began = true; return true;
        }, cancellationToken);
    }

    public Task<bool> FinishAsync(Guid id, Guid token, DateTimeOffset now, NotificationTransportResult result, CancellationToken cancellationToken)
    {
        var finished = false;
        return InTransaction(async () =>
        {
            if (finished && await db.NotificationDeliveryAttempts.AnyAsync(value => value.DeliveryId == id && value.FinishedAtUtc == now && value.ResultCode == result.ToString(), cancellationToken)) return true;
            var item = await LockAsync(id, cancellationToken);
            if (item is null || !NotificationDispatchPolicy.Finish(item, token, now, result)) return false;
            await PersistAsync(item, cancellationToken);
            await EndAttempt(item, now, item.LastResultCode!, cancellationToken); finished = true; return true;
        }, cancellationToken);
    }

    public Task<int> RecoverBatchAsync(DateTimeOffset now, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        // Old target gates cannot carry a live <=300-second reservation after an hour.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM notification_target_rate_gates WHERE id IN
              (SELECT id FROM notification_target_rate_gates WHERE next_at_utc<{now.AddHours(-1)} ORDER BY next_at_utc LIMIT 100 FOR UPDATE SKIP LOCKED)
            """, cancellationToken);
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
            if (item.State == "Unknown") await EndAttempt(item, now, "LeaseExpiredAfterSend", cancellationToken);
        }
        return batch.Length;
    }, cancellationToken);

    public Task<bool> RejectBeforeSendAsync(Guid id, Guid token, DateTimeOffset now, string code, CancellationToken cancellationToken) => InTransaction(async () =>
    {
        if (code is not ("ConfigurationMissing" or "ConfigurationInvalid" or "BindingMissing" or "NetworkDenied" or "ConfigurationChanged" or "EventUnavailable"))
            throw new ArgumentException("Invalid notification pre-send code.");
        var item = await LockAsync(id, cancellationToken);
        if (item is null || item.State != "Leased" || item.LeaseToken != token || item.LeaseExpiresAtUtc <= now) return false;
        await Reject(item, now, code, cancellationToken); return true;
    }, cancellationToken);

    private Task<int> Reject(NotificationDelivery item, DateTimeOffset now, string code, CancellationToken cancellationToken)
    {
        item.State = code is "ConfigurationChanged" or "EventUnavailable" ? "Canceled" : "Failed";
        item.LastResultCode = code; item.CompletedAtUtc = now; item.LeaseToken = null; item.LeaseExpiresAtUtc = null;
        return PersistAsync(item, cancellationToken);
    }
    private Task<int> EndAttempt(NotificationDelivery item, DateTimeOffset now, string code, CancellationToken cancellationToken) =>
        db.NotificationDeliveryAttempts.Where(value => value.DeliveryId == item.Id && value.AttemptNumber == item.AttemptCount && value.FinishedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FinishedAtUtc, now).SetProperty(value => value.ResultCode, code), cancellationToken);

    private Task<NotificationDelivery?> LockAsync(Guid id, CancellationToken cancellationToken) =>
        db.NotificationDeliveries.FromSqlInterpolated($"SELECT * FROM notification_deliveries WHERE id={id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> CurrentAsync(NotificationDelivery item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var channels = await db.NotificationChannels.FromSqlInterpolated($"""
            SELECT * FROM notification_channels WHERE id={item.ChannelId} AND is_enabled AND version={item.ChannelVersion} FOR UPDATE
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
