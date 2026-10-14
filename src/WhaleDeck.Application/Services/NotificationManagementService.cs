using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class NotificationManagementService(INotificationManagementRepository repository)
{
    public Task<NotificationManagementPage<NotificationChannelDto>> ListChannelsAsync(NotificationContext context, NotificationManagementQuery query, CancellationToken cancellationToken)
    { Authorize(context); return repository.ListChannelsAsync(Page(query), cancellationToken); }

    public Task<NotificationManagementPage<NotificationRouteDto>> ListRoutesAsync(NotificationContext context, NotificationManagementQuery query, Guid? channelId, CancellationToken cancellationToken)
    { Authorize(context); FilterId(channelId); return repository.ListRoutesAsync(Page(query), channelId, cancellationToken); }

    public Task<NotificationManagementPage<NotificationDeliveryDto>> ListDeliveriesAsync(NotificationContext context, NotificationManagementQuery query, Guid? channelId, string? state, CancellationToken cancellationToken)
    {
        Authorize(context); FilterId(channelId);
        if (state is not (null or "Pending" or "Leased" or "Sending" or "RetryWait" or "Succeeded" or "Failed" or "Unknown" or "Canceled"))
            throw new ArgumentException("Notification delivery state is invalid.");
        return repository.ListDeliveriesAsync(Page(query), channelId, state, cancellationToken);
    }

    public Task<NotificationChannelDto> SaveChannelAsync(NotificationContext context, Guid? id, SaveNotificationChannelCommand command, CancellationToken cancellationToken)
    {
        Authorize(context); ValidateWrite(context, id, command.ExpectedVersion, command.Confirmed);
        if (command.KeepCredential && (id is null || command.CredentialReference is not null))
            throw new ArgumentException("Credential keep and replacement are mutually exclusive.");
        NotificationDispatchPolicy.Validate(new NotificationChannel { Name = command.Name, Provider = command.Provider,
            DestinationReference = command.DestinationReference, CredentialReference = command.CredentialReference });
        return repository.SaveChannelAsync(context, id, command with { Name = command.Name.Trim() }, cancellationToken);
    }

    public Task<NotificationRouteDto> SaveRouteAsync(NotificationContext context, Guid? id, SaveNotificationRouteCommand command, CancellationToken cancellationToken)
    {
        Authorize(context); ValidateWrite(context, id, command.ExpectedVersion, command.Confirmed);
        NotificationDispatchPolicy.Validate(new NotificationRoute { ChannelId = command.ChannelId, SourceKind = command.SourceKind, MinimumSeverity = command.MinimumSeverity });
        return repository.SaveRouteAsync(context, id, command, cancellationToken);
    }

    public Task DeleteChannelAsync(NotificationContext context, Guid id, DeleteNotificationConfigurationCommand command, CancellationToken cancellationToken)
    { Authorize(context); ValidateWrite(context, id, command.ExpectedVersion, command.Confirmed); return repository.DeleteChannelAsync(context, id, command.ExpectedVersion, cancellationToken); }

    public Task DeleteRouteAsync(NotificationContext context, Guid id, DeleteNotificationConfigurationCommand command, CancellationToken cancellationToken)
    { Authorize(context); ValidateWrite(context, id, command.ExpectedVersion, command.Confirmed); return repository.DeleteRouteAsync(context, id, command.ExpectedVersion, cancellationToken); }

    public Task<IReadOnlyCollection<NotificationAttemptDto>> AttemptsAsync(NotificationContext context, Guid deliveryId, CancellationToken cancellationToken)
    {
        Authorize(context); FilterId(deliveryId);
        return repository.AttemptsAsync(deliveryId, cancellationToken);
    }

    private static void Authorize(NotificationContext context)
    {
        if (!context.IsAdministrator) throw new UnauthorizedAccessException("Notification administration is restricted.");
        if (string.IsNullOrWhiteSpace(context.Subject) || context.Subject.Length > 256 || context.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(context.TraceId) || context.TraceId.Length > 128 || context.TraceId.Any(char.IsControl) ||
            context.SourceIp is { } ip && (ip.Length > 64 || ip.Any(char.IsControl)))
            throw new ArgumentException("Authenticated notification context is invalid.");
    }

    private static NotificationManagementQuery Page(NotificationManagementQuery query)
    {
        var take = Math.Clamp(query.Take, 1, 100);
        if (query.Page < 1 || (long)(query.Page - 1) * take > int.MaxValue) throw new ArgumentException("Notification page is invalid.");
        return query with { Take = take };
    }

    private static void FilterId(Guid? id) { if (id == Guid.Empty) throw new ArgumentException("Notification target is invalid."); }
    private static void ValidateWrite(NotificationContext context, Guid? id, long? expectedVersion, bool confirmed)
    {
        FilterId(id);
        if (!Guid.TryParse(context.IdempotencyKey, out var key) || key == Guid.Empty) throw new ArgumentException("A UUID idempotency key is required.");
        if (!confirmed) throw new ArgumentException("Notification configuration changes require confirmation.");
        if (id is null ? expectedVersion is not null : expectedVersion is null or < 0 or long.MaxValue)
            throw new ArgumentException("Notification expected version is invalid.");
    }
}
