using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Identity;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class PermissionSnapshotTests
{
    private const string Subject = "11111111-2222-3333-4444-555555555555";
    private static readonly string[] AdministratorGroups = ["admin-id"];
    [Fact]
    public async Task MissingApiTokenDoesNotPromoteCookieClaims()
    {
        var directory = Directory(new StubClientFactory(HttpStatusCode.ServiceUnavailable), NewCache());
        var current = await directory.ResolveCurrentAsync(Subject, "User", ["admin-id"], default);
        Assert.False(current.IsAdministrator);
    }

    [Fact]
    public async Task ExpiredSnapshotCannotPreserveAdministratorWhenAuthentikFails()
    {
        var cache = NewCache();
        await cache.SetStringAsync($"whaledeck:authz:{Subject}", JsonSerializer.Serialize(new
        {
            Groups = AdministratorGroups,
            Administrator = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }));
        var directory = Directory(new StubClientFactory(HttpStatusCode.ServiceUnavailable), cache, "test-token");
        var current = await directory.ResolveCurrentAsync(Subject, "User", ["admin-id"], default);
        Assert.False(current.IsAdministrator);
        Assert.Empty(current.Groups);
    }

    [Fact]
    public async Task ValidSnapshotSurvivesShortAuthentikOutage()
    {
        var cache = NewCache();
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        await cache.SetStringAsync($"whaledeck:authz:{Subject}", JsonSerializer.Serialize(new
        {
            Groups = AdministratorGroups,
            Administrator = true,
            ExpiresAtUtc = expires,
            MappingRevision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("\nconfigured:admin-id")))
        }));
        var directory = Directory(new StubClientFactory(HttpStatusCode.ServiceUnavailable), cache, "test-token");
        var current = await directory.ResolveCurrentAsync(Subject, "User", [], default);
        Assert.True(current.IsAdministrator);
        Assert.Equal(expires, current.PermissionExpiresAtUtc);
    }

    [Fact]
    public async Task ExplicitlyDisabledAccountIsRejected()
    {
        var factory = new StubClientFactory(HttpStatusCode.OK, """{"results":[{"pk":42,"is_active":false,"groups_obj":[{"pk":"admin-id"}]}]}""");
        var directory = Directory(factory, NewCache(), "test-token");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => directory.ResolveCurrentAsync(Subject, "User", ["admin-id"], default));
    }

    [Fact]
    public async Task AuthoritativeUuidLookupResolvesAdministratorGroup()
    {
        var factory = new StubClientFactory(HttpStatusCode.OK,
            """{"results":[{"pk":42,"is_active":true,"groups_obj":[{"pk":"admin-id","name":"Whale Deck Administrators"}]}]}""");
        var directory = Directory(factory, NewCache(), "test-token");

        var current = await directory.ResolveCurrentAsync(Subject, "User", [], default);

        Assert.True(current.IsAdministrator);
        Assert.Equal(["admin-id"], current.Groups);
        Assert.NotNull(factory.LastRequestUri);
        Assert.Equal("/api/v3/core/users/", factory.LastRequestUri.AbsolutePath);
        Assert.Contains($"uuid={Subject}", factory.LastRequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("include_groups=true", factory.LastRequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseRoleMappingGrantsAdministratorPermission()
    {
        var mapping = new RoleMapping
        {
            AuthentikGroupId = "operators-id",
            AuthentikGroupNameSnapshot = "Platform Operators"
        };
        var factory = new StubClientFactory(HttpStatusCode.OK,
            """{"results":[{"pk":42,"is_active":true,"groups_obj":[{"pk":"operators-id","name":"Platform Operators"}]}]}""");
        var directory = Directory(factory, NewCache(), "test-token", [mapping]);

        var current = await directory.ResolveCurrentAsync(Subject, "User", [], default);

        Assert.True(current.IsAdministrator);
    }

    [Fact]
    public async Task RoleMappingChangeInvalidatesPermissionSnapshotImmediately()
    {
        var mapping = new RoleMapping
        {
            AuthentikGroupId = "operators-id",
            AuthentikGroupNameSnapshot = "Platform Operators"
        };
        var mappings = new List<RoleMapping> { mapping };
        var factory = new StubClientFactory(HttpStatusCode.OK,
            """{"results":[{"pk":42,"is_active":true,"groups_obj":[{"pk":"operators-id","name":"Platform Operators"}]}]}""");
        var directory = Directory(factory, NewCache(), "test-token", mappings);
        Assert.True((await directory.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);

        mapping.IsEnabled = false;
        mapping.Version++;

        Assert.False((await directory.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);
    }

    [Fact]
    public async Task CreateUserSerializesAnonymousPayloadUsingItsRuntimeType()
    {
        var factory = new StubClientFactory(HttpStatusCode.Created, """{"pk":42}""");
        var directory = Directory(factory, NewCache(), "test-token");

        await directory.ExecuteAsync("create-user", null, new Dictionary<string, string>
        {
            ["username"] = "managed-user",
            ["name"] = "Managed User",
            ["email"] = "managed-user@example.invalid"
        }, default);

        Assert.NotNull(factory.LastRequestBody);
        using var payload = JsonDocument.Parse(factory.LastRequestBody);
        Assert.Equal("managed-user", payload.RootElement.GetProperty("username").GetString());
        Assert.Equal("Managed User", payload.RootElement.GetProperty("name").GetString());
        Assert.True(payload.RootElement.GetProperty("is_active").GetBoolean());
    }

    [Fact]
    public async Task PrivilegedActionObservesRemovedGroupsDespitePositiveNavigationSnapshot()
    {
        var cache = NewCache();
        var warm = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(true, true)), cache, "test-token");
        Assert.True((await warm.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);
        var factory = new StubClientFactory(HttpStatusCode.OK, LiveUser(true, false));
        var changed = Directory(factory, cache, "test-token");
        Assert.False((await changed.ResolveForPrivilegedActionAsync(Subject, "User", default)).IsAdministrator);
        Assert.NotNull(factory.LastRequestUri);
        Assert.False((await changed.ResolveCurrentAsync(Subject, "User", ["admin-id"], default)).IsAdministrator);
    }

    [Fact]
    public async Task PrivilegedGroupIdentityNeverUsesDisplayNamesAsIdentifiers()
    {
        const string payload = """
            {"results":[{"uuid":"11111111-2222-3333-4444-555555555555","is_active":true,"groups_obj":[
                {"name":"admin-id"},{"name":"3c2cd059-605c-4e78-b490-43199dc8ec47"},{"pk":"other-group"}
            ]}]}
            """;
        var directory = Directory(new StubClientFactory(HttpStatusCode.OK, payload), NewCache(), "test-token");
        var current = await directory.ResolveForPrivilegedActionAsync(Subject, "User", default);
        Assert.False(current.IsAdministrator);
        Assert.Equal("other-group", Assert.Single(current.Groups));
    }

    [Fact]
    public async Task PrivilegedActionCanRestoreAuthorityAfterAnAuthoritativeNegativeSnapshot()
    {
        var cache = NewCache();
        var denied = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(true, false)), cache, "test-token");
        Assert.False((await denied.ResolveForPrivilegedActionAsync(Subject, "User", default)).IsAdministrator);
        var restored = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(true, true)), cache, "test-token");
        Assert.True((await restored.ResolveForPrivilegedActionAsync(Subject, "User", default)).IsAdministrator);
        Assert.True((await restored.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);
    }

    [Fact]
    public async Task DisabledPrivilegedIdentityIsRejectedAndEvictsTheOldPositiveSnapshot()
    {
        var cache = NewCache();
        var warm = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(true, true)), cache, "test-token");
        Assert.True((await warm.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);
        var disabled = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(false, true)), cache, "test-token");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => disabled.ResolveForPrivilegedActionAsync(Subject, "User", default));
        Assert.Null(await cache.GetStringAsync($"whaledeck:authz:{Subject}"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("test-token", false)]
    [InlineData("test-token", true)]
    public async Task PrivilegedActionsNeverTrustPositiveCacheOrClaimFallbackDuringOutage(string? token, bool fallback)
    {
        var cache = NewCache();
        var warm = Directory(new StubClientFactory(HttpStatusCode.OK, LiveUser(true, true)), cache, "test-token");
        Assert.True((await warm.ResolveCurrentAsync(Subject, "User", [], default)).IsAdministrator);
        var unavailable = Directory(new StubClientFactory(HttpStatusCode.ServiceUnavailable), cache, token, allowClaimFallback: fallback);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unavailable.ResolveForPrivilegedActionAsync(Subject, "User", default));
        Assert.Null(await cache.GetStringAsync($"whaledeck:authz:{Subject}"));
    }

    [Theory]
    [InlineData("""{"results":[{"is_active":true,"groups_obj":[{"pk":"admin-id"}]}]}""")]
    [InlineData("""{"results":[{"uuid":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","is_active":true,"groups_obj":[{"pk":"admin-id"}]}]}""")]
    [InlineData("""{"results":[{"uuid":"11111111-2222-3333-4444-555555555555","groups_obj":[{"pk":"admin-id"}]}]}""")]
    [InlineData("""{"results":[{"uuid":"11111111-2222-3333-4444-555555555555","is_active":"true","groups_obj":[{"pk":"admin-id"}]}]}""")]
    public async Task PrivilegedIdentityRequiresTheExactUuidAndExplicitActiveBoolean(string payload)
    {
        var directory = Directory(new StubClientFactory(HttpStatusCode.OK, payload), NewCache(), "test-token");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => directory.ResolveForPrivilegedActionAsync(Subject, "User", default));
    }

    private static string LiveUser(bool active, bool administrator) => JsonSerializer.Serialize(new
    {
        results = new[] { new { uuid = Subject, is_active = active, groups_obj = administrator ? new[] { new { pk = "admin-id" } } : [] } }
    });

    private static MemoryDistributedCache NewCache() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private static AuthentikIdentityDirectory Directory(
        IHttpClientFactory clientFactory,
        IDistributedCache cache,
        string? token = null,
        IReadOnlyCollection<RoleMapping>? mappings = null,
        bool allowClaimFallback = false)
    {
        var configuration = Config(token);
        configuration["Authentication:Authentik:AllowClaimFallback"] = allowClaimFallback.ToString();
        return new AuthentikIdentityDirectory(
            clientFactory,
            cache,
            new StubRoleMappingRepository(mappings ?? []),
            new StubAdministratorGroupConfiguration("admin-id"),
            configuration);
    }

    private static IConfigurationRoot Config(string? token = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Authentication:Authentik:ApiToken"] = token,
        ["Authentication:Authentik:AdministratorGroupId"] = "admin-id",
        ["Authentication:Authentik:AllowClaimFallback"] = "false"
    }).Build();

    private sealed class StubClientFactory(HttpStatusCode status, string payload = "{}") : IHttpClientFactory
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public HttpClient CreateClient(string name) => new(new StubHandler(status, payload, (uri, body) =>
        {
            LastRequestUri = uri;
            LastRequestBody = body;
        }))
        {
            BaseAddress = new Uri("http://authentik.invalid/api/v3/")
        };
    }

    private sealed class StubAdministratorGroupConfiguration(string groupId) : IAdministratorGroupConfiguration
    {
        public string GroupId { get; } = groupId;
    }

    private sealed class StubRoleMappingRepository(IReadOnlyCollection<RoleMapping> mappings) : IRoleMappingRepository
    {
        public Task<IReadOnlyCollection<RoleMapping>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(mappings);
        public Task<RoleMapping> SaveAsync(string actorSubject, string traceId, SaveRoleMappingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string actorSubject, string traceId, Guid id, long expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubHandler(HttpStatusCode status, string payload, Action<Uri?, string?> captureRequest) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captureRequest(request.RequestUri, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(payload) };
        }
    }
}
