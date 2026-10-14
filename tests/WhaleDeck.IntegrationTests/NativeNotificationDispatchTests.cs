using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Services;
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
    }
}
