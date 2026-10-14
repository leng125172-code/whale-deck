namespace WhaleDeck.Application.Models;

public sealed record NotificationManagementQuery(int Page = 1, int Take = 25);
public sealed record NotificationManagementPage<T>(IReadOnlyCollection<T> Items, bool HasMore, int Page, int Take);
public sealed record NotificationChannelDto(Guid Id, string Name, string Provider, string DestinationReference,
    bool HasCredential, bool IsEnabled, long Version, DateTimeOffset CreatedAtUtc);
public sealed record NotificationRouteDto(Guid Id, Guid ChannelId, string SourceKind, string MinimumSeverity,
    bool IncludeRecovery, bool IsEnabled, long Version, DateTimeOffset CreatedAtUtc);
public sealed record NotificationDeliveryDto(Guid Id, Guid NotificationId, Guid ChannelId, long ChannelVersion,
    string Provider, string State, int AttemptCount, DateTimeOffset CreatedAtUtc, DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset? LastAttemptAtUtc, DateTimeOffset? CompletedAtUtc, string? LastResultCode);
public sealed record SaveNotificationChannelCommand(string Name, string Provider, string DestinationReference,
    string? CredentialReference = null, bool KeepCredential = false, bool IsEnabled = false,
    long? ExpectedVersion = null, bool Confirmed = false);
public sealed record SaveNotificationRouteCommand(Guid ChannelId, string SourceKind = "all", string MinimumSeverity = "Warning",
    bool IncludeRecovery = true, bool IsEnabled = false, long? ExpectedVersion = null, bool Confirmed = false);
public sealed record DeleteNotificationConfigurationCommand(long ExpectedVersion, bool Confirmed = false);
