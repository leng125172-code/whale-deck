using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Configuration metadata only. Network transport never runs in these transactions.</summary>
public sealed class NotificationManagementRepository(PlatformDbContext db) : INotificationManagementRepository
{
    public async Task<NotificationManagementPage<NotificationChannelDto>> ListChannelsAsync(NotificationManagementQuery query, CancellationToken cancellationToken)
    {
        var rows = await db.NotificationChannels.AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((query.Page - 1) * query.Take).Take(query.Take + 1).Select(item => new NotificationChannelDto(item.Id, item.Name, item.Provider,
                item.DestinationReference, item.CredentialReference != null, item.IsEnabled, item.Version, item.CreatedAtUtc)).ToArrayAsync(cancellationToken);
        return Page(rows, query);
    }

    public async Task<NotificationManagementPage<NotificationRouteDto>> ListRoutesAsync(NotificationManagementQuery query, Guid? channelId, CancellationToken cancellationToken)
    {
        var rows = await db.NotificationRoutes.AsNoTracking().Where(item => channelId == null || item.ChannelId == channelId)
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Skip((query.Page - 1) * query.Take).Take(query.Take + 1)
            .Select(item => new NotificationRouteDto(item.Id, item.ChannelId, item.SourceKind, item.MinimumSeverity,
                item.IncludeRecovery, item.IsEnabled, item.Version, item.CreatedAtUtc)).ToArrayAsync(cancellationToken);
        return Page(rows, query);
    }

    public async Task<NotificationManagementPage<NotificationDeliveryDto>> ListDeliveriesAsync(NotificationManagementQuery query, Guid? channelId, string? state, CancellationToken cancellationToken)
    {
        var rows = await db.NotificationDeliveries.AsNoTracking().Where(item => (channelId == null || item.ChannelId == channelId) && (state == null || item.State == state))
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Skip((query.Page - 1) * query.Take).Take(query.Take + 1)
            .Select(item => new NotificationDeliveryDto(item.Id, item.NotificationId, item.ChannelId, item.ChannelVersion, item.Provider, item.State,
                item.AttemptCount, item.CreatedAtUtc, item.NextAttemptAtUtc, item.LastAttemptAtUtc, item.CompletedAtUtc, item.LastResultCode)).ToArrayAsync(cancellationToken);
        return Page(rows, query);
    }

    public Task<NotificationChannelDto> SaveChannelAsync(NotificationContext context, Guid? id, SaveNotificationChannelCommand command, CancellationToken cancellationToken)
    {
        var target = id ?? StableId(context, "channel");
        return Write(context, id is null ? "notification.channel.create" : "notification.channel.update", "notification-channel", target, command, async () =>
        {
            var existing = id is null ? null : await Channel(target, cancellationToken);
            if (existing is not null) Version(command.ExpectedVersion, existing.Version);
            if (command.IsEnabled && await db.NotificationChannels.CountAsync(item => item.IsEnabled && item.Id != target, cancellationToken) >= 32)
                throw new InvalidOperationException("Enabled notification channel budget exceeded.");
            if (command.KeepCredential && existing!.Provider != command.Provider) throw new ArgumentException("Provider changes require explicit credential replacement or removal.");
            var credential = command.KeepCredential ? existing!.CredentialReference : command.CredentialReference;
            var now = DateTimeOffset.UtcNow; var version = existing is null ? 0 : existing.Version + 1;
            if (existing is null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO notification_channels (id,name,provider,destination_reference,credential_reference,is_enabled,created_at_utc,version)
                    VALUES ({target},{command.Name},{command.Provider},{command.DestinationReference},{credential},{command.IsEnabled},{now},{version})
                    """, cancellationToken);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE notification_channels SET name={command.Name},provider={command.Provider},destination_reference={command.DestinationReference},
                    credential_reference={credential},is_enabled={command.IsEnabled},version={version} WHERE id={target}
                    """, cancellationToken);
            return new NotificationChannelDto(target, command.Name, command.Provider, command.DestinationReference, credential is not null,
                command.IsEnabled, version, existing?.CreatedAtUtc ?? now);
        }, async () => Map(await Channel(target, cancellationToken)), cancellationToken);
    }

    public Task<NotificationRouteDto> SaveRouteAsync(NotificationContext context, Guid? id, SaveNotificationRouteCommand command, CancellationToken cancellationToken)
    {
        var target = id ?? StableId(context, "route");
        return Write(context, id is null ? "notification.route.create" : "notification.route.update", "notification-route", target, command, async () =>
        {
            var existing = id is null ? null : await Route(target, cancellationToken);
            if (existing is not null)
            {
                Version(command.ExpectedVersion, existing.Version);
                if (existing.ChannelId != command.ChannelId) throw new ArgumentException("A notification route cannot move to another channel.");
            }
            var channel = await Channel(command.ChannelId, cancellationToken);
            if (channel.Version == long.MaxValue) throw new InvalidOperationException("Notification channel version exhausted.");
            if (command.IsEnabled && await db.NotificationRoutes.CountAsync(item => item.IsEnabled && item.Id != target, cancellationToken) >= 512)
                throw new InvalidOperationException("Enabled notification route budget exceeded.");
            var now = DateTimeOffset.UtcNow; var version = existing is null ? 0 : existing.Version + 1;
            if (existing is null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO notification_routes (id,channel_id,source_kind,minimum_severity,include_recovery,is_enabled,created_at_utc,version)
                    VALUES ({target},{command.ChannelId},{command.SourceKind},{command.MinimumSeverity},{command.IncludeRecovery},{command.IsEnabled},{now},{version})
                    """, cancellationToken);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE notification_routes SET source_kind={command.SourceKind},minimum_severity={command.MinimumSeverity},include_recovery={command.IncludeRecovery},
                    is_enabled={command.IsEnabled},version={version} WHERE id={target}
                    """, cancellationToken);
            // Explicitly confirmed routing changes invalidate pre-send intents for this channel.
            // Already Sending is never silently canceled or automatically resent.
            await InvalidateChannel(channel.Id, cancellationToken);
            return new NotificationRouteDto(target, command.ChannelId, command.SourceKind, command.MinimumSeverity,
                command.IncludeRecovery, command.IsEnabled, version, existing?.CreatedAtUtc ?? now);
        }, async () => Map(await Route(target, cancellationToken)), cancellationToken);
    }

