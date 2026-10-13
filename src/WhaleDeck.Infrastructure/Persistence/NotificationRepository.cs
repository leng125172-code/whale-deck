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

    public Task<MarkNotificationsReadDto> MarkAllReadAsync(NotificationContext context, long throughSequence, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await BeginWriteAsync(context.Subject, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var latest = await Visible(context, now).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0;
            if (throughSequence > latest) throw new ArgumentException("Notification snapshot is no longer valid.");
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_read_states (notification_id, subject, read_at_utc)
                SELECT id, {context.Subject}, {now} FROM notification_events
                WHERE expires_at_utc > {now} AND sequence <= {throughSequence}
                  AND ((audience = 'Administrator' AND {context.IsAdministrator}) OR (audience = 'Subject' AND recipient_subject = {context.Subject}))
                  AND NOT EXISTS (SELECT 1 FROM notification_read_states r WHERE r.notification_id = notification_events.id AND r.subject = {context.Subject})
                ORDER BY sequence DESC LIMIT 1000 FOR KEY SHARE
                ON CONFLICT (subject, notification_id) DO NOTHING
                """, cancellationToken);
            if (changed > 0) Audit(context, "notifications.read-all", "self");
            await db.SaveChangesAsync(cancellationToken);
            var remaining = await Visible(context, now).AnyAsync(item => item.Sequence <= throughSequence &&
                !db.NotificationReadStates.Any(read => read.NotificationId == item.Id && read.Subject == context.Subject), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MarkNotificationsReadDto(changed, remaining);
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
