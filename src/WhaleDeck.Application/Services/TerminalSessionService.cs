using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class TerminalSessionService(ITerminalSessionRepository repository, ITerminalGrantSigner signer,
    IIdentityDirectory identity, TimeProvider time)
{
    public async Task<TerminalGrantDto> IssueHostAsync(TerminalActor actor, CreateHostTerminalCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (!command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var requestId) || requestId == Guid.Empty || command.IdempotencyKey != requestId.ToString("D"))
            throw new ArgumentException("TERMINAL_CONFIRMATION_AND_KEY_REQUIRED");
        var now = time.GetUtcNow(); now = now.AddTicks(-(now.Ticks % 10)); var id = Guid.NewGuid();
        var candidate = new TerminalSession { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            IdempotencyKey = command.IdempotencyKey, GrantHash = Hash(signer.Sign(id, actor.Subject, actor.LoginBinding)),
            CreatedAtUtc = now, GrantExpiresAtUtc = now.AddSeconds(30), ExpiresAtUtc = now.AddMinutes(10) };
        var stored = await repository.IssueAsync(actor, candidate, now, cancellationToken);
        if (stored.State != "Issued" || stored.GrantExpiresAtUtc <= time.GetUtcNow()) throw new InvalidOperationException("TERMINAL_GRANT_ALREADY_USED_OR_EXPIRED");
        var token = signer.Sign(stored.Id, actor.Subject, actor.LoginBinding);
        if (!FixedHashEquals(stored.GrantHash, Hash(token))) throw new InvalidOperationException("TERMINAL_SIGNING_KEY_CHANGED");
        return new(stored.Id, token, stored.GrantExpiresAtUtc, stored.ExpiresAtUtc, stored.TargetId);
    }

    public async Task<TerminalSessionDto> ClaimAsync(TerminalActor actor, Guid id, string token, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || !Hex(token)) throw new ArgumentException("TERMINAL_GRANT_INVALID");
        return Map(await repository.ClaimAsync(actor, id, Hash(token), time.GetUtcNow(), cancellationToken));
    }

    public async Task<TerminalSessionDto> GetAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        var row = await repository.FindOwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("TERMINAL_SESSION_NOT_FOUND");
        var now = time.GetUtcNow();
        if (row.State is "Issued" && row.GrantExpiresAtUtc <= now || row.State is "Connecting" or "Active" && (row.ExpiresAtUtc <= now || row.LeaseExpiresAtUtc <= now))
            return Map(row) with { State = "Expired", CloseCode = "TERMINAL_EXPIRED" };
        return Map(row);
    }

    public async Task<bool> RenewAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        return await repository.RenewAsync(actor, id, time.GetUtcNow(), cancellationToken);
    }

    // Cleanup must work after logout/demotion. Repository still binds both
    // owner and login, and may only close, never create/renew a capability.
    public Task CloseAsync(TerminalActor actor, Guid id, string code, int? exitCode, CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        if (id == Guid.Empty || code is not ("TERMINAL_CLOSED" or "TERMINAL_DISCONNECTED" or "TERMINAL_EXPIRED" or "TERMINAL_FORBIDDEN" or "TERMINAL_FAILED"))
            throw new ArgumentException("TERMINAL_CLOSE_INVALID");
        return repository.CloseAsync(actor, id, code, exitCode, time.GetUtcNow(), cancellationToken);
    }

    private async Task AuthorizeAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        var current = await identity.ResolveCurrentAsync(actor.Subject, actor.Name, actor.Groups, cancellationToken);
        if (!current.IsAdministrator || current.Subject != actor.Subject) throw new UnauthorizedAccessException("TERMINAL_ADMINISTRATOR_REQUIRED");
    }
    private static void ValidateActor(TerminalActor actor)
    {
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            !Hex(actor.LoginBinding) || string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 64 || actor.TraceId.Any(char.IsControl))
            throw new ArgumentException("TERMINAL_ACTOR_INVALID");
    }
    private static bool Hex(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit) && value.All(character => !char.IsAsciiLetterLower(character));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool FixedHashEquals(string first, string second) => first.Length == 64 && second.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    private static TerminalSessionDto Map(TerminalSession row) => new(row.Id, row.TargetId, row.State, row.CreatedAtUtc,
        row.GrantExpiresAtUtc, row.ExpiresAtUtc, row.ConnectedAtUtc, row.ClosedAtUtc, row.CloseCode, row.ExitCode);
}
