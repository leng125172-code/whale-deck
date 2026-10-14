using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeNotificationSnapshotTests
{
    [NativePostgresFact]
    public async Task RealPostgresMaterializedSnapshotExcludesLateCommitAndPreservesScopeProgressAndReplay()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_snapshots_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Snapshot native test requires its isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
        Console.WriteLine("INBOX_PHASE=snapshot-migrated");
        var now = DateTimeOffset.UtcNow;
        var owner = new NotificationContext("snapshot-owner", false, "native-snapshot");
        var service = new NotificationService(new NotificationRepository(db));
        NotificationEvent Event(string subject) => new()
        {
            SourceKey = Guid.NewGuid().ToString("D"), SourceKind = "Job", SourceId = Guid.NewGuid(),
            Audience = "Subject", RecipientSubject = subject, MessageCode = "JOB_SUCCEEDED", Severity = "Info",
            OccurredAtUtc = now, ExpiresAtUtc = now.AddDays(1)
        };
        await using var delayed = new PlatformDbContext(options);
        await using var delayedTransaction = await delayed.Database.BeginTransactionAsync();
        var late = Event(owner.Subject); delayed.NotificationEvents.Add(late); await delayed.SaveChangesAsync();
        var early = Event(owner.Subject); db.NotificationEvents.Add(early); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.True(late.Sequence < early.Sequence);
        var snapshot = await service.PrepareReadAllAsync(owner, default);
        Assert.Equal(1, snapshot.ItemCount);
        await delayedTransaction.CommitAsync();
        var first = await service.MarkAllReadAsync(owner, snapshot.SnapshotId, default);
        Assert.Equal(new MarkNotificationsReadDto(1, false, 1), first);
        Assert.False(await db.NotificationReadStates.AnyAsync(item => item.NotificationId == late.Id));
        Assert.True(await db.NotificationReadStates.AnyAsync(item => item.NotificationId == early.Id));
        Assert.Equal(1, (await service.SummaryAsync(owner, default)).UnreadCount);
        Assert.Equal(0, (await service.MarkAllReadAsync(owner, snapshot.SnapshotId, default)).ChangedCount);
        await service.SetReadAsync(owner, early.Id, false, default);
        Assert.Equal(0, (await service.MarkAllReadAsync(owner, snapshot.SnapshotId, default)).ChangedCount);
        Assert.Equal(2, (await service.SummaryAsync(owner, default)).UnreadCount);
        Console.WriteLine("INBOX_PHASE=snapshot-late-commit");

        var next = await service.PrepareReadAllAsync(owner, default);
        Assert.Equal(2, next.ItemCount);
        await Assert.ThrowsAsync<NotificationSnapshotUnavailableException>(() => service.MarkAllReadAsync(owner, snapshot.SnapshotId, default));
        await Assert.ThrowsAsync<NotificationSnapshotUnavailableException>(() => service.MarkAllReadAsync(owner with { Subject = "different-owner" }, next.SnapshotId, default));
        await service.SetReadAsync(owner, late.Id, true, default);
        Assert.Equal(new MarkNotificationsReadDto(1, false, 2), await service.MarkAllReadAsync(owner, next.SnapshotId, default));
        await db.NotificationReadSnapshots.Where(item => item.Id == next.SnapshotId).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now.AddSeconds(-1)));
        await Assert.ThrowsAsync<NotificationSnapshotUnavailableException>(() => service.MarkAllReadAsync(owner, next.SnapshotId, default));
        var admin = owner with { Subject = "snapshot-admin", IsAdministrator = true };
        db.NotificationEvents.Add(new() { SourceKey = "snapshot-broadcast", SourceKind = "Alert", SourceId = Guid.NewGuid(), Audience = "Administrator",
            MessageCode = "ALERT_ACTIVE", Severity = "Info", OccurredAtUtc = now, ExpiresAtUtc = now.AddDays(1) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var adminSnapshot = await service.PrepareReadAllAsync(admin, default);
        Assert.Equal(1, adminSnapshot.ItemCount);
        await Assert.ThrowsAsync<NotificationSnapshotUnavailableException>(() => service.MarkAllReadAsync(admin with { IsAdministrator = false }, adminSnapshot.SnapshotId, default));
        Console.WriteLine("INBOX_PHASE=snapshot-owned-expired");

        var batchOwner = owner with { Subject = "snapshot-batch" };
        db.NotificationEvents.AddRange(Enumerable.Range(0, 1005).Select(_ => Event(batchOwner.Subject)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var batch = await service.PrepareReadAllAsync(batchOwner, default);
        Assert.Equal(1005, batch.ItemCount);
        // Independent reads after preparation are still processed members, not a no-progress error.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_read_states (notification_id,subject,read_at_utc)
            SELECT n.id,{batchOwner.Subject},{now} FROM notification_read_snapshot_items m JOIN notification_events n ON n.id=m.notification_id
            WHERE m.snapshot_id={batch.SnapshotId} ORDER BY n.sequence DESC LIMIT 1000
            """);
        Assert.Equal(new MarkNotificationsReadDto(0, true, 1000), await service.MarkAllReadAsync(batchOwner, batch.SnapshotId, default));
        Assert.Equal(new MarkNotificationsReadDto(5, false, 5), await service.MarkAllReadAsync(batchOwner, batch.SnapshotId, default));
        Assert.Equal(0, (await service.SummaryAsync(batchOwner, default)).UnreadCount);
        var empty = await service.PrepareReadAllAsync(batchOwner, default);
        Assert.Equal(0, empty.ItemCount);
        Assert.Equal(new MarkNotificationsReadDto(0), await service.MarkAllReadAsync(batchOwner, empty.SnapshotId, default));
        var concurrentOwner = owner with { Subject = "snapshot-concurrent" };
        db.NotificationEvents.AddRange(Enumerable.Range(0, 1005).Select(_ => Event(concurrentOwner.Subject)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var concurrentSnapshot = await service.PrepareReadAllAsync(concurrentOwner, default);
        async Task<MarkNotificationsReadDto> MarkConcurrently()
        {
            await using var other = new PlatformDbContext(options);
            return await new NotificationRepository(other).MarkAllReadAsync(concurrentOwner, concurrentSnapshot.SnapshotId, default);
        }
        var concurrentResults = await Task.WhenAll(MarkConcurrently(), MarkConcurrently());
        Assert.Equal(1005, concurrentResults.Sum(item => item.ChangedCount));
        Assert.Equal(1005, concurrentResults.Sum(item => item.ProcessedCount));
        Assert.Equal(1005, await db.NotificationReadStates.CountAsync(item => item.Subject == concurrentOwner.Subject));
        // Bounded batches must not silently turn "all" into a total 100k limit.
        var largeOwner = owner with { Subject = "snapshot-large" };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification_events (id,source_key,source_kind,source_id,audience,recipient_subject,
                message_code,severity,occurred_at_utc,published_at_utc,expires_at_utc)
            SELECT md5('snapshot-large-event:'||g::text)::uuid,'snapshot-large:'||g::text,'Job',
                md5('snapshot-large-source:'||g::text)::uuid,'Subject',{largeOwner.Subject},'JOB_SUCCEEDED','Info',
                {now},{now},{now.AddDays(1)} FROM generate_series(1,100005) g
            """);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var large = await service.PrepareReadAllAsync(largeOwner, default);
        Assert.Equal(100005, large.ItemCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        Assert.Equal(new MarkNotificationsReadDto(1000, true, 1000), await service.MarkAllReadAsync(largeOwner, large.SnapshotId, default));
        Console.WriteLine("INBOX_PHASE=snapshot-large");
        await db.NotificationReadSnapshots.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now.AddSeconds(-1)));
        await db.NotificationEvents.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now.AddSeconds(-1)));
        var removed = 0;
        for (var batchIndex = 0; batchIndex < 103; batchIndex++)
        {
            var count = await NotificationRetention.PruneAsync(db, now, default);
            Assert.InRange(count, 0, 1000); removed += count;
            if (count == 0) break;
        }
        Assert.Equal(102018, removed);
        Assert.Empty(await db.NotificationReadSnapshotItems.ToArrayAsync());
        Assert.Empty(await db.NotificationReadSnapshots.ToArrayAsync());
        Assert.Empty(await db.NotificationReadStates.ToArrayAsync());
        Console.WriteLine("INBOX_PHASE=snapshot-retained");
    }
}
