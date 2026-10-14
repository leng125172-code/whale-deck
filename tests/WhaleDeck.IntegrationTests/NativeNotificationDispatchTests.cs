using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Services;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeNotificationDispatchTests
{
    [NativePostgresFact]
    public async Task RealPostgresLateCommitFanoutLeaseRecoveryVersionAndRetention()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_dispatch_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native dispatch test requires its isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        Console.WriteLine("INBOX_PHASE=dispatch-migrated");
        var now = DateTimeOffset.UtcNow;
        var channel = new NotificationChannel { Name = "isolated-test", Provider = "Webhook", DestinationReference = "approved-test", IsEnabled = true, CreatedAtUtc = now.AddMinutes(-1) };
        db.NotificationChannels.Add(channel);
        for (var index = 0; index < 2; index++) db.NotificationRoutes.Add(new()
        { ChannelId = channel.Id, SourceKind = "Alert", MinimumSeverity = "Info", IsEnabled = true, CreatedAtUtc = now.AddMinutes(-1) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        NotificationEvent Event(string key, string audience = "Administrator", DateTimeOffset? published = null, DateTimeOffset? expires = null) => new()
        {
            SourceKey = key, SourceKind = "Alert", Audience = audience, RecipientSubject = audience == "Subject" ? "ordinary-subject" : null,
            MessageCode = "ALERT_ACTIVE", Severity = "Critical", PublishedAtUtc = published ?? now, OccurredAtUtc = now,
            ExpiresAtUtc = expires ?? now.AddDays(1)
        };

        // Sequence allocation is not commit order. The lower sequence remains uncommitted until after routing the higher.
        await using var delayed = new PlatformDbContext(options);
        await using var delayedTransaction = await delayed.Database.BeginTransactionAsync();
        var late = Event("late-commit"); delayed.NotificationEvents.Add(late); await delayed.SaveChangesAsync();
        var early = Event("early-commit"); var personal = Event("personal", "Subject");
        var old = Event("before-route", published: now.AddMinutes(-2)); var expired = Event("expired", expires: now.AddSeconds(-1));
        db.NotificationEvents.AddRange(early, personal, old, expired); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var store = new NotificationDispatchStore(db);
        Assert.Equal(3, await store.RouteBatchAsync(now, default));
        Assert.Single(await db.NotificationDeliveries.ToArrayAsync());
        await delayedTransaction.CommitAsync();
        Assert.Equal(1, await store.RouteBatchAsync(now, default));
        Assert.Equal(0, await store.RouteBatchAsync(now, default));
        Assert.Equal(2, await db.NotificationDeliveries.CountAsync());
        Assert.Equal("Personal", (await db.NotificationDispatchReceipts.AsNoTracking().SingleAsync(item => item.NotificationId == personal.Id)).Outcome);
        Assert.Equal("NoRoute", (await db.NotificationDispatchReceipts.AsNoTracking().SingleAsync(item => item.NotificationId == old.Id)).Outcome);
        Assert.False(await db.NotificationDispatchReceipts.AnyAsync(item => item.NotificationId == expired.Id));
        Console.WriteLine("INBOX_PHASE=dispatch-late-commit");

        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var concurrent = new PlatformDbContext(options);
            return await new NotificationDispatchStore(concurrent).ClaimAsync(now, default);
        }));
        var leased = claims.OfType<NotificationDelivery>().ToArray();
        Assert.Equal(2, leased.Length); Assert.Equal(2, leased.Select(item => item.Id).Distinct().Count());
        Assert.False(await store.BeginSendAsync(leased[0].Id, Guid.NewGuid(), now, default));
        Assert.True(await store.BeginSendAsync(leased[0].Id, leased[0].LeaseToken!.Value, now, default));
        // One prepare lease and one sending lease expire: only the prepare lease is safe to repeat.
        Assert.Equal(2, await store.RecoverBatchAsync(now.AddMinutes(2), default));
        Assert.Equal("Unknown", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == leased[0].Id)).State);
        Assert.Equal("Pending", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == leased[1].Id)).State);
        Assert.False(await store.FinishAsync(leased[0].Id, leased[0].LeaseToken!.Value, now.AddMinutes(2), NotificationTransportResult.Accepted, default));

        var retry = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(2), default));
        Assert.Equal(leased[1].Id, retry.Id);
        Assert.True(await store.BeginSendAsync(retry.Id, retry.LeaseToken!.Value, now.AddMinutes(2), default));
        Assert.True(await store.FinishAsync(retry.Id, retry.LeaseToken!.Value, now.AddMinutes(2).AddSeconds(1), NotificationTransportResult.RetryableNotSent, default));
        Assert.Null(await store.ClaimAsync(now.AddMinutes(2).AddSeconds(2), default));
        var secondAttempt = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(3), default));
        Assert.True(await store.BeginSendAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), default));
        Assert.True(await store.FinishAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), NotificationTransportResult.Accepted, default));
        Assert.False(await store.FinishAsync(secondAttempt.Id, secondAttempt.LeaseToken!.Value, now.AddMinutes(3), NotificationTransportResult.Accepted, default));
        var succeeded = await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == secondAttempt.Id);
        Assert.Equal("Succeeded", succeeded.State); Assert.Equal(2, succeeded.AttemptCount);
        Console.WriteLine("INBOX_PHASE=dispatch-leases");

        var changed = Event("channel-changed"); db.NotificationEvents.Add(changed); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(1, await store.RouteBatchAsync(now.AddMinutes(4), default));
        var stale = Assert.IsType<NotificationDelivery>(await store.ClaimAsync(now.AddMinutes(4), default));
        await db.NotificationChannels.Where(item => item.Id == channel.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, 1L));
        Assert.False(await store.BeginSendAsync(stale.Id, stale.LeaseToken!.Value, now.AddMinutes(4), default));
        Assert.Equal(1, await store.RecoverBatchAsync(now.AddMinutes(4), default));
        Assert.Equal("Canceled", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(item => item.Id == stale.Id)).State);

        Assert.Equal(6, await NotificationRetention.PruneAsync(db, now.AddDays(2), default));
        Assert.Empty(await db.NotificationDeliveries.ToArrayAsync());
        Assert.Empty(await db.NotificationDispatchReceipts.ToArrayAsync());
        Assert.Single(await db.NotificationChannels.ToArrayAsync());
        Console.WriteLine("INBOX_PHASE=dispatch-retained");
        await Management(options, db, store, now);
    }

    private static async Task Management(DbContextOptions<PlatformDbContext> options, PlatformDbContext db, NotificationDispatchStore store, DateTimeOffset now)
    {
        var repository = new NotificationManagementRepository(db); var service = new NotificationManagementService(repository);
        NotificationContext Context(string actor = "native-management") => new(actor, true, "native-trace", "127.0.0.1", Guid.NewGuid().ToString());
        var context = Context(); var create = new SaveNotificationChannelCommand("Managed", "Webhook", "approved-target", "private-reference", Confirmed: true);
        var channel = await service.SaveChannelAsync(context, null, create, default);
        Assert.Equal(channel.Id, (await service.SaveChannelAsync(context, null, create, default)).Id);
        Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.ActorSubject == context.Subject));
        Assert.Equal(1, await db.NotificationChannels.CountAsync(item => item.Id == channel.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveChannelAsync(context, null, create with { Name = "Different" }, default));
        var updateContext = Context();
        var update = create with { Name = "Updated", CredentialReference = null, KeepCredential = true, ExpectedVersion = channel.Version };
        var updated = await service.SaveChannelAsync(updateContext, channel.Id, update, default);
        Assert.Equal(1, updated.Version); Assert.True(updated.HasCredential);
        Assert.Equal("private-reference", (await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id)).CredentialReference);
        Assert.Equal(1, (await service.SaveChannelAsync(updateContext, channel.Id, update, default)).Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveChannelAsync(Context(), channel.Id, update, default));

        var routeContext = Context(); var routeCommand = new SaveNotificationRouteCommand(channel.Id, Confirmed: true);
        var route = await service.SaveRouteAsync(routeContext, null, routeCommand, default);
        Assert.Equal(route.Id, (await service.SaveRouteAsync(routeContext, null, routeCommand, default)).Id);
        Assert.Equal(2, (await db.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == channel.Id)).Version);
        // Overlapping routes are legal; dispatch deduplicates by event and channel.
        var overlap = await service.SaveRouteAsync(Context(), null, routeCommand, default);
        var changedRoute = await service.SaveRouteAsync(Context(), route.Id, routeCommand with { MinimumSeverity = "Error", ExpectedVersion = route.Version }, default);
        Assert.Equal(1, changedRoute.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveRouteAsync(Context(), route.Id, routeCommand with { ExpectedVersion = 0 }, default));
        var deleteContext = Context();
        await service.DeleteRouteAsync(deleteContext, route.Id, new(1, true), default);
        await service.DeleteRouteAsync(deleteContext, route.Id, new(1, true), default);
        Assert.False(await db.NotificationRoutes.AnyAsync(item => item.Id == route.Id));
        await service.DeleteRouteAsync(Context(), overlap.Id, new(overlap.Version, true), default);

        var pageOne = await service.ListChannelsAsync(Context(), new(Take: 1), default);
        var pageTwo = await service.ListChannelsAsync(Context(), new(Page: 2, Take: 1), default);
        Assert.True(pageOne.HasMore); Assert.NotEqual(pageOne.Items.Single().Id, pageTwo.Items.Single().Id);
        var enabled = await service.SaveChannelAsync(Context(), channel.Id, update with { ExpectedVersion = 6, IsEnabled = true }, default);
        Assert.Equal(7, enabled.Version);
        var enabledRoute = await service.SaveRouteAsync(Context(), null, new(channel.Id, MinimumSeverity: "Info", IsEnabled: true, Confirmed: true), default);
        var item = new NotificationEvent { SourceKey = "management-version", SourceKind = "Alert", Audience = "Administrator", MessageCode = "ALERT_ACTIVE", Severity = "Critical",
            PublishedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1), OccurredAtUtc = now, ExpiresAtUtc = now.AddDays(1) };
        db.NotificationEvents.Add(item); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await store.RouteBatchAsync(now.AddMinutes(5), default);
        Assert.True(await db.NotificationDeliveries.AnyAsync(delivery => delivery.ChannelId == channel.Id));
        await service.SaveRouteAsync(Context(), enabledRoute.Id, new(channel.Id, IsEnabled: false, ExpectedVersion: 0, Confirmed: true), default);
        await store.RecoverBatchAsync(now.AddMinutes(5), default);
        Assert.Equal("Canceled", (await db.NotificationDeliveries.AsNoTracking().SingleAsync(delivery => delivery.ChannelId == channel.Id)).State);
        var deliveries = await service.ListDeliveriesAsync(Context(), new(), channel.Id, "Canceled", default);
        Assert.Single(deliveries.Items);
        var actualVersion = (await db.NotificationChannels.AsNoTracking().SingleAsync(value => value.Id == channel.Id)).Version;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteChannelAsync(Context(), channel.Id, new(actualVersion, true), default));
        await service.SaveChannelAsync(Context(), channel.Id, update with { ExpectedVersion = actualVersion, IsEnabled = false }, default);

        // Failure after the mutation but before audit insertion must roll back both.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_notification_audit_failure CHECK (actor_subject <> 'audit-failure') NOT VALID");
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => service.SaveChannelAsync(Context("audit-failure"), null, create with { Name = "Must rollback" }, default));
            Assert.False(await db.NotificationChannels.AnyAsync(value => value.Name == "Must rollback"));
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_notification_audit_failure"); }

        // One pre-existing enabled channel: race two creators for the final budget slot.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO notification_channels (id,name,provider,destination_reference,is_enabled,created_at_utc,version)
            SELECT gen_random_uuid(),'budget-' || n,'Webhook','approved-target',true,now(),0 FROM generate_series(1,30) n
            """);
        var successes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            await using var concurrent = new PlatformDbContext(options);
            try { await new NotificationManagementService(new NotificationManagementRepository(concurrent)).SaveChannelAsync(Context(), null,
                create with { Name = "concurrent-" + index, IsEnabled = true }, default); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(successes, value => value); Assert.Equal(32, await db.NotificationChannels.CountAsync(value => value.IsEnabled));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_routes (id,channel_id,source_kind,minimum_severity,include_recovery,is_enabled,created_at_utc,version)
            SELECT gen_random_uuid(),{channel.Id},'Alert','Warning',true,true,now(),0 FROM generate_series(1,510)
            """);
        Assert.Equal(512, await db.NotificationRoutes.CountAsync(value => value.IsEnabled));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveRouteAsync(Context(), null, new(channel.Id, IsEnabled: true, Confirmed: true), default));
        Assert.DoesNotContain("private-reference", string.Join(' ', await db.AuditEvents.Select(value => value.DetailJson).ToArrayAsync()));
        Console.WriteLine("INBOX_PHASE=dispatch-management");
    }
}
