using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace WhaleDeck.Api.Security;

/// <summary>Keep OIDC logout credentials in encrypted, expiring server-side tickets.
/// The browser's HttpOnly cookie contains only a random session reference.</summary>
public sealed class ServerSideTicketStore(IDistributedCache cache, IDataProtectionProvider protection) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("WhaleDeck.ServerSession.v1");
    private const string Prefix = "auth:ticket:";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (!IsValidKey(key)) throw new ArgumentException("Invalid session reference.", nameof(key));
        var expires = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8);
        return cache.SetAsync(Prefix + key, protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            new DistributedCacheEntryOptions { AbsoluteExpiration = expires });
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!IsValidKey(key)) return null;
        var bytes = await cache.GetAsync(Prefix + key);
        if (bytes is null) return null;
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(bytes));
            if (ticket is null || ticket.Properties.ExpiresUtc <= DateTimeOffset.UtcNow) return null;
            return ticket;
        }
        catch (CryptographicException)
        {
            await RemoveAsync(key);
            return null;
        }
    }

    public Task RemoveAsync(string key) => IsValidKey(key) ? cache.RemoveAsync(Prefix + key) : Task.CompletedTask;

    private static bool IsValidKey(string key) => key.Length == 64 && key.All(char.IsAsciiHexDigit);
}
