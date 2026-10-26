using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class NativeBffValkeyFactAttribute : FactAttribute
{
    public NativeBffValkeyFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_BFF_SESSION_ENDPOINT")))
            Skip = "Requires an explicitly owned local Valkey fixture; not a mocked integration pass.";
    }
}

public sealed class NativeBffSessionTests
{
    [NativeBffValkeyFact]
    public async Task ActualValkeyAtomicRevocationExpirySubjectScopeAndTicketPrivacy()
    {
        var endpoint = Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_BFF_SESSION_ENDPOINT")!;
        var options = new ConfigurationOptions { AbortOnConnectFail = true, ConnectTimeout = 5000, AsyncTimeout = 5000 };
        if (System.Text.RegularExpressions.Regex.IsMatch(endpoint, "^unix:/tmp/wd-bff-session-[a-f0-9]{16}/s\\.sock$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            options.EndPoints.Add(new UnixDomainSocketEndPoint(endpoint[5..]));
        else
        {
            Assert.Equal("127.0.0.1:16379", endpoint);
            options.EndPoints.Add(endpoint);
        }
        using var firstConnection = await ConnectionMultiplexer.ConnectAsync(options);
        using var secondConnection = await ConnectionMultiplexer.ConnectAsync(options);
        // Use the deployed read/write/connection/scripting + TIME policy,
        // never a broad +@all fixture that conceals missing production rights.
        await Assert.ThrowsAsync<RedisServerException>(async () =>
        { _ = await firstConnection.GetDatabase().ExecuteAsync("CONFIG", "GET", "maxmemory"); });
        await Assert.ThrowsAsync<RedisServerException>(async () =>
        { _ = await firstConnection.GetDatabase().ExecuteAsync("ACL", "LIST"); });
        var first = new ValkeyBffSessionStorage(firstConnection, TimeProvider.System);
        var second = new ValkeyBffSessionStorage(secondConnection, TimeProvider.System);
        var protection = new EphemeralDataProtectionProvider();
        var api = new ServerSideTicketStore(first, protection, TimeProvider.System);
        var anotherApi = new ServerSideTicketStore(second, protection, TimeProvider.System);
        var known = new HashSet<string>(StringComparer.Ordinal); var database = firstConnection.GetDatabase();
        var emptyPayload = new byte[] { 1, 2, 3 }; // Synthetic encrypted-storage stand-in for malformed-record cases only.
        string FreshBinding() { var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); known.Add(key); return key; }
        async Task<string> Create(string subject = "native-session-subject")
        {
            var ticket = BffSessionLifecycleTests.Ticket(subject, DateTimeOffset.UtcNow.AddMinutes(10));
            ticket.Properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "SYNTHETIC-NATIVE-OIDC-NOT-A-CREDENTIAL" }]);
            var reference = await api.StoreAsync(ticket); known.Add(BffSessionValidation.Binding(reference)); return reference;
        }
        try
        {
            await Assert.ThrowsAsync<RedisServerException>(() => database.StringGetAsync("outside-whaledeck-session-fixture"));
            // Two independent connections, API ticket readers and metadata-only
            // Worker adapter all observe the same external linearization point.
            var reference = await Create(); var binding = BffSessionValidation.Binding(reference); var key = ValkeyBffSessionStorage.Prefix + binding;
            Assert.True(await second.IsActiveAsync("native-session-subject", binding, default));
            Assert.False(await second.IsActiveAsync("other-subject", binding, default));
            var protectedBytes = (byte[])(await database.HashGetAsync(key, "ticket"))!;
            Assert.DoesNotContain("SYNTHETIC-NATIVE-OIDC", Encoding.UTF8.GetString(protectedBytes));
            Assert.Equal("SYNTHETIC-NATIVE-OIDC-NOT-A-CREDENTIAL", (await anotherApi.RetrieveAsync(reference))!.Properties.GetTokenValue("id_token"));
            var ttl = await database.KeyTimeToLiveAsync(key); Assert.NotNull(ttl); Assert.InRange(ttl.Value.TotalSeconds, 1, 600);
            var other = await Create();
            await api.RemoveAsync(reference);
            await anotherApi.RenewAsync(reference, BffSessionLifecycleTests.Ticket("native-session-subject", DateTimeOffset.UtcNow.AddMinutes(20)));
            Assert.Null(await api.RetrieveAsync(reference)); Assert.False(await second.IsActiveAsync("native-session-subject", binding, default));
            Assert.NotNull(await api.RetrieveAsync(other));
            await anotherApi.RenewAsync(other, BffSessionLifecycleTests.Ticket("native-session-subject", DateTimeOffset.UtcNow.AddMinutes(20)));
            await api.RemoveAsync(other); Assert.False(await second.IsActiveAsync("native-session-subject", BffSessionValidation.Binding(other), default));

            for (var run = 0; run < 64; run++)
            {
                var current = await Create(); var hash = BffSessionValidation.Binding(current);
                await Task.WhenAll(api.RemoveAsync(current), anotherApi.RenewAsync(current,
                    BffSessionLifecycleTests.Ticket("native-session-subject", DateTimeOffset.UtcNow.AddMinutes(20))));
                await anotherApi.RenewAsync(current, BffSessionLifecycleTests.Ticket("native-session-subject", DateTimeOffset.UtcNow.AddMinutes(20)));
                Assert.Null(await first.ReadAsync(hash, default)); Assert.False(await second.IsActiveAsync("native-session-subject", hash, default));
            }
            // Failed renewals do not change owner, shorten TTL or update any
            // part of a malformed record before returning a failure.
            var live = FreshBinding(); var untilAt = DateTimeOffset.UtcNow.AddMinutes(20);
            Assert.True(await first.CreateAsync(live, "owner", emptyPayload, untilAt, default));
            Assert.False(await second.RenewAsync(live, "another-owner", emptyPayload, untilAt, default));
            Assert.False(await second.RenewAsync(live, "owner", emptyPayload, untilAt.AddMinutes(-1), default));
            Assert.Equal(1, (await first.ReadAsync(live, default))!.Revision);
            Assert.True(await second.RenewAsync(live, "owner", emptyPayload, untilAt, default));
            await first.RemoveAsync(live, 1, default); Assert.True(await second.IsActiveAsync("owner", live, default));
            await first.RemoveAsync(live, 2, default); Assert.False(await second.IsActiveAsync("owner", live, default));

            foreach (var corruption in new[] { "no-ttl", "no-ticket", "fractional-revision", "padded-revision", "invalid-expiry", "wrong-type" })
            {
                var malformed = FreshBinding(); var own = ValkeyBffSessionStorage.Prefix + malformed;
                Assert.True(await first.CreateAsync(malformed, "owner", emptyPayload, untilAt, default));
                switch (corruption)
                {
                    case "no-ttl": await database.KeyPersistAsync(own); break;
                    case "no-ticket": await database.HashDeleteAsync(own, "ticket"); break;
                    case "fractional-revision": await database.HashSetAsync(own, "revision", "1.5"); break;
                    case "padded-revision": await database.HashSetAsync(own, "revision", "01"); break;
                    case "invalid-expiry": await database.HashSetAsync(own, "expires", "nan"); break;
                    case "wrong-type": await database.KeyDeleteAsync(own); await database.StringSetAsync(own, "not-a-session", TimeSpan.FromMinutes(1)); break;
                }
                Assert.Null(await first.ReadAsync(malformed, default)); Assert.False(await second.IsActiveAsync("owner", malformed, default));
                Assert.False(await second.RenewAsync(malformed, "owner", emptyPayload, untilAt, default));
                if (corruption == "fractional-revision") Assert.Equal("1.5", (string?)await database.HashGetAsync(own, "revision"));
            }
            var expired = FreshBinding(); var ownExpired = ValkeyBffSessionStorage.Prefix + expired;
            Assert.True(await first.CreateAsync(expired, "owner", emptyPayload, untilAt, default));
            Assert.True(await database.KeyExpireAsync(ownExpired, TimeSpan.FromMilliseconds(1))); await Task.Delay(50);
            Assert.Null(await first.ReadAsync(expired, default)); Assert.False(await second.IsActiveAsync("owner", expired, default));
            Assert.False(await second.RenewAsync(expired, "owner", emptyPayload, untilAt, default));
        }
        finally
        {
            // No SCAN/KEYS/FLUSH. Every deleted key was explicitly generated by
            // this test before its first write; other sessions stay untouched.
            var ownedKeys = known.Select(binding => (RedisKey)(ValkeyBffSessionStorage.Prefix + binding)).ToArray();
            if (ownedKeys.Length != 0) await database.KeyDeleteAsync(ownedKeys);
            foreach (var owned in ownedKeys) Assert.False(await database.KeyExistsAsync(owned));
        }
    }
}
