using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Security;

/// <summary>Reads the server-side cookie reference without caching authentication for a long-lived WebSocket.</summary>
public sealed class TerminalCookieIdentity(IOptionsMonitor<CookieAuthenticationOptions> options)
{
    public async Task<TerminalLogin> ReadAsync(HttpContext context, string? expectedKey = null)
    {
        var settings = options.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        if (settings.SessionStore is not ServerSideTicketStore store) throw new InvalidOperationException("TERMINAL_SERVER_SESSION_REQUIRED");
        var reference = expectedKey;
        if (reference is null)
        {
            var cookie = settings.CookieManager.GetRequestCookie(context, settings.Cookie.Name!);
            // The cookie handler uses this fixed claim for ITicketStore. Do
            // not use AuthenticateAsync here: its per-request result is cached.
            var referenceTicket = string.IsNullOrEmpty(cookie) ? null : settings.TicketDataFormat.Unprotect(cookie);
            reference = referenceTicket?.Principal.FindFirst("Microsoft.AspNetCore.Authentication.Cookies-SessionId")?.Value;
        }
        if (reference is null || reference.Length != 64 || !reference.All(char.IsAsciiHexDigit)) throw new UnauthorizedAccessException("TERMINAL_LOGIN_REQUIRED");
        var ticket = await store.RetrieveAsync(reference);
        var subject = ticket?.Principal.FindFirst("sub")?.Value;
        if (ticket?.Principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject)) throw new UnauthorizedAccessException("TERMINAL_LOGIN_EXPIRED");
        return new(reference, new(subject, ticket.Principal.Identity.Name, ticket.Principal.FindAll("groups").Select(item => item.Value).ToArray(),
            BffSessionValidation.Binding(reference), context.TraceIdentifier, context.Connection.RemoteIpAddress?.ToString()));
    }
}
// Never returned in HTTP/WS responses. The raw reference only lives in memory.
public sealed record TerminalLogin(string Key, TerminalActor Actor);
