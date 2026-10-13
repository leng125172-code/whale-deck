using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeNotificationInboxTests
{
    [NativePostgresFact]
    public async Task RealPostgresAtomicSourcesAudienceReadsPaginationConcurrencyAndRetention()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_notifications_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native test requires its isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var now = DateTimeOffset.UtcNow;
        var adminA = new NotificationContext("admin-a", true, "native-admin-a");
        var adminB = new NotificationContext("admin-b", true, "native-admin-b");
        var userA = new NotificationContext("user-a", false, "native-user-a");
        var userB = new NotificationContext("user-b", false, "native-user-b");
        var repository = new NotificationRepository(db);
        var service = new NotificationService(repository);
        Assert.Equal(new NotificationSummaryDto(0, 0, 0), await service.SummaryAsync(userA, default));

        // A terminal job and its notification commit/rollback together, including diagnostic privacy.
        var job = new OperationJob { JobType = "test.native", ActorSubject = userA.Subject, IdempotencyKey = Guid.NewGuid().ToString("D"),
            RequestJson = "{\"password\":\"private-input\"}", ResultJson = "{\"token\":\"private-result\"}" };
        db.OperationJobs.Add(job); await db.SaveChangesAsync();
        Assert.Empty(await db.NotificationEvents.ToArrayAsync());
        job.State = "Succeeded"; job.CompletedAtUtc = now;
        await db.SaveChangesAsync();
        var personal = await db.NotificationEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Subject", personal.Audience); Assert.Equal(userA.Subject, personal.RecipientSubject);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(personal));
        await db.SaveChangesAsync(); Assert.Equal(1, await db.NotificationEvents.CountAsync());
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.OperationJobs.Add(new OperationJob { JobType = "rollback.test", ActorSubject = userA.Subject, IdempotencyKey = Guid.NewGuid().ToString("D"),
                State = "Failed", CompletedAtUtc = now });
            await db.SaveChangesAsync(); Assert.Equal(2, await db.NotificationEvents.CountAsync());
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.Equal(1, await db.NotificationEvents.CountAsync()); Assert.Equal(1, await db.OperationJobs.CountAsync());

        // Real alert history produces a broadcast in the same transaction. Muted recovery stays quiet.
        var rule = new AlertRule { RuleType = "AgentOffline" };
        var alert = new AlertEvent { RuleId = rule.Id, Fingerprint = "native-notification", Severity = "Critical", SummaryCode = "native" };
        db.AlertRules.Add(rule); db.AlertEvents.Add(alert);
        db.AlertEventHistory.Add(new AlertEventHistory { AlertEventId = alert.Id, State = "Active", ActorSubject = "system:test" });
        await db.SaveChangesAsync();
        var broadcast = await db.NotificationEvents.AsNoTracking().SingleAsync(item => item.Audience == "Administrator");
        alert.State = "Silenced"; alert.SilencedUntilUtc = now.AddHours(1); await db.SaveChangesAsync();
        alert.State = "Recovered"; alert.RecoveredAtUtc = now;
        db.AlertEventHistory.Add(new AlertEventHistory { AlertEventId = alert.Id, State = "Recovered", ActorSubject = "system:test", OccurredAtUtc = now });
        await db.SaveChangesAsync(); Assert.Equal(2, await db.NotificationEvents.CountAsync());
        db.ChangeTracker.Clear();

        Assert.Single((await service.ListAsync(adminA, new(), default)).Items);
        Assert.Single((await service.ListAsync(userA, new(), default)).Items);
        Assert.Empty((await service.ListAsync(userB, new(), default)).Items);
        Assert.Null((await service.ListAsync(userA, new(), default)).Items.Single().TargetPath);
        Assert.Equal("/alerts", (await service.ListAsync(adminA, new(), default)).Items.Single().TargetPath);
        Assert.Empty((await service.ListAsync(adminA with { IsAdministrator = false }, new(), default)).Items);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetReadAsync(userB, personal.Id, true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetReadAsync(userA, broadcast.Id, true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetReadAsync(adminA with { IsAdministrator = false }, broadcast.Id, true, default));
        var firstRead = await service.SetReadAsync(adminA, broadcast.Id, true, default);
        var repeated = await service.SetReadAsync(adminA, broadcast.Id, true, default);
        Assert.Equal(firstRead.ReadAtUtc, repeated.ReadAtUtc);
        Assert.Equal(0, (await service.SummaryAsync(adminA, default)).UnreadCount);
        Assert.Equal(1, (await service.SummaryAsync(adminB, default)).UnreadCount);
        Assert.Empty((await service.ListAsync(adminA, new(Status: "unread"), default)).Items);
        await service.SetReadAsync(adminA, broadcast.Id, false, default);
        Assert.Equal(1, (await service.SummaryAsync(adminA, default)).UnreadCount);

        // Concurrent retries create exactly one personal read/audit, not a shared global read flag.
        async Task ReadConcurrently()
        {
            await using var other = new PlatformDbContext(options);
            await new NotificationRepository(other).SetReadAsync(userA, personal.Id, true, default);
        }
        await Task.WhenAll(ReadConcurrently(), ReadConcurrently());
        Assert.Equal(1, await db.NotificationReadStates.CountAsync(item => item.Subject == userA.Subject));
        Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.ActorSubject == userA.Subject && item.Action == "notifications.read"));
        Assert.All(await db.AuditEvents.Where(item => item.TargetType == "notification").ToArrayAsync(), item => Assert.Null(item.DetailJson));

        static NotificationEvent Event(string subject, DateTimeOffset expiry) => new()
        {
            SourceKey = Guid.NewGuid().ToString("D"), SourceKind = "Job", SourceId = Guid.NewGuid(),
            Audience = "Subject", RecipientSubject = subject, MessageCode = "JOB_SUCCEEDED", Severity = "Info",
            OccurredAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expiry
        };
        var batchContext = new NotificationContext("batch-owner", false, "native-batch");
        db.NotificationEvents.AddRange(Enumerable.Range(0, 1005).Select(_ => Event(batchContext.Subject, now.AddDays(1))));
        await db.SaveChangesAsync();
        var firstPage = await service.ListAsync(batchContext, new(Take: 3), default);
        var secondPage = await service.ListAsync(batchContext, new(Take: 3, Before: firstPage.NextBefore), default);
        Assert.Equal(3, firstPage.Items.Count); Assert.Equal(3, secondPage.Items.Count);
        Assert.True(firstPage.Items.Last().Sequence > secondPage.Items.First().Sequence);
        Assert.Empty((await service.ListAsync(batchContext, new(Kind: "Alert"), default)).Items);
        var cutoff = (await service.SummaryAsync(batchContext, default)).SnapshotSequence;
        var marked = await service.MarkAllReadAsync(batchContext, cutoff, default);
        Assert.Equal(1000, marked.ChangedCount); Assert.True(marked.HasMore);
        db.NotificationEvents.Add(Event(batchContext.Subject, now.AddDays(1))); await db.SaveChangesAsync();
        marked = await service.MarkAllReadAsync(batchContext, cutoff, default);
        Assert.Equal(5, marked.ChangedCount); Assert.False(marked.HasMore);
        Assert.Equal(1, (await service.SummaryAsync(batchContext, default)).UnreadCount);
        Assert.Equal(0, (await service.MarkAllReadAsync(batchContext, cutoff, default)).ChangedCount);
        await Assert.ThrowsAsync<ArgumentException>(() => service.MarkAllReadAsync(batchContext, long.MaxValue, default));

        // DB uniqueness, event immutability, bounded retention and FK read-state cascade.
        await using (var collision = new PlatformDbContext(options))
        {
            collision.NotificationEvents.Add(new NotificationEvent { SourceKey = personal.SourceKey, SourceKind = "Job", SourceId = job.Id,
                Audience = "Administrator", MessageCode = "JOB_SUCCEEDED", Severity = "Info", OccurredAtUtc = now });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => collision.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        var fact = await db.NotificationEvents.SingleAsync(item => item.Id == personal.Id);
        db.Entry(fact).Property(item => item.SourceKey).CurrentValue = "not-allowed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var expired = Enumerable.Range(0, 1005).Select(_ => Event(userA.Subject, now.AddDays(-1))).ToArray();
        db.NotificationEvents.AddRange(expired);
        db.NotificationReadStates.Add(new() { NotificationId = expired[0].Id, Subject = userA.Subject, ReadAtUtc = now });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetReadAsync(userA, expired[0].Id, true, default));
        Assert.Equal(1000, await NotificationRetention.PruneAsync(db, now, default));
        Assert.Equal(5, await NotificationRetention.PruneAsync(db, now, default));
        Assert.Equal(0, await NotificationRetention.PruneAsync(db, now, default));
        Assert.False(await db.NotificationReadStates.AnyAsync(item => item.NotificationId == expired[0].Id));
        Assert.Equal(1008, await db.NotificationEvents.CountAsync());
    }
}
