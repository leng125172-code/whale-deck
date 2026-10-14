using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class NotificationRepository(PlatformDbContext db) : INotificationRepository
{
    private IQueryable<NotificationEvent> Visible(NotificationContext context, DateTimeOffset now) => db.NotificationEvents.AsNoTracking()
        .Where(item => item.ExpiresAtUtc > now && ((item.Audience == "Administrator" && context.IsAdministrator) ||
            (item.Audience == "Subject" && item.RecipientSubject == context.Subject)));

    public async Task<NotificationPageDto> ListAsync(NotificationContext context, NotificationQuery query, CancellationToken cancellationToken)
    {
        var visible = Visible(context, DateTimeOffset.UtcNow);
        var snapshot = await visible.MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0;
        var filtered = visible.Where(item => item.Sequence <= snapshot);
        if (query.Before is { } before) filtered = filtered.Where(item => item.Sequence < before);
        if (query.Kind != "all") filtered = filtered.Where(item => item.SourceKind == query.Kind);
        if (query.Status == "unread") filtered = filtered.Where(item => !db.NotificationReadStates.Any(read => read.NotificationId == item.Id && read.Subject == context.Subject));
        var rows = await filtered.OrderByDescending(item => item.Sequence).Take(query.Take + 1).Select(item => new
        {
            Event = item,
            ReadAt = db.NotificationReadStates.Where(read => read.NotificationId == item.Id && read.Subject == context.Subject)
                .Select(read => (DateTimeOffset?)read.ReadAtUtc).FirstOrDefault()
        }).ToArrayAsync(cancellationToken);
        var page = rows.Take(query.Take).Select(item =>
        {
            var text = NotificationSources.Text(item.Event);
            var target = !context.IsAdministrator ? null : item.Event.SourceKind == "Alert" ? "/alerts" : $"/jobs?jobId={item.Event.SourceId:D}";
            return new NotificationDto(item.Event.Id, item.Event.Sequence, item.Event.SourceKind, item.Event.SourceId, item.Event.MessageCode, item.Event.Severity,
                text.Title, text.Summary, item.Event.OccurredAtUtc, item.Event.PublishedAtUtc, item.ReadAt, target);
        }).ToArray();
        return new(page, rows.Length > query.Take ? page[^1].Sequence : null, snapshot);
    }

    public async Task<NotificationSummaryDto> SummaryAsync(NotificationContext context, CancellationToken cancellationToken)
    {
        var result = await Visible(context, DateTimeOffset.UtcNow).GroupBy(_ => 1).Select(group => new NotificationSummaryDto(
            group.LongCount(item => !db.NotificationReadStates.Any(read => read.NotificationId == item.Id && read.Subject == context.Subject)),
            group.LongCount(), group.Max(item => item.Sequence))).SingleOrDefaultAsync(cancellationToken);
        return result ?? new(0, 0, 0);
    }

    public Task<NotificationReadDto> SetReadAsync(NotificationContext context, Guid id, bool isRead, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginWriteAsync(context.Subject, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            // Hold the fact while writing its FK read state; concurrent retention cannot turn an expired read into a 500.
            var visible = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM notification_events
                WHERE id={id} AND expires_at_utc>{now}
                  AND ((audience='Administrator' AND {context.IsAdministrator}) OR (audience='Subject' AND recipient_subject={context.Subject}))
                FOR KEY SHARE
                """).ToArrayAsync(cancellationToken);
            if (visible.Length == 0) throw new KeyNotFoundException("Notification was not found.");
            var changed = isRead
                ? await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_read_states (notification_id, subject, read_at_utc) VALUES ({id}, {context.Subject}, {now}) ON CONFLICT (subject, notification_id) DO NOTHING", cancellationToken)
                : await db.NotificationReadStates.Where(item => item.NotificationId == id && item.Subject == context.Subject).ExecuteDeleteAsync(cancellationToken);
            if (changed > 0) Audit(context, isRead ? "notifications.read" : "notifications.unread", id.ToString("D"));
            await db.SaveChangesAsync(cancellationToken);
            var readAt = await db.NotificationReadStates.AsNoTracking().Where(item => item.NotificationId == id && item.Subject == context.Subject)
                .Select(item => (DateTimeOffset?)item.ReadAtUtc).SingleOrDefaultAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new NotificationReadDto(id, readAt is not null, readAt);
        });

    public Task<PrepareNotificationsReadDto> PrepareReadAllAsync(NotificationContext context, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginWriteAsync(context.Subject, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var expires = now.AddMinutes(5);
            var id = Guid.NewGuid();
            // Invalidate immediately, but never cascade a large old set inside a user's request.
            // Retention removes superseded members in bounded batches without undoing read states.
            await db.NotificationReadSnapshots.Where(item => item.Subject == context.Subject && item.IsCurrent)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsCurrent, false)
                    .SetProperty(item => item.ExpiresAtUtc, now), cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_read_snapshots (id,subject,is_administrator,item_count,created_at_utc,expires_at_utc)
                VALUES ({id},{context.Subject},{context.IsAdministrator},0,{now},{expires})
                """, cancellationToken);
            // This single statement materializes the actually visible committed set, not sequence allocation order.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_read_snapshot_items (snapshot_id,notification_id,is_processed)
                SELECT {id},n.id,false FROM notification_events n
                WHERE n.expires_at_utc>{now}
                  AND ((n.audience='Administrator' AND {context.IsAdministrator}) OR (n.audience='Subject' AND n.recipient_subject={context.Subject}))
                  AND NOT EXISTS (SELECT 1 FROM notification_read_states r WHERE r.notification_id=n.id AND r.subject={context.Subject})
                ORDER BY n.sequence DESC FOR KEY SHARE OF n
                """, cancellationToken);
            var count = await db.NotificationReadSnapshotItems.LongCountAsync(item => item.SnapshotId == id, cancellationToken);
            await db.NotificationReadSnapshots.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ItemCount, count), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PrepareNotificationsReadDto(id, count, expires);
        });

    public Task<MarkNotificationsReadDto> MarkAllReadAsync(NotificationContext context, Guid snapshotId, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginWriteAsync(context.Subject, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var headers = await db.NotificationReadSnapshots.FromSqlInterpolated($"""
                SELECT * FROM notification_read_snapshots WHERE id={snapshotId} AND subject={context.Subject} AND is_current
                  AND is_administrator={context.IsAdministrator} AND expires_at_utc>{now} FOR KEY SHARE
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (headers.Length == 0) throw new NotificationSnapshotUnavailableException();
            if (headers[0].CompletedAtUtc is not null) return new MarkNotificationsReadDto(0);
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT n.id AS "Value" FROM notification_read_snapshot_items m
                JOIN notification_events n ON n.id=m.notification_id
                WHERE m.snapshot_id={snapshotId} AND NOT m.is_processed AND n.expires_at_utc>{now}
                  AND ((n.audience='Administrator' AND {context.IsAdministrator}) OR (n.audience='Subject' AND n.recipient_subject={context.Subject}))
                ORDER BY n.sequence DESC LIMIT 1000 FOR KEY SHARE OF n
                """).ToArrayAsync(cancellationToken);
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_read_states (notification_id, subject, read_at_utc)
                SELECT id, {context.Subject}, {now} FROM notification_events
                WHERE id=ANY({ids})
                ON CONFLICT (subject, notification_id) DO NOTHING
                """, cancellationToken);
            var processed = await db.NotificationReadSnapshotItems.Where(item => item.SnapshotId == snapshotId && ids.Contains(item.NotificationId))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsProcessed, true), cancellationToken);
            if (changed > 0) Audit(context, "notifications.read-all", "self");
            await db.SaveChangesAsync(cancellationToken);
            var visible = Visible(context, now);
            var remaining = await db.NotificationReadSnapshotItems.AnyAsync(member => member.SnapshotId == snapshotId && !member.IsProcessed &&
                visible.Any(source => source.Id == member.NotificationId), cancellationToken);
            if (!remaining) await db.NotificationReadSnapshots.Where(item => item.Id == snapshotId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CompletedAtUtc, now), cancellationToken);
            else
            {
                var absoluteExpiry = headers[0].CreatedAtUtc.AddHours(1);
                var renewedExpiry = now.AddMinutes(5) < absoluteExpiry ? now.AddMinutes(5) : absoluteExpiry;
                await db.NotificationReadSnapshots.Where(item => item.Id == snapshotId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, renewedExpiry), cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new MarkNotificationsReadDto(changed, remaining, processed);
        });

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginWriteAsync(string subject, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s';", cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({subject}, 41263101))", cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private void Audit(NotificationContext context, string action, string id) => db.AuditEvents.Add(new AuditEvent
    {
        ActorSubject = context.Subject, Action = action, TargetType = "notification", TargetId = id,
        Result = "Succeeded", TraceId = context.TraceId, SourceIp = context.SourceIp
    });
}
