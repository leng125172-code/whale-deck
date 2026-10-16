namespace WhaleDeck.Domain.Entities;

/// <summary>Control-plane metadata only. Never contains terminal input/output or a reusable credential.</summary>
public sealed class TerminalSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string GrantHash { get; init; }
    public string TargetId { get; init; } = "whaledeck.host";
    public string TargetKind { get; init; } = "Host";
    public string? InstanceId { get; init; }
    public string? Shell { get; init; }
    public string State { get; set; } = "Issued";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset GrantExpiresAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? ConnectedAtUtc { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public string? CloseCode { get; set; }
    public int? ExitCode { get; set; }
    public long Version { get; set; }
}
