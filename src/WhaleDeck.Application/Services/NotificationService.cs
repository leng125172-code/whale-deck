using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class NotificationService(INotificationRepository repository)
{
    public Task<NotificationPageDto> ListAsync(NotificationContext context, NotificationQuery query, CancellationToken cancellationToken)
    {
        Validate(context);
        if (query.Status is not ("all" or "unread") || query.Kind is not ("all" or "Alert" or "Job") || query.Before <= 0)
            throw new ArgumentException("Notification filter or cursor is invalid.");
        return repository.ListAsync(context, query with { Take = Math.Clamp(query.Take, 1, 100) }, cancellationToken);
    }

    public Task<NotificationSummaryDto> SummaryAsync(NotificationContext context, CancellationToken cancellationToken)
    {
        Validate(context); return repository.SummaryAsync(context, cancellationToken);
    }

    public Task<NotificationReadDto> SetReadAsync(NotificationContext context, Guid id, bool isRead, CancellationToken cancellationToken)
    {
        Validate(context); if (id == Guid.Empty) throw new ArgumentException("Notification ID is invalid.");
        return repository.SetReadAsync(context, id, isRead, cancellationToken);
    }

    public Task<MarkNotificationsReadDto> MarkAllReadAsync(NotificationContext context, long throughSequence, CancellationToken cancellationToken)
    {
        Validate(context); ArgumentOutOfRangeException.ThrowIfNegative(throughSequence);
        return repository.MarkAllReadAsync(context, throughSequence, cancellationToken);
    }

    private static void Validate(NotificationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Subject) || context.Subject.Length > 256 || context.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl))
            throw new ArgumentException("Notification actor or trace is invalid.");
    }
}
