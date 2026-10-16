using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task TerminalSessionBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); var repository = new TerminalSessionRepository(db);
        var actor = new TerminalActor("terminal-native-admin", null, [], new('A', 64), "terminal-native-trace", null);
        var now = DateTimeOffset.UtcNow; var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("isolated-native-ticket")));
        TerminalSession Session(TerminalActor owner, DateTimeOffset? created = null, string? key = null) => new()
        {
            ActorSubject = owner.Subject, LoginBinding = owner.LoginBinding, IdempotencyKey = key ?? Guid.NewGuid().ToString("D"), GrantHash = hash,
            CreatedAtUtc = created ?? now, GrantExpiresAtUtc = (created ?? now).AddSeconds(30), ExpiresAtUtc = (created ?? now).AddMinutes(10)
        };
        var candidate = Session(actor);
        var issued = await repository.IssueAsync(actor, candidate, now, default);
        var replay = await repository.IssueAsync(actor, Session(actor, key: candidate.IdempotencyKey), now, default);
        Assert.Equal(issued.Id, replay.Id); Assert.Single(await db.TerminalSessions.ToArrayAsync());
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "terminal.grant-issued").ToArrayAsync());
        Assert.Null(await repository.FindOwnedAsync(actor with { LoginBinding = new('B', 64) }, issued.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.ClaimAsync(actor with { Subject = "other" }, issued.Id, hash, now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimAsync(actor, issued.Id, new('C', 64), now, default));
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var concurrent = new PlatformDbContext(options);
            try { await new TerminalSessionRepository(concurrent).ClaimAsync(actor, issued.Id, hash, now, default); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(claims, value => value);
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "terminal.connecting").ToArrayAsync());
        Assert.Null((await repository.FindOwnedAsync(actor, issued.Id, default))!.ConnectedAtUtc);
        Assert.True(await repository.RenewAsync(actor, issued.Id, now.AddSeconds(1), default));
        Assert.True(await repository.RenewAsync(actor, issued.Id, now.AddSeconds(2), default));
        Assert.NotNull((await repository.FindOwnedAsync(actor, issued.Id, default))!.ConnectedAtUtc);
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "terminal.opened").ToArrayAsync());
        Assert.False(await repository.RenewAsync(actor with { LoginBinding = new('B', 64) }, issued.Id, now.AddSeconds(2), default));
        Assert.False(await repository.RenewAsync(actor, issued.Id, now.AddMinutes(1), default));
        await repository.CloseAsync(actor, issued.Id, "TERMINAL_CLOSED", 7, now.AddSeconds(2), default);
        await repository.CloseAsync(actor, issued.Id, "TERMINAL_FAILED", null, now.AddSeconds(3), default);
        var closed = await repository.FindOwnedAsync(actor, issued.Id, default); Assert.Equal(7, closed!.ExitCode);
        Assert.Equal("TERMINAL_CLOSED", closed.CloseCode);
        Assert.Single(await db.AuditEvents.Where(item => item.Action == "terminal.closed").ToArrayAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimAsync(actor, issued.Id, hash, now.AddSeconds(4), default));

        // Persist and reload container bindings with the same atomic single-use lifecycle.
        var container = new TerminalSession { ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            IdempotencyKey = Guid.NewGuid().ToString("D"), GrantHash = hash, TargetKind = "Container", TargetId = new('a', 64),
            InstanceId = "10:2026-10-16T00:00:00Z", Shell = "sh", CreatedAtUtc = now,
            GrantExpiresAtUtc = now.AddSeconds(30), ExpiresAtUtc = now.AddMinutes(10) };
        await repository.IssueAsync(actor, container, now, default);
        var loadedContainer = await repository.FindOwnedAsync(actor, container.Id, default);
        Assert.Equal(container.InstanceId, loadedContainer!.InstanceId); Assert.Equal("Container", loadedContainer.TargetKind);
        await repository.ClaimAsync(actor, container.Id, hash, now, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimAsync(actor, container.Id, hash, now, default));
        await repository.RenewAsync(actor, container.Id, now, default);
        await repository.CloseAsync(actor, container.Id, "TERMINAL_CLOSED", 7, now, default);
        var metadata = await db.AuditEvents.AsNoTracking().Where(item => item.TargetId == container.Id.ToString("D")).ToArrayAsync();
        Assert.Equal(4, metadata.Length);
        Assert.All(metadata, item => { Assert.Equal(actor.Subject, item.ActorSubject); Assert.Contains(container.TargetId, item.DetailJson!); Assert.Contains(container.InstanceId, item.DetailJson!); Assert.DoesNotContain(hash, item.DetailJson!); });
        // PostgreSQL must reject cross-kind or incomplete metadata, independently of Application validation.
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE terminal_sessions SET target_kind='Host' WHERE id={container.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE terminal_sessions SET shell=NULL WHERE id={container.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE terminal_sessions SET instance_id=NULL WHERE id={container.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE terminal_sessions SET target_id='alias' WHERE id={container.Id}"));

        // Expired grants cannot be claimed; a new issue closes stale metadata.
        var expired = await repository.IssueAsync(actor, Session(actor, now.AddMinutes(-2)), now, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimAsync(actor, expired.Id, hash, now, default));
        var first = await repository.IssueAsync(actor, Session(actor), now, default);
        Assert.Equal("Closed", (await repository.FindOwnedAsync(actor, expired.Id, default))!.State);
        var second = await repository.IssueAsync(actor, Session(actor), now, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.IssueAsync(actor, Session(actor), now, default));
        var other = actor with { Subject = "second-admin" };
        await repository.IssueAsync(other, Session(other), now, default);
        await repository.IssueAsync(other, Session(other), now, default);
        var third = actor with { Subject = "third-admin" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.IssueAsync(third, Session(third), now, default));
        await repository.CloseAsync(actor, first.Id, "TERMINAL_CLOSED", null, now.AddSeconds(2), default);
        await repository.CloseAsync(actor, second.Id, "TERMINAL_CLOSED", null, now.AddSeconds(2), default);
        foreach (var row in await db.TerminalSessions.AsNoTracking().Where(item => item.ActorSubject == other.Subject).ToArrayAsync())
            await repository.CloseAsync(other, row.Id, "TERMINAL_CLOSED", null, now.AddSeconds(2), default);

        // Audit failure must roll back grant creation and consumption together.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_terminal_audit_failure CHECK (action NOT LIKE 'terminal.%') NOT VALID");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => repository.IssueAsync(third, Session(third), now, default)); }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_terminal_audit_failure"); }
        Assert.False(await db.TerminalSessions.AnyAsync(item => item.ActorSubject == third.Subject));
        // Capture the application time before waiting for the SQL row lock.
        // Consumption after the deadline must still fail, not use stale time.
        var waitingAt = DateTimeOffset.UtcNow;
        var late = await repository.IssueAsync(third, Session(third, waitingAt.AddSeconds(-29)), waitingAt, default);
        await using (var locker = new PlatformDbContext(options))
        {
            await using var transaction = await locker.Database.BeginTransactionAsync();
            await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM terminal_sessions WHERE id={late.Id} FOR UPDATE");
            var pending = Task.Run(async () =>
            {
                await using var concurrent = new PlatformDbContext(options);
                return await Assert.ThrowsAsync<InvalidOperationException>(() => new TerminalSessionRepository(concurrent).ClaimAsync(third, late.Id, hash, waitingAt, default));
            });
            await Task.Delay(1500);
            await transaction.CommitAsync(); await pending;
        }
        Assert.DoesNotContain("isolated-native-ticket", string.Join(' ', await db.AuditEvents.Where(item => item.TargetType == "terminal-session").Select(item => item.DetailJson).ToArrayAsync()));
        Console.WriteLine("INBOX_PHASE=terminal-session-grants-claims-budget-audit");
    }
}
