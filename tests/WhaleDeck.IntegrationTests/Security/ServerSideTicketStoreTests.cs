using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class ServerSideTicketStoreTests
{
    [Fact]
    public async Task LogoutCredentialIsEncryptedAndRemovedWithSession()
    {
        var cache = new MemoryBffSessionStorage(TimeProvider.System);
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider(), TimeProvider.System);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "sensitive-test-id-token" }]);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "cookie")), properties, "Cookies");
        var key = await store.StoreAsync(ticket);
        Assert.Equal(64, key.Length);
        Assert.DoesNotContain("sensitive", key);
        var stored = await cache.ReadAsync(BffSessionValidation.Binding(key), default);
        Assert.NotNull(stored);
        Assert.DoesNotContain("sensitive-test-id-token", Encoding.UTF8.GetString(stored.ProtectedTicket.Span));
        var retrieved = await store.RetrieveAsync(key);
        Assert.Equal("sensitive-test-id-token", retrieved?.Properties.GetTokenValue("id_token"));
        await store.RemoveAsync(key);
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task InvalidReferencesAndTamperedTicketsFailClosed()
    {
        var cache = new MemoryBffSessionStorage(TimeProvider.System);
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider(), TimeProvider.System);
        Assert.Null(await store.RetrieveAsync("../another-key"));
        var key = new string('A', 64);
        await cache.CreateAsync(BffSessionValidation.Binding(key), "fixture", new byte[] { 1, 2, 3 }, DateTimeOffset.UtcNow.AddMinutes(10), default);
        Assert.Null(await store.RetrieveAsync(key));
        Assert.Null(await cache.ReadAsync(BffSessionValidation.Binding(key), default));
    }

    [Fact]
    public async Task RenewalKeepsTheReferenceAndUsesTicketExpiry()
    {
        var cache = new MemoryBffSessionStorage(TimeProvider.System);
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider(), TimeProvider.System);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "fixture")], "cookie")),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, "Cookies");
        var key = await store.StoreAsync(ticket);
        ticket.Properties.Items["renewed"] = "yes";
        await store.RenewAsync(key, ticket);
        Assert.Equal("yes", (await store.RetrieveAsync(key))?.Properties.Items["renewed"]);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.RenewAsync(key, ticket);
        Assert.NotNull(await store.RetrieveAsync(key)); // A stale renewal cannot delete the newer live entry.
        await store.RemoveAsync(key); Assert.Null(await store.RetrieveAsync(key));
    }
}
