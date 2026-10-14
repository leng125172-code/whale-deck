using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface INotificationManagementRepository
{
    Task<NotificationManagementPage<NotificationChannelDto>> ListChannelsAsync(NotificationManagementQuery query, CancellationToken cancellationToken);
    Task<NotificationManagementPage<NotificationRouteDto>> ListRoutesAsync(NotificationManagementQuery query, Guid? channelId, CancellationToken cancellationToken);
    Task<NotificationManagementPage<NotificationDeliveryDto>> ListDeliveriesAsync(NotificationManagementQuery query, Guid? channelId, string? state, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<NotificationAttemptDto>> AttemptsAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task<NotificationSubmissionDto> TestAsync(NotificationContext context, Guid channelId, TestNotificationCommand command, CancellationToken cancellationToken);
    Task<NotificationSubmissionDto> RetryUnknownAsync(NotificationContext context, Guid deliveryId, RetryUnknownNotificationCommand command, CancellationToken cancellationToken);
    Task<NotificationChannelDto> SaveChannelAsync(NotificationContext context, Guid? id, SaveNotificationChannelCommand command, CancellationToken cancellationToken);
    Task<NotificationRouteDto> SaveRouteAsync(NotificationContext context, Guid? id, SaveNotificationRouteCommand command, CancellationToken cancellationToken);
    Task DeleteChannelAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken);
    Task DeleteRouteAsync(NotificationContext context, Guid id, long expectedVersion, CancellationToken cancellationToken);
}
