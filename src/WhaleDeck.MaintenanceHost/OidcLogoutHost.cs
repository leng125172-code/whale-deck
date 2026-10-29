using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>
/// Restores the issuer virtual Host for Authentik's RP logout validation only.
/// The hint is UNTRUSTED: this never authenticates it, changes destination,
/// or skips Authentik's signature/audience/issuer checks. Authentik remains the verifier.
/// </summary>
public static class OidcLogoutHost
{
    public static void Configure(TransformBuilderContext context, IConfiguration configuration)
    {
        if (context.Route.ClusterId != "authentik") return;
        // Default forwarders are normally appended after custom transforms.
        // Add the same forwarders explicitly first so they cannot restore the
        // browser Host after this narrowly scoped canonical-host adjustment.
        context.UseDefaultForwarders = false;
        context.AddXForwarded(ForwardedTransformActions.Append, true);
        context.AddRequestTransform(transform =>
        {
            var request = transform.HttpContext.Request;
            var approved = request.Path == "/application/o/whaledeck-internal/end-session/"
                ? configuration["Maintenance:Oidc:InternalIssuer"] : configuration["Maintenance:Oidc:ExternalIssuer"];
            var host = Resolve(request, approved);
            if (host is not null)
            {
                transform.ProxyRequest.Headers.Host = host;
                transform.ProxyRequest.Headers.Remove("X-Forwarded-Host");
                transform.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", host);
            }
            return ValueTask.CompletedTask;
        });
    }

    public static string? Resolve(HttpRequest request, string? approvedIssuer)
    {
        var provider = request.Path.Value switch
        {
            "/application/o/whaledeck-internal/end-session/" => "whaledeck-internal",
            "/application/o/whaledeck-external/end-session/" => "whaledeck-external",
            _ => null
        };
        if (provider is null || !HttpMethods.IsGet(request.Method) || request.Query["id_token_hint"].Count != 1) return null;
        var hint = request.Query["id_token_hint"][0];
        if (hint is null || hint.Length is < 1 or > 32768) return null;
        var parts = hint.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) || parts[1].Length > 21846) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(parts[1]);
            if (bytes.Length > 16384 || WebEncoders.Base64UrlEncode(bytes) != parts[1]) return null;
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            var claims = json.RootElement.EnumerateObject().Where(p => p.NameEquals("iss")).ToArray();
            if (claims.Length != 1 || claims[0].Value.ValueKind != JsonValueKind.String) return null;
            var issuer = claims[0].Value.GetString();
            if (issuer is null || issuer != approvedIssuer || issuer.Length > 512 || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
                uri.Scheme != request.Scheme || uri.Scheme is not ("http" or "https") || uri.Port != 8081 ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/application/o/" + provider + "/") return null;
            if (!string.Equals(uri.Host, "precision-7920-tower.local", StringComparison.OrdinalIgnoreCase) &&
                (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))) return null;
            return uri.Authority;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException) { return null; }
    }
}
