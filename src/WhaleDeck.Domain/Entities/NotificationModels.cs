namespace WhaleDeck.Domain.Entities;

/// <summary>Immutable, typed durable event: no request/result JSON, credentials or copied user profile.</summary>
public sealed class NotificationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public required string SourceKey { get; init; }
    public required string SourceKind { get; init; }
    public Guid SourceId { get; init; }
    public Guid? ResourceId { get; init; }
    public required string Audience { get; init; }
    public string? RecipientSubject { get; init; }
    public required string MessageCode { get; init; }
    public required string Severity { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset PublishedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; init; } = DateTimeOffset.UtcNow.AddDays(180);
}

/// <summary>Only an Authentik subject and the personal disposition; administrators do not share read state.</summary>
public sealed class NotificationReadState
{
    public Guid NotificationId { get; init; }
    public required string Subject { get; init; }
    public DateTimeOffset ReadAtUtc { get; init; }
}
