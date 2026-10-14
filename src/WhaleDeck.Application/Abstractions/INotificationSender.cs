using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface INotificationPreparedSend : IAsyncDisposable
{
    string ConfigurationRevision { get; }
    string TargetIdentity { get; }
    int MinimumIntervalSeconds { get; }
    Task<NotificationTransportResult> SendAsync(Guid deliveryId, NotificationEvent message, CancellationToken cancellationToken);
}

public interface INotificationTransport
{
    Task<INotificationPreparedSend> PrepareAsync(string provider, string destinationReference, string? credentialReference, CancellationToken cancellationToken);
}

public interface INotificationDispatchRepository
{
    Task<IReadOnlyCollection<NotificationChannel>> EnabledChannelsAsync(CancellationToken cancellationToken);
    Task<int> RouteBatchAsync(DateTimeOffset now, CancellationToken cancellationToken, IReadOnlyDictionary<Guid, string>? revisions = null);
    Task<NotificationDelivery?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<NotificationEvent?> MessageAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> BeginSendAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken, string? configurationRevision = null, int minimumIntervalSeconds = 0, string? targetIdentity = null);
    Task<bool> FinishAsync(Guid id, Guid token, DateTimeOffset now, NotificationTransportResult result, CancellationToken cancellationToken);
    Task<bool> RejectBeforeSendAsync(Guid id, Guid token, DateTimeOffset now, string code, CancellationToken cancellationToken);
    Task<int> RecoverBatchAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class NotificationConfigurationException(string code) : Exception("Notification private configuration is unavailable or invalid.")
{
    public string Code { get; } = code is "ConfigurationMissing" or "ConfigurationInvalid" or "BindingMissing" or "NetworkDenied" ? code : "ConfigurationInvalid";
}
