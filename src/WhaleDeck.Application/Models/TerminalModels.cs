namespace WhaleDeck.Application.Models;

public sealed record TerminalActor(string Subject, string? Name, IReadOnlyCollection<string> Groups,
    string LoginBinding, string TraceId, string? SourceIp);
public sealed record CreateHostTerminalCommand(bool Confirmed, string IdempotencyKey);
public sealed record CreateContainerTerminalCommand(bool Confirmed, string IdempotencyKey, string ContainerId, string InstanceId, string Shell);
public sealed record ContainerTerminalTargetDto(string ContainerId, string Name, string InstanceId, string ImageId, string ConfiguredUser);
public sealed record TerminalGrantDto(Guid SessionId, string ConnectToken, DateTimeOffset GrantExpiresAtUtc,
    DateTimeOffset ExpiresAtUtc, string TargetId, string TargetKind = "Host", string? InstanceId = null, string? Shell = null);
public sealed record TerminalSessionDto(Guid Id, string TargetId, string State, DateTimeOffset CreatedAtUtc,
    DateTimeOffset GrantExpiresAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? ClosedAtUtc, string? CloseCode, int? ExitCode, string TargetKind = "Host", string? InstanceId = null, string? Shell = null);
