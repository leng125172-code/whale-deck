using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhaleDeck.Api.Security;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class ServerSideTicketStoreTests
{
    [Fact]
    public async Task LogoutCredentialIsEncryptedAndRemovedWithSession()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider());
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "sensitive-test-id-token" }]);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "cookie")), properties, "Cookies");
        var key = await store.StoreAsync(ticket);
        Assert.Equal(64, key.Length);
        Assert.DoesNotContain("sensitive", key);
        var stored = await cache.GetAsync("auth:ticket:" + key);
        Assert.NotNull(stored);
        Assert.DoesNotContain("sensitive-test-id-token", Encoding.UTF8.GetString(stored));
        var retrieved = await store.RetrieveAsync(key);
        Assert.Equal("sensitive-test-id-token", retrieved?.Properties.GetTokenValue("id_token"));
        await store.RemoveAsync(key);
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task InvalidReferencesAndTamperedTicketsFailClosed()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider());
        Assert.Null(await store.RetrieveAsync("../another-key"));
        var key = new string('A', 64);
        await cache.SetAsync("auth:ticket:" + key, [1, 2, 3]);
        Assert.Null(await store.RetrieveAsync(key));
        Assert.Null(await cache.GetAsync("auth:ticket:" + key));
    }

    [Fact]
    public async Task RenewalKeepsTheReferenceAndUsesTicketExpiry()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = new ServerSideTicketStore(cache, new EphemeralDataProtectionProvider());
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("cookie")),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, "Cookies");
        var key = await store.StoreAsync(ticket);
        ticket.Properties.Items["renewed"] = "yes";
        await store.RenewAsync(key, ticket);
        Assert.Equal("yes", (await store.RetrieveAsync(key))?.Properties.Items["renewed"]);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.RenewAsync(key, ticket);
        Assert.Null(await store.RetrieveAsync(key));
    }
}
