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
                item.AttemptCount, item.CreatedAtUtc, item.NextAttemptAtUtc, item.LastAttemptAtUtc, item.CompletedAtUtc, item.LastResultCode,
                item.Version, item.IntentKind, item.OriginDeliveryId, db.NotificationDeliveries.Any(child => child.OriginDeliveryId == item.Id))).ToArrayAsync(cancellationToken);
        return Page(rows, query);
    }

    public Task<NotificationChannelDto> SaveChannelAsync(NotificationContext context, Guid? id, SaveNotificationChannelCommand command, CancellationToken cancellationToken)
    {
        var target = id ?? StableId(context, "channel");
        return Write(context, id is null ? "notification.channel.create" : "notification.channel.update", "notification-channel", target, command, async () =>
        {
            var existing = id is null ? null : await Channel(target, cancellationToken);
            if (existing is not null) Version(command.ExpectedVersion, existing.Version);
            if (command.IsEnabled && existing?.PrivateConfigurationBlocked == true) throw new InvalidOperationException("Private configuration must be successfully reconciled before enabling this channel.");
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
            if (await db.NotificationPrivateOperations.AnyAsync(operation => operation.ChannelId == id, cancellationToken)) throw new InvalidOperationException("Private operation history prevents channel deletion; disable it instead.");
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

    public Task<NotificationSubmissionDto> TestAsync(NotificationContext context, Guid channelId, TestNotificationCommand command, CancellationToken cancellationToken)
    {
        var id = StableId(context, "test");
        return Write(context, "notification.test.request", "notification-channel", channelId, command, async () =>
        {
            var channel = await Channel(channelId, cancellationToken); Version(command.ExpectedChannelVersion, channel.Version);
            if (channel.PrivateConfigurationBlocked) throw new InvalidOperationException("Reconcile private configuration before testing this channel.");
            var now = DateTimeOffset.UtcNow; var expires = now.AddMinutes(5); var sourceKey = "notification-test:" + id.ToString("N");
            if (await db.NotificationDeliveries.CountAsync(item => item.ChannelId == channelId && item.IntentKind == "Test" && item.CreatedAtUtc > now.AddMinutes(-5), cancellationToken) >= 10)
                throw new InvalidOperationException("Notification testing budget exceeded; at most ten per channel in five minutes.");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_events (id,source_key,source_kind,source_id,audience,message_code,severity,occurred_at_utc,published_at_utc,expires_at_utc)
                VALUES ({id},{sourceKey},'NotificationTest',{channelId},'Administrator','NOTIFICATION_TEST','Info',{now},{now},{expires})
                """, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_deliveries (id,notification_id,channel_id,channel_version,provider,destination_reference,credential_reference,
                  state,attempt_count,created_at_utc,next_attempt_at_utc,intent_kind,requested_by_subject,version)
                VALUES ({id},{id},{channel.Id},{channel.Version},{channel.Provider},{channel.DestinationReference},{channel.CredentialReference},
                  'Pending',0,{now},{now},'Test',{context.Subject},0)
                """, cancellationToken);
            return new NotificationSubmissionDto(id, id, "Pending", "Test", null);
        }, () => Submission(id, cancellationToken), cancellationToken);
    }

    public Task<NotificationSubmissionDto> RetryUnknownAsync(NotificationContext context, Guid deliveryId, RetryUnknownNotificationCommand command, CancellationToken cancellationToken)
    {
        var id = StableId(context, "manual-retry");
        return Write(context, "notification.unknown.retry", "notification-delivery", deliveryId, command, async () =>
        {
            var original = await db.NotificationDeliveries.FromSqlInterpolated($"SELECT * FROM notification_deliveries WHERE id={deliveryId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Notification delivery was not found.");
            Version(command.ExpectedVersion, original.Version);
            if (original.State != "Unknown" || original.ConfigurationRevision is null || await db.NotificationDeliveries.AnyAsync(child => child.OriginDeliveryId == deliveryId, cancellationToken))
                throw new InvalidOperationException("Only an Unknown delivery without an existing manual retry can be retried.");
            var channel = await Channel(original.ChannelId, cancellationToken); Version(command.ExpectedChannelVersion, channel.Version);
            if (!channel.IsEnabled || channel.Version != original.ChannelVersion || channel.Provider != original.Provider ||
                channel.DestinationReference != original.DestinationReference || channel.CredentialReference != original.CredentialReference)
                throw new InvalidOperationException("The original approved channel configuration is no longer current.");
            var now = DateTimeOffset.UtcNow;
            if (!await db.NotificationEvents.AnyAsync(source => source.Id == original.NotificationId && source.ExpiresAtUtc > now && source.Audience == "Administrator" && source.RecipientSubject == null, cancellationToken))
                throw new InvalidOperationException("The source notification is unavailable or expired.");
            var ancestor = original; var depth = 0;
            while (ancestor.OriginDeliveryId is { } parent)
            {
                if (++depth >= 10) throw new InvalidOperationException("Manual retry chain budget exceeded.");
                ancestor = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == parent, cancellationToken);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notification_deliveries (id,notification_id,channel_id,channel_version,provider,destination_reference,credential_reference,
                  configuration_revision,state,attempt_count,created_at_utc,next_attempt_at_utc,intent_kind,requested_by_subject,origin_delivery_id,version)
                VALUES ({id},{original.NotificationId},{channel.Id},{original.ChannelVersion},{original.Provider},{original.DestinationReference},
                  {original.CredentialReference},{original.ConfigurationRevision},'Pending',0,{now},{now},'ManualRetry',{context.Subject},{deliveryId},0)
                """, cancellationToken);
            return new NotificationSubmissionDto(id, original.NotificationId, "Pending", "ManualRetry", deliveryId);
        }, () => Submission(id, cancellationToken), cancellationToken);
    }
    private async Task<NotificationSubmissionDto> Submission(Guid id, CancellationToken cancellationToken) =>
        await db.NotificationDeliveries.AsNoTracking().Where(item => item.Id == id).Select(item => new NotificationSubmissionDto(item.Id, item.NotificationId, item.State, item.IntentKind, item.OriginDeliveryId))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Notification submission was not found.");

    public async Task<IReadOnlyCollection<NotificationAttemptDto>> AttemptsAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!await db.NotificationDeliveries.AnyAsync(item => item.Id == deliveryId, cancellationToken)) throw new KeyNotFoundException("Notification delivery was not found.");
        return await db.NotificationDeliveryAttempts.AsNoTracking().Where(item => item.DeliveryId == deliveryId).OrderBy(item => item.AttemptNumber).Take(5)
            .Select(item => new NotificationAttemptDto(item.AttemptNumber, item.StartedAtUtc, item.FinishedAtUtc, item.ResultCode)).ToArrayAsync(cancellationToken);
    }
    private async Task<NotificationChannel> Channel(Guid id, CancellationToken cancellationToken)
    {
        var channel = await db.NotificationChannels.FromSqlInterpolated($"SELECT * FROM notification_channels WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Notification channel was not found.");
        if (await db.NotificationPrivateOperations.AnyAsync(item => item.ChannelId == id && (item.State == "Pending" || item.State == "Running" || item.State == "Unknown"), cancellationToken))
            throw new InvalidOperationException("Private configuration is pending or unresolved; the channel cannot be changed or tested.");
        return channel;
    }
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
