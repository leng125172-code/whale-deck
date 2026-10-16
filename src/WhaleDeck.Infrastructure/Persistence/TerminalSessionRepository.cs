using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class TerminalSessionRepository(PlatformDbContext db) : ITerminalSessionRepository
{
    public Task<TerminalSession> IssueAsync(TerminalActor actor, TerminalSession proposed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        // Serialize the tiny global session budget across API replicas. No
        // terminal bytes or token are used as lock keys or SQL parameters.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(873145910)", cancellationToken);
        var existing = await Owned(actor).SingleOrDefaultAsync(item => item.IdempotencyKey == proposed.IdempotencyKey, cancellationToken);
        if (existing is not null) return existing;
        var stale = await db.TerminalSessions.Where(item => item.State == "Issued" && item.GrantExpiresAtUtc <= now ||
            (item.State == "Connecting" || item.State == "Active") && (item.ExpiresAtUtc <= now || item.LeaseExpiresAtUtc <= now)).Take(64).ToListAsync(cancellationToken);
        foreach (var row in stale)
        {
            row.State = "Closed"; row.ClosedAtUtc = now; row.CloseCode = "TERMINAL_EXPIRED"; row.Version++;
            Audit(row, "terminal.expired", "Expired", actor.TraceId, null, now);
        }
        await db.SaveChangesAsync(cancellationToken);
        var open = db.TerminalSessions.Where(item => item.State == "Issued" || item.State == "Connecting" || item.State == "Active");
        if (await open.CountAsync(cancellationToken) >= 4 || await open.CountAsync(item => item.ActorSubject == actor.Subject, cancellationToken) >= 2)
            throw new InvalidOperationException("TERMINAL_SESSION_LIMIT");
        db.TerminalSessions.Add(proposed); Audit(proposed, "terminal.grant-issued", "Issued", actor.TraceId, actor.SourceIp, now);
        await db.SaveChangesAsync(cancellationToken); return proposed;
    }, cancellationToken);

    public Task<TerminalSession> ClaimAsync(TerminalActor actor, Guid id, string grantHash, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.TerminalSessions.FromSqlInterpolated($"SELECT * FROM terminal_sessions WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("TERMINAL_SESSION_NOT_FOUND");
        // Waiting for a row lock must not extend the 30-second grant window.
        if (DateTimeOffset.UtcNow > now) now = DateTimeOffset.UtcNow;
        if (row.State != "Issued" || row.GrantExpiresAtUtc <= now || row.ExpiresAtUtc <= now || !EqualHash(row.GrantHash, grantHash))
            throw new InvalidOperationException("TERMINAL_GRANT_USED_OR_EXPIRED");
        row.State = "Connecting"; row.LeaseExpiresAtUtc = now.AddSeconds(20); row.Version++;
        Audit(row, "terminal.connecting", "Connecting", actor.TraceId, actor.SourceIp, now);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);

    public Task<TerminalSession?> FindOwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) =>
        Owned(actor).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<bool> RenewAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.TerminalSessions.FromSqlInterpolated($"SELECT * FROM terminal_sessions WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (DateTimeOffset.UtcNow > now) now = DateTimeOffset.UtcNow;
        if (row is null || row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding ||
            row.State is not ("Connecting" or "Active") || row.ExpiresAtUtc <= now || row.LeaseExpiresAtUtc <= now) return false;
        if (row.State == "Connecting")
        {
            row.ConnectedAtUtc = now;
            Audit(row, "terminal.opened", "Active", actor.TraceId, actor.SourceIp, now);
        }
        row.State = "Active"; row.LeaseExpiresAtUtc = now.AddSeconds(35); row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    public Task CloseAsync(TerminalActor actor, Guid id, string code, int? exitCode, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.TerminalSessions.FromSqlInterpolated($"SELECT * FROM terminal_sessions WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding || row.State == "Closed") return true;
        row.State = "Closed"; row.CloseCode = code; row.ExitCode = exitCode; row.ClosedAtUtc = now; row.LeaseExpiresAtUtc = null; row.Version++;
        Audit(row, "terminal.closed", code, actor.TraceId, actor.SourceIp, now);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    private IQueryable<TerminalSession> Owned(TerminalActor actor) => db.TerminalSessions.AsNoTracking().Where(item => item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding);
    private void Audit(TerminalSession row, string action, string result, string trace, string? ip, DateTimeOffset now) => db.AuditEvents.Add(new()
    {
        ActorSubject = row.ActorSubject, Action = action, TargetType = "terminal-session", TargetId = row.Id.ToString("D"),
        Result = result, TraceId = trace, SourceIp = ip, OccurredAtUtc = now
    });
    private static bool EqualHash(string first, string second) => first.Length == 64 && second.Length == 64 &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(first), System.Text.Encoding.ASCII.GetBytes(second));
    private async Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var value = await action(); await transaction.CommitAsync(cancellationToken); return value;
    });
}
