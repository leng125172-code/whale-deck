using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class TerminalSessionService(ITerminalSessionRepository repository, ITerminalGrantSigner signer,
    IIdentityDirectory identity, TimeProvider time, IContainerTerminalGateway containers)
{
    public async Task<TerminalGrantDto> IssueHostAsync(TerminalActor actor, CreateHostTerminalCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        return await IssueAsync(actor, command.Confirmed, command.IdempotencyKey, "Host", "whaledeck.host", null, null, cancellationToken);
    }

    public async Task<ContainerTerminalTargetDto> InspectContainerAsync(TerminalActor actor, string containerId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        RequireContainerId(containerId);
        var target = await containers.InspectContainerTerminalAsync(containerId, cancellationToken);
        if (target.ContainerId != containerId) throw new InvalidOperationException("TERMINAL_TARGET_MISMATCH");
        return target;
    }

    public async Task<TerminalGrantDto> IssueContainerAsync(TerminalActor actor, CreateContainerTerminalCommand command, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        ValidateCommand(command.Confirmed, command.IdempotencyKey);
        RequireContainerId(command.ContainerId);
        if (string.IsNullOrWhiteSpace(command.InstanceId) || command.InstanceId.Length > 80 || command.InstanceId.Any(char.IsControl) || command.Shell is not ("sh" or "bash" or "ash"))
            throw new ArgumentException("CONTAINER_TERMINAL_TARGET_INVALID");
        var target = await InspectContainerAsync(actor, command.ContainerId, cancellationToken);
        if (target.InstanceId != command.InstanceId) throw new InvalidOperationException("CONTAINER_INSTANCE_CHANGED");
        // Recheck directory permission after the external inspection, before persisting authority.
        await AuthorizeAsync(actor, cancellationToken);
        return await IssueAsync(actor, command.Confirmed, command.IdempotencyKey, "Container", command.ContainerId, command.InstanceId, command.Shell, cancellationToken);
    }

    private async Task<TerminalGrantDto> IssueAsync(TerminalActor actor, bool confirmed, string key, string kind, string targetId, string? instanceId, string? shell, CancellationToken cancellationToken)
    {
        ValidateCommand(confirmed, key);
        var now = time.GetUtcNow(); now = now.AddTicks(-(now.Ticks % 10)); var id = Guid.NewGuid();
        var candidate = new TerminalSession { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            IdempotencyKey = key, GrantHash = Hash(signer.Sign(id, actor.Subject, actor.LoginBinding, targetId, instanceId, shell)),
            TargetKind = kind, TargetId = targetId, InstanceId = instanceId, Shell = shell,
            CreatedAtUtc = now, GrantExpiresAtUtc = now.AddSeconds(30), ExpiresAtUtc = now.AddMinutes(10) };
        var stored = await repository.IssueAsync(actor, candidate, now, cancellationToken);
        if (stored.TargetKind != kind || stored.TargetId != targetId || stored.InstanceId != instanceId || stored.Shell != shell)
            throw new InvalidOperationException("TERMINAL_IDEMPOTENCY_TARGET_CONFLICT");
        if (stored.State != "Issued" || stored.GrantExpiresAtUtc <= time.GetUtcNow()) throw new InvalidOperationException("TERMINAL_GRANT_ALREADY_USED_OR_EXPIRED");
        var token = signer.Sign(stored.Id, actor.Subject, actor.LoginBinding, targetId, instanceId, shell);
        if (!FixedHashEquals(stored.GrantHash, Hash(token))) throw new InvalidOperationException("TERMINAL_SIGNING_KEY_CHANGED");
        return new(stored.Id, token, stored.GrantExpiresAtUtc, stored.ExpiresAtUtc, stored.TargetId, stored.TargetKind, stored.InstanceId, stored.Shell);
    }

    private static void ValidateCommand(bool confirmed, string key)
    {
        if (!confirmed || !Guid.TryParseExact(key, "D", out var requestId) || requestId == Guid.Empty || key != requestId.ToString("D"))
            throw new ArgumentException("TERMINAL_CONFIRMATION_AND_KEY_REQUIRED");
    }
    private static void RequireContainerId(string id)
    {
        if (id is not { Length: 64 } || id.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw new ArgumentException("CONTAINER_ID_REQUIRED");
    }

    public async Task<TerminalSessionDto> ClaimAsync(TerminalActor actor, Guid id, string token, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        if (id == Guid.Empty || !Hex(token)) throw new ArgumentException("TERMINAL_GRANT_INVALID");
        var row = await repository.ClaimAsync(actor, id, Hash(token), time.GetUtcNow(), cancellationToken);
        // The hash enforces single-use in SQL; the HMAC also authenticates the
        // actual persisted target. Corrupted/swapped metadata cannot redirect a
        // valid token to a different container, startup instance or shell.
        var expected = signer.Sign(row.Id, actor.Subject, actor.LoginBinding, row.TargetId, row.InstanceId, row.Shell);
        if (!FixedHashEquals(token, expected))
        {
            await repository.CloseAsync(actor, id, "TERMINAL_FAILED", null, time.GetUtcNow(), cancellationToken);
            throw new InvalidOperationException("TERMINAL_GRANT_SCOPE_CHANGED");
        }
        return Map(row);
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

    public async Task<bool> RequireConnectingAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(actor, cancellationToken);
        var row = await repository.FindOwnedAsync(actor, id, cancellationToken); var now = time.GetUtcNow();
        return row is { State: "Connecting" } && row.ExpiresAtUtc > now && row.LeaseExpiresAtUtc > now;
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
        row.GrantExpiresAtUtc, row.ExpiresAtUtc, row.ConnectedAtUtc, row.ClosedAtUtc, row.CloseCode, row.ExitCode, row.TargetKind, row.InstanceId, row.Shell);
}
