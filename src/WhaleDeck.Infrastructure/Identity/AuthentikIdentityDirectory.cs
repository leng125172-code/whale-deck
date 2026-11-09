using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Identity;

public sealed class AuthentikIdentityDirectory(
    IHttpClientFactory httpClientFactory,
    IDistributedCache cache,
    IRoleMappingRepository roleMappings,
    IAdministratorGroupConfiguration administratorGroup,
    IConfiguration configuration) : IIdentityDirectory, IIdentityManager, IPrivilegedIdentityDirectory
{
    private static readonly TimeSpan PermissionTtl = TimeSpan.FromHours(2);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken cancellationToken)
    {
        var administrators = await AdministratorGroupsAsync(cancellationToken);
        var key = $"whaledeck:authz:{subject}";
        GroupResolution resolution;
        try { resolution = await ResolveGroupsFromAuthentik(subject, [], cancellationToken, privileged: true); }
        catch (UnauthorizedAccessException)
        {
            await cache.RemoveAsync(key, cancellationToken);
            throw;
        }
        if (!resolution.Authoritative)
        {
            await cache.RemoveAsync(key, cancellationToken);
            throw new UnauthorizedAccessException("Authoritative Authentik identity is required for this action.");
        }
        var administrator = resolution.Groups.Any(administrators.GroupIds.Contains);
        var expires = DateTimeOffset.UtcNow.Add(PermissionTtl);
        await cache.SetStringAsync(key, JsonSerializer.Serialize(new PermissionSnapshot(resolution.Groups, administrator, expires, administrators.Revision)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = PermissionTtl }, cancellationToken);
        return new CurrentUserDto(subject, name, resolution.Groups, administrator, expires);
    }

    public async Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken)
    {
        var administratorGroups = await AdministratorGroupsAsync(cancellationToken);
        var key = $"whaledeck:authz:{subject}";
        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached is not null)
        {
            var snapshot = JsonSerializer.Deserialize<PermissionSnapshot>(cached);
            if (snapshot is not null && snapshot.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                string.Equals(snapshot.MappingRevision, administratorGroups.Revision, StringComparison.Ordinal))
                return new CurrentUserDto(subject, name, snapshot.Groups, snapshot.Administrator, snapshot.ExpiresAtUtc);
        }

        var resolution = await ResolveGroupsFromAuthentik(subject, claimGroups, cancellationToken);
        var groups = resolution.Groups;
        var administrator = groups.Any(administratorGroups.GroupIds.Contains);
        var ttl = resolution.Authoritative || configuration.GetValue<bool>("Authentication:Authentik:AllowClaimFallback")
            ? PermissionTtl : TimeSpan.FromSeconds(15);
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        var current = new PermissionSnapshot(groups, administrator, expires, administratorGroups.Revision);
        await cache.SetStringAsync(key, JsonSerializer.Serialize(current),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        return new CurrentUserDto(subject, name, groups, administrator, expires);
    }

    private async Task<AdministratorGroupSelection> AdministratorGroupsAsync(CancellationToken cancellationToken)
    {
        var configured = administratorGroup.GroupId;
        var mappings = await roleMappings.ListAsync(cancellationToken);
        var identifiers = mappings.Where(item => item.IsEnabled && item.Role == "Administrator")
            .Select(item => item.AuthentikGroupId)
            .Append(configured ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        var revisionMaterial = string.Join('\n', mappings
            .OrderBy(item => item.Id)
            .Select(item => $"{item.Id:D}:{item.Version}:{item.IsEnabled}:{item.Role}:{item.AuthentikGroupId}")) +
            $"\nconfigured:{configured}";
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionMaterial)));
        return new AdministratorGroupSelection(identifiers, revision);
    }

    public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) =>
        ListDirectoryAsync("core/users/", "AuthentikUser", cancellationToken);

    public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) =>
        ListDirectoryAsync("core/groups/", "AuthentikGroup", cancellationToken);

    public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) =>
        ListDirectoryAsync("core/applications/", "AuthentikApplication", cancellationToken, "slug");

    public async Task ExecuteAsync(string action, string? resourceId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "create-user":
                {
                    var username = Required(parameters, "username", 1, 150);
                    var payload = new
                    {
                        username,
                        name = Optional(parameters, "name", 150) ?? username,
                        email = Optional(parameters, "email", 254) ?? string.Empty,
                        is_active = true
                    };
                    using var created = await SendAsync(HttpMethod.Post, "core/users/", payload, cancellationToken);
                    using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
                    var id = document.RootElement.GetProperty("pk").ToString();
                    if (parameters.TryGetValue("password", out var password))
                        await SendAndDisposeAsync(HttpMethod.Post, $"core/users/{Uri.EscapeDataString(id)}/set_password/", new { password }, cancellationToken);
                    return;
                }
            case "update-user":
                await SendAndDisposeAsync(HttpMethod.Patch, $"core/users/{ResourceId(resourceId)}/", new
                {
                    name = Optional(parameters, "name", 150),
                    email = Optional(parameters, "email", 254)
                }, cancellationToken);
                return;
            case "disable-user":
                await SendAndDisposeAsync(HttpMethod.Patch, $"core/users/{ResourceId(resourceId)}/", new { is_active = false }, cancellationToken);
                return;
            case "create-sso":
                {
                    var slug = Required(parameters, "slug", 1, 80);
                    var providerType = parameters.TryGetValue("providerType", out var type) ? type : "oauth2";
                    if (providerType != "oauth2") throw new InvalidOperationException("Only the approved OAuth2/OIDC provider type is currently supported.");
                    var authorizationFlow = parameters.TryGetValue("authorizationFlowId", out var authorizationFlowId) && !string.IsNullOrWhiteSpace(authorizationFlowId)
                        ? authorizationFlowId
                        : await ResolveFlowAsync(
                            configuration["Authentication:Authentik:AuthorizationFlowSlug"] ?? "default-provider-authorization-explicit-consent",
                            cancellationToken);
                    var invalidationFlow = parameters.TryGetValue("invalidationFlowId", out var invalidationFlowId) && !string.IsNullOrWhiteSpace(invalidationFlowId)
                        ? invalidationFlowId
                        : await ResolveFlowAsync(
                            configuration["Authentication:Authentik:InvalidationFlowSlug"] ?? "default-provider-invalidation-flow",
                            cancellationToken);
                    var providerPayload = new
                    {
                        name = Required(parameters, "name", 1, 150),
                        authorization_flow = authorizationFlow,
                        invalidation_flow = invalidationFlow,
                        client_type = "confidential",
                        grant_types = new[] { "authorization_code", "refresh_token" },
                        redirect_uris = new[]
                        {
                        new
                        {
                            matching_mode = "strict",
                            url = ApprovedRedirectUri(parameters),
                            redirect_uri_type = "authorization"
                        }
                    },
                        sub_mode = "user_uuid"
                    };
                    using var provider = await SendAsync(HttpMethod.Post, "providers/oauth2/", providerPayload, cancellationToken);
                    using var document = JsonDocument.Parse(await provider.Content.ReadAsStringAsync(cancellationToken));
                    var providerId = document.RootElement.GetProperty("pk").ToString();
                    try
                    {
                        await SendAndDisposeAsync(HttpMethod.Post, "core/applications/", new
                        {
                            name = providerPayload.name,
                            slug,
                            provider = providerId,
                            policy_engine_mode = "any"
                        }, cancellationToken);
                    }
                    catch
                    {
                        try
                        {
                            using var cleanup = await Client().DeleteAsync($"providers/oauth2/{Uri.EscapeDataString(providerId)}/", CancellationToken.None);
                        }
                        catch (HttpRequestException) { }
                        throw;
                    }
                    return;
                }
            case "update-sso":
                await SendAndDisposeAsync(HttpMethod.Patch, $"core/applications/{ResourceId(resourceId)}/", new
                {
                    name = Optional(parameters, "name", 150),
                    open_in_new_tab = parameters.TryGetValue("openInNewTab", out var open) && bool.TryParse(open, out var parsed) && parsed
                }, cancellationToken);
                return;
            default: throw new InvalidOperationException("The Authentik management action is unsupported.");
        }
    }

    private async Task<GroupResolution> ResolveGroupsFromAuthentik(string subject, IReadOnlyCollection<string> fallback, CancellationToken cancellationToken, bool privileged = false)
    {
        var token = configuration["Authentication:Authentik:ApiToken"];
        var allowClaimFallback = !privileged && configuration.GetValue("Authentication:Authentik:AllowClaimFallback", false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new GroupResolution(allowClaimFallback ? fallback.ToArray() : [], false);
        }
        if (!Guid.TryParse(subject, out var userUuid))
        {
            return new GroupResolution([], false);
        }
        try
        {
            using var response = await Client().GetAsync(
                $"core/users/?uuid={Uri.EscapeDataString(userUuid.ToString())}&include_groups=true&page_size=2",
                cancellationToken);
            if (!response.IsSuccessStatusCode) return new GroupResolution([], false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() != 1)
                return new GroupResolution([], false);
            var user = results[0];
            if (privileged && (!user.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(uuid.GetString(), out var resolvedUuid) || resolvedUuid != userUuid ||
                !user.TryGetProperty("is_active", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                return new GroupResolution([], false);
            if (user.TryGetProperty("is_active", out var active) && !active.GetBoolean())
                throw new UnauthorizedAccessException("The Authentik account is disabled.");
            if (!user.TryGetProperty("groups_obj", out var groups)) return new GroupResolution([], true);
            return new GroupResolution(groups.EnumerateArray()
                .Select(group => group.TryGetProperty("pk", out var pk) ? pk.GetString()
                    : !privileged && group.TryGetProperty("name", out var displayName) ? displayName.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray(), true);
        }
        catch (HttpRequestException) when (!allowClaimFallback)
        {
            return new GroupResolution([], false);
        }
        catch (HttpRequestException) when (allowClaimFallback)
        {
            return new GroupResolution(fallback.ToArray(), false);
        }
    }

    private async Task<IReadOnlyCollection<ManagedResourceDto>> ListDirectoryAsync(
        string path,
        string type,
        CancellationToken cancellationToken,
        string idProperty = "pk")
    {
        var results = new List<ManagedResourceDto>();
        string? next = path;
        for (var page = 0; page < 20 && next is not null; page++)
        {
            using var response = await Client().GetAsync(next, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            results.AddRange(document.RootElement.GetProperty("results").EnumerateArray().Select(item =>
            {
                var id = item.TryGetProperty(idProperty, out var idValue) ? idValue.ToString() : string.Empty;
                var display = item.TryGetProperty("name", out var name) ? name.GetString() : item.TryGetProperty("username", out var username) ? username.GetString() : id;
                var active = !item.TryGetProperty("is_active", out var activeValue) || activeValue.GetBoolean();
                return new ManagedResourceDto(id, display ?? id, type, active ? "Active" : "Disabled", string.Empty, true, new Dictionary<string, string>());
            }));
            next = document.RootElement.TryGetProperty("pagination", out var pagination) &&
                   pagination.TryGetProperty("next", out var nextValue) && nextValue.ValueKind == JsonValueKind.Number && nextValue.GetInt32() > 0
                ? AppendPage(path, nextValue.GetInt32()) : null;
        }
        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), WebJson);
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            var detail = SafeProviderError(await response.Content.ReadAsStringAsync(cancellationToken));
            response.Dispose();
            throw new HttpRequestException(
                $"Authentik rejected {method.Method} {path} ({(int)status}): {detail}",
                null,
                status);
        }
        return response;
    }

    private static string SafeProviderError(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "no provider detail";
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "non-object provider response";
            var builder = new StringBuilder();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (builder.Length > 0) builder.Append("; ");
                builder.Append(property.Name).Append('=');
                if (property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append("[redacted]");
                }
                else
                {
                    var value = property.Value.ToString().Replace('\r', ' ').Replace('\n', ' ');
                    builder.Append(value.AsSpan(0, Math.Min(value.Length, 160)));
                }
                if (builder.Length >= 512) break;
            }
            return builder.Length == 0 ? "empty provider object" : builder.ToString(0, Math.Min(builder.Length, 512));
        }
        catch (JsonException)
        {
            return "unparseable provider response";
        }
    }

    private async Task SendAndDisposeAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, payload, cancellationToken);
    }

    private async Task<string> ResolveFlowAsync(string slug, CancellationToken cancellationToken)
    {
        if (slug.Length is < 1 or > 100 || slug.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException("The configured Authentik flow slug is invalid.");
        using var response = await Client().GetAsync($"flows/instances/?slug={Uri.EscapeDataString(slug)}&page_size=2", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = document.RootElement.GetProperty("results");
        if (results.GetArrayLength() != 1)
            throw new InvalidOperationException($"The configured Authentik flow was not found: {slug}");
        return results[0].GetProperty("pk").ToString();
    }

    private static string ResourceId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100
        ? Uri.EscapeDataString(value) : throw new InvalidOperationException("The Authentik resource id is invalid.");
    private static string Required(IReadOnlyDictionary<string, string> values, string key, int minimum, int maximum) =>
        values.TryGetValue(key, out var value) && value.Trim().Length >= minimum && value.Trim().Length <= maximum
            ? value.Trim() : throw new InvalidOperationException($"The Authentik field is invalid: {key}");
    private static string? Optional(IReadOnlyDictionary<string, string> values, string key, int maximum) =>
        values.TryGetValue(key, out var value) && value.Trim().Length <= maximum ? value.Trim() : null;
    private static string ApprovedRedirectUri(IReadOnlyDictionary<string, string> values)
    {
        var value = Required(values, "redirectUri", 1, 2048);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
            uri.Host is not ("192.168.22.19" or "192.168.100.13" or "127.0.0.1" or "localhost" or "precision-7920-tower.local") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The SSO redirect URI is not approved.");
        return uri.AbsoluteUri;
    }
    private static string AppendPage(string path, int page) => path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + $"page={page}";

    private HttpClient Client()
    {
        var client = httpClientFactory.CreateClient("Authentik");
        var token = configuration["Authentication:Authentik:ApiToken"];
        if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private sealed record PermissionSnapshot(string[] Groups, bool Administrator, DateTimeOffset ExpiresAtUtc, string? MappingRevision);
    private sealed record GroupResolution(string[] Groups, bool Authoritative);
    private sealed record AdministratorGroupSelection(HashSet<string> GroupIds, string Revision);
}
