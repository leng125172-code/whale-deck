using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Identity;

public sealed class NotificationRequestAuthorizer(IIdentityDirectory identity) : INotificationRequestAuthorizer
{
    public async Task<bool> IsAdministratorAsync(string subject, CancellationToken cancellationToken)
    {
        try { return (await identity.ResolveCurrentAsync(subject, null, [], cancellationToken)).IsAdministrator; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
