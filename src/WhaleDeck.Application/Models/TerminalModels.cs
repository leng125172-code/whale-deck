namespace WhaleDeck.Application.Models;

public sealed record TerminalActor(string Subject, string? Name, IReadOnlyCollection<string> Groups,
    string LoginBinding, string TraceId, string? SourceIp);
public sealed record CreateHostTerminalCommand(bool Confirmed, string IdempotencyKey);
public sealed record TerminalGrantDto(Guid SessionId, string ConnectToken, DateTimeOffset GrantExpiresAtUtc,
    DateTimeOffset ExpiresAtUtc, string TargetId);
public sealed record TerminalSessionDto(Guid Id, string TargetId, string State, DateTimeOffset CreatedAtUtc,
    DateTimeOffset GrantExpiresAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? ClosedAtUtc, string? CloseCode, int? ExitCode);
