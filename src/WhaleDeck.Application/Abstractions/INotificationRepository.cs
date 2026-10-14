using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface INotificationRepository
{
    Task<NotificationPageDto> ListAsync(NotificationContext context, NotificationQuery query, CancellationToken cancellationToken);
    Task<NotificationSummaryDto> SummaryAsync(NotificationContext context, CancellationToken cancellationToken);
    Task<NotificationReadDto> SetReadAsync(NotificationContext context, Guid id, bool isRead, CancellationToken cancellationToken);
    Task<PrepareNotificationsReadDto> PrepareReadAllAsync(NotificationContext context, CancellationToken cancellationToken);
    Task<MarkNotificationsReadDto> MarkAllReadAsync(NotificationContext context, Guid snapshotId, CancellationToken cancellationToken);
}
