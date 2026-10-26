using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WhaleDeck.Api.Security;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class TerminalCookieIdentityTests
{
    [Fact]
    public async Task LiveTicketIsReadAgainAfterLogoutEvenWhenHttpContextStillHasPrincipal()
    {
        var (reader, store, context, key) = await Setup();
        var first = await reader.ReadAsync(context);
        Assert.Equal("admin-subject", first.Actor.Subject); Assert.Equal(64, first.Actor.LoginBinding.Length); Assert.NotEqual(key, first.Actor.LoginBinding);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin-subject")], "Cookies"));
        await store.RemoveAsync(key);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(context, first.Key));
    }
    [Fact]
    public async Task RenewalKeepsBindingButUpdatedDirectoryClaimsAreReadFromServer()
    {
        var (reader, store, context, key) = await Setup(); var first = await reader.ReadAsync(context);
        var ticket = (await store.RetrieveAsync(key))!;
        var renewed = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin-subject"), new Claim("groups", "updated-group")], "Cookies")), ticket.Properties, ticket.AuthenticationScheme);
        await store.RenewAsync(key, renewed);
        var second = await reader.ReadAsync(context, first.Key);
        Assert.Equal(first.Actor.LoginBinding, second.Actor.LoginBinding); Assert.Contains("updated-group", second.Actor.Groups);
    }
    [Fact]
    public async Task MissingOrUnprotectableCookieCannotOpenATerminal()
    {
        var (reader, _, context, _) = await Setup();
        context.Request.Headers.Cookie = "whaledeck.session=tampered";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(context));
        context.Request.Headers.Cookie = "";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(context));
    }
    internal static async Task<(TerminalCookieIdentity Reader, ServerSideTicketStore Store, DefaultHttpContext Context, string Key)> Setup()
    {
        var protection = new EphemeralDataProtectionProvider();
        var store = new ServerSideTicketStore(new MemoryBffSessionStorage(TimeProvider.System), protection, TimeProvider.System);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin-subject")], "Cookies")), new() { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, "Cookies");
        var key = await store.StoreAsync(ticket);
        var format = new TicketDataFormat(protection.CreateProtector("terminal-test-cookie"));
        var reference = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId", key)], "Cookies")), new(), "Cookies");
        var settings = new CookieAuthenticationOptions { TicketDataFormat = format, SessionStore = store, CookieManager = new ChunkingCookieManager() };
        settings.Cookie.Name = "whaledeck.session";
        var context = new DefaultHttpContext { TraceIdentifier = "terminal-cookie-test" };
        context.Request.Headers.Cookie = "whaledeck.session=" + format.Protect(reference);
        return (new(new Monitor(settings)), store, context, key);
    }
    private sealed class Monitor(CookieAuthenticationOptions settings) : IOptionsMonitor<CookieAuthenticationOptions>
    {
        public CookieAuthenticationOptions CurrentValue => settings;
        public CookieAuthenticationOptions Get(string? name) => settings;
        public IDisposable? OnChange(Action<CookieAuthenticationOptions, string?> listener) => null;
    }
}