    public Task DeleteChannelAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken) =>
        Write(context, "notification.channel.delete", "notification-channel", id, expectedVersion, async () =>
        {
            var item = await Channel(id, cancellationToken); Version(expectedVersion, item.Version);
            if (await db.NotificationDeliveries.AnyAsync(delivery => delivery.ChannelId == id, cancellationToken))
                throw new InvalidOperationException("Notification delivery history prevents channel deletion; disable it instead.");
            await db.NotificationChannels.Where(channel => channel.Id == id).ExecuteDeleteAsync(cancellationToken);
            return true;
        }, () => Task.FromResult(true), cancellationToken);

    public Task DeleteRouteAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken) =>
        Write(context, "notification.route.delete", "notification-route", id, expectedVersion, async () =>
        {
            var item = await Route(id, cancellationToken); Version(expectedVersion, item.Version);
            var channel = await Channel(item.ChannelId, cancellationToken);
            if (channel.Version == long.MaxValue) throw new InvalidOperationException("Notification channel version exhausted.");
            await db.NotificationRoutes.Where(route => route.Id == id).ExecuteDeleteAsync(cancellationToken);
            await InvalidateChannel(item.ChannelId, cancellationToken); return true;
        }, () => Task.FromResult(true), cancellationToken);

    private Task<int> InvalidateChannel(Guid id, CancellationToken cancellationToken) => db.NotificationChannels.Where(item => item.Id == id)
        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
    private async Task<NotificationChannel> Channel(Guid id, CancellationToken cancellationToken) =>
        await db.NotificationChannels.FromSqlInterpolated($"SELECT * FROM notification_channels WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Notification channel was not found.");
    private async Task<NotificationRoute> Route(Guid id, CancellationToken cancellationToken) =>
        await db.NotificationRoutes.FromSqlInterpolated($"SELECT * FROM notification_routes WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Notification route was not found.");
    private static void Version(long? expected, long actual) { if (expected != actual) throw new DbUpdateConcurrencyException("Notification configuration version changed."); }
    private static NotificationManagementPage<T> Page<T>(T[] rows, NotificationManagementQuery query) => new(rows.Take(query.Take).ToArray(), rows.Length > query.Take, query.Page, query.Take);

    private static NotificationChannelDto Map(NotificationChannel item) => new(item.Id, item.Name, item.Provider, item.DestinationReference,
        item.CredentialReference is not null, item.IsEnabled, item.Version, item.CreatedAtUtc);
    private static NotificationRouteDto Map(NotificationRoute item) => new(item.Id, item.ChannelId, item.SourceKind, item.MinimumSeverity,
        item.IncludeRecovery, item.IsEnabled, item.Version, item.CreatedAtUtc);
    private static Guid StableId(NotificationContext context, string scope) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        "whaledeck-notification:" + scope + ":" + context.Subject + ":" + Guid.Parse(context.IdempotencyKey!).ToString("D"))).AsSpan(0, 16));

    private Task<T> Write<T>(NotificationContext context, string action, string kind, Guid target, object command,
        Func<Task<T>> mutation, Func<Task<T>> replay, CancellationToken cancellationToken)
    {
        var auditId = StableId(context, "receipt"); var ready = false; T result = default!;
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { action, kind, target, command })));
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s';", cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(41263103)", cancellationToken);
            var receipt = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == auditId, cancellationToken);
            if (receipt is not null)
            {
                using var detail = JsonDocument.Parse(receipt.DetailJson ?? "{}");
                if (receipt.ActorSubject != context.Subject || receipt.Action != action || receipt.TargetId != target.ToString("D") ||
                    !detail.RootElement.TryGetProperty("requestHash", out var stored) || stored.GetString() != fingerprint)
                    throw new InvalidOperationException("The notification idempotency key was already used for another request.");
                if (!ready) result = await replay();
                await transaction.CommitAsync(cancellationToken); return result;
            }
            result = await mutation(); ready = true;
            var now = DateTimeOffset.UtcNow;
            var detailJson = JsonSerializer.Serialize(new { requestHash = fingerprint });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_events (id,actor_subject,action,target_type,target_id,result,source_ip,trace_id,occurred_at_utc,detail_json)
                VALUES ({auditId},{context.Subject},{action},{kind},{target.ToString("D")},'Succeeded',{context.SourceIp},{context.TraceId},{now},CAST({detailJson} AS jsonb))
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken); return result;
        });
    }
}
