using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Security;

/// <summary>Keep OIDC logout credentials in encrypted, expiring server-side tickets.
/// The browser's HttpOnly cookie contains only a random session reference.</summary>
public sealed class ServerSideTicketStore(IBffSessionStorage sessions, IDataProtectionProvider protection, TimeProvider clock) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("WhaleDeck.ServerSession.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var subject = Subject(ticket); var expires = Expiry(ticket);
        if (expires <= clock.GetUtcNow()) throw new ArgumentException("SESSION_TICKET_EXPIRED");
        ticket.Properties.ExpiresUtc = expires;
        var payload = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            if (await sessions.CreateAsync(BffSessionValidation.Binding(key), subject, payload, expires, CancellationToken.None)) return key;
        }
        throw new InvalidOperationException("SESSION_STORAGE_UNAVAILABLE");
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (!IsValidKey(key)) throw new ArgumentException("Invalid session reference.", nameof(key));
        var subject = Subject(ticket); var expires = Expiry(ticket);
        // Renewal is never logout and cannot revoke a newer valid ticket by
        // carrying an older expiry. The atomic store also rejects shortening.
        if (expires <= clock.GetUtcNow()) return;
        ticket.Properties.ExpiresUtc = expires;
        _ = await sessions.RenewAsync(BffSessionValidation.Binding(key), subject,
            protector.Protect(TicketSerializer.Default.Serialize(ticket)), expires, CancellationToken.None);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!IsValidKey(key)) return null;
        var binding = BffSessionValidation.Binding(key);
        var stored = await sessions.ReadAsync(binding, CancellationToken.None);
        if (stored is null) return null;
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(stored.ProtectedTicket.ToArray()));
            if (ticket is null || ticket.Properties.ExpiresUtc <= clock.GetUtcNow() ||
                BffSessionValidation.SubjectHash(Subject(ticket)) != stored.SubjectHash ||
                ticket.Properties.ExpiresUtc?.ToUnixTimeMilliseconds() != stored.ExpiresAt.ToUnixTimeMilliseconds())
            {
                await sessions.RemoveAsync(binding, stored.Revision, CancellationToken.None); return null;
            }
            return ticket;
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException or System.IO.InvalidDataException)
        {
            await sessions.RemoveAsync(binding, stored.Revision, CancellationToken.None);
            return null;
        }
    }

    public Task RemoveAsync(string key) => IsValidKey(key) ? sessions.RemoveAsync(BffSessionValidation.Binding(key), null, CancellationToken.None) : Task.CompletedTask;

    private DateTimeOffset Expiry(AuthenticationTicket ticket)
    {
        var expiry = ticket.Properties.ExpiresUtc ?? clock.GetUtcNow().Add(BffSessionValidation.MaximumLifetime);
        if (expiry > clock.GetUtcNow().Add(BffSessionValidation.MaximumLifetime)) throw new ArgumentException("SESSION_EXPIRY_INVALID");
        // AuthenticationProperties stores RFC1123 timestamps at whole seconds.
        return DateTimeOffset.FromUnixTimeSeconds(expiry.ToUnixTimeSeconds());
    }
    private static string Subject(AuthenticationTicket ticket)
    {
        var subject = ticket.Principal.FindFirst("sub")?.Value;
        if (ticket.Principal.Identity?.IsAuthenticated != true || subject is null) throw new ArgumentException("SESSION_SUBJECT_INVALID");
        _ = BffSessionValidation.SubjectHash(subject); return subject;
    }

    private static bool IsValidKey(string key) => key.Length == 64 && key.All(char.IsAsciiHexDigit);
}
