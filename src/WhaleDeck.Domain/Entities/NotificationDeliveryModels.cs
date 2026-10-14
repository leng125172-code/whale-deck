namespace WhaleDeck.Domain.Entities;

/// <summary>Destination and credential names refer to private server configuration, never secret values.</summary>
public sealed class NotificationChannel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public required string DestinationReference { get; set; }
    public string? CredentialReference { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
    public DateTimeOffset? NextDeliveryAtUtc { get; set; }
}

public sealed class NotificationRoute
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ChannelId { get; init; }
    public string SourceKind { get; set; } = "all";
    public string MinimumSeverity { get; set; } = "Warning";
    public bool IncludeRecovery { get; set; } = true;
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
}

/// <summary>A per-event receipt, not a maximum sequence cursor: a late transaction must not be skipped.</summary>
public sealed class NotificationDispatchReceipt
{
    public Guid NotificationId { get; init; }
    public DateTimeOffset RoutedAtUtc { get; init; }
    public required string Outcome { get; init; }
    public int DeliveryCount { get; init; }
}

/// <summary>Durable transport intent and lease; never stores a rendered body or transport credentials.</summary>
public sealed class NotificationDelivery
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid NotificationId { get; init; }
    public Guid ChannelId { get; init; }
    public long ChannelVersion { get; init; }
    public required string Provider { get; init; }
    public required string DestinationReference { get; init; }
    public string? CredentialReference { get; init; }
    public string? ConfigurationRevision { get; init; }
    public string State { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastResultCode { get; set; }
}

public sealed class NotificationDeliveryAttempt
{
    public Guid DeliveryId { get; init; }
    public int AttemptNumber { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? ResultCode { get; set; }
}

public sealed class NotificationTargetRateGate
{
    public required string Id { get; init; }
    public DateTimeOffset NextAtUtc { get; set; }
}
