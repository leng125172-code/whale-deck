using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Application.Services;

/// <summary>Only database work is retried by persistence; transport is invoked once, outside all transactions.</summary>
public sealed class NotificationSender(INotificationDispatchRepository repository, INotificationTransport transport, TimeProvider clock, INotificationRequestAuthorizer? authorizer = null)
{
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await repository.RecoverBatchAsync(now, cancellationToken);
        var revisions = new Dictionary<Guid, string>();
        foreach (var channel in await repository.EnabledChannelsAsync(cancellationToken))
        {
            try
            {
                await using var prepared = await transport.PrepareAsync(channel.Provider, channel.DestinationReference, channel.CredentialReference, cancellationToken);
                revisions.Add(channel.Id, prepared.ConfigurationRevision);
            }
            catch (NotificationConfigurationException) { /* Invalid bindings get a definitive pre-send failure, never an external call. */ }
        }
        await repository.RouteBatchAsync(now, cancellationToken, revisions);
        var sent = 0;
        for (var index = 0; index < 10 && !cancellationToken.IsCancellationRequested; index++)
        {
            var item = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken);
            if (item is null) break;
            var token = item.LeaseToken!.Value;
            if (item.IntentKind != "Automatic" && (item.RequestedBySubject is null || authorizer is null ||
                !await authorizer.IsAdministratorAsync(item.RequestedBySubject, cancellationToken)))
            { await repository.RejectBeforeSendAsync(item.Id, token, clock.GetUtcNow(), "PermissionRevoked", cancellationToken); continue; }
            INotificationPreparedSend prepared;
            try { prepared = await transport.PrepareAsync(item.Provider, item.DestinationReference, item.CredentialReference, cancellationToken); }
            catch (NotificationConfigurationException error)
            { await repository.RejectBeforeSendAsync(item.Id, token, clock.GetUtcNow(), error.Code, cancellationToken); continue; }
            await using (prepared)
            {
                if (item.IntentKind == "Test" && item.ConfigurationRevision is null)
                {
                    if (!await repository.BindTestAsync(item.Id, token, prepared.ConfigurationRevision, clock.GetUtcNow(), cancellationToken)) continue;
                }
                else if (item.ConfigurationRevision is null || item.ConfigurationRevision != prepared.ConfigurationRevision)
                { await repository.RejectBeforeSendAsync(item.Id, token, clock.GetUtcNow(), "ConfigurationChanged", cancellationToken); continue; }
                var message = await repository.MessageAsync(item.NotificationId, cancellationToken);
                if (message is null || message.Audience != "Administrator" || message.RecipientSubject is not null || message.ExpiresAtUtc <= clock.GetUtcNow())
                { await repository.RejectBeforeSendAsync(item.Id, token, clock.GetUtcNow(), "EventUnavailable", cancellationToken); continue; }
                cancellationToken.ThrowIfCancellationRequested();
                if (!await repository.BeginSendAsync(item.Id, token, clock.GetUtcNow(), cancellationToken, prepared.ConfigurationRevision, prepared.MinimumIntervalSeconds, prepared.TargetIdentity)) continue;
                NotificationTransportResult result;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(20)); // Always less than durable 90-second Sending lease.
                try { result = await prepared.SendAsync(item.Id, message, deadline.Token); }
                catch (Exception) { result = NotificationTransportResult.Unknown; } // Neither cancellation nor an exception proves non-delivery.
                using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await repository.FinishAsync(item.Id, token, clock.GetUtcNow(), result, completion.Token);
                sent++;
            }
        }
        return sent;
    }
}
