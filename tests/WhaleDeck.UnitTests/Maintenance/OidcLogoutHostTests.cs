using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class OidcLogoutHostTests
{
    private const string Canonical = "http://192.168.100.13:8081/application/o/whaledeck-external/";
    private static string? Resolve(HttpRequest request) => OidcLogoutHost.Resolve(request, Canonical);
    private const string End = "/application/o/whaledeck-external/end-session/";
    private static string Hint(string json) => "e30." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json)) + ".c2ln";
    private static DefaultHttpContext Request(string issuer, string path = End)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "http"; context.Request.Method = "GET";
        context.Request.Host = new HostString("127.0.0.1", 8081); context.Request.Path = path;
        context.Request.QueryString = QueryString.Create("id_token_hint", Hint(JsonSerializer.Serialize(new { iss = issuer })));
        return context;
    }

    [Theory]
    [InlineData("192.168.100.13", "whaledeck-external")]
    [InlineData("192.168.22.19", "whaledeck-internal")]
    [InlineData("precision-7920-tower.local", "whaledeck-external")]
    [InlineData("[::1]", "whaledeck-external")]
    public void CanonicalVirtualHostDoesNotDependOnBrowserNic(string host, string provider)
    {
        var request = Request("http://" + host + ":8081/application/o/" + provider + "/", "/application/o/" + provider + "/end-session/").Request;
        Assert.Equal(host + ":8081", OidcLogoutHost.Resolve(request, "http://" + host + ":8081/application/o/" + provider + "/"));
        Assert.Equal("127.0.0.1:8081", request.Host.Value);
    }

    [Theory]
    [InlineData("https://192.168.100.13:8081/application/o/whaledeck-external/")]
    [InlineData("http://192.168.100.13:9000/application/o/whaledeck-external/")]
    [InlineData("http://user@192.168.100.13:8081/application/o/whaledeck-external/")]
    [InlineData("http://192.168.100.13:8081/application/o/whaledeck-internal/")]
    [InlineData("http://127.0.0.1:8081/application/o/whaledeck-external/")]
    [InlineData("http://example.com:8081/application/o/whaledeck-external/")]
    [InlineData("http://0.0.0.0:8081/application/o/whaledeck-external/")]
    [InlineData("http://[::]:8081/application/o/whaledeck-external/")]
    [InlineData("http://192.168.100.13:8081/application/o/whaledeck-external/?x=1")]
    [InlineData("http://192.168.100.13:8081/application/o/whaledeck-external/#x")]
    public void OtherAuthoritiesProvidersAndUriComponentsCannotChangeVirtualHost(string issuer) => Assert.Null(Resolve(Request(issuer).Request));

    [Theory]
    [InlineData("/application/o/other/end-session/")]
    [InlineData("/application/o/whaledeck-external/end-session")]
    [InlineData("/application/o/whaledeck-external/authorize/")]
    [InlineData("/api/v3/flows/executor/default-authentication-flow/")]
    public void OtherEndpointsRemainUnchanged(string path) => Assert.Null(Resolve(Request("http://127.0.0.1:8081/application/o/whaledeck-external/", path).Request));

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"iss\":4}")]
    [InlineData("{\"iss\":\"http://127.0.0.1:8081/application/o/whaledeck-external/\",\"iss\":\"http://192.168.100.13:8081/application/o/whaledeck-external/\"}")]
    [InlineData("{bad")]
    public void MalformedAndAmbiguousPayloadsStayUnchanged(string json)
    {
        var context = Request("unused"); context.Request.QueryString = QueryString.Create("id_token_hint", Hint(json));
        Assert.Null(Resolve(context.Request));
    }

    [Fact]
    public void MissingOrDifferentConfiguredIssuerCannotSupplyItsOwnTrustAnchor()
    {
        var request = Request(Canonical).Request;
        Assert.Null(OidcLogoutHost.Resolve(request, null));
        Assert.Null(OidcLogoutHost.Resolve(request, "http://127.0.0.1:8081/application/o/whaledeck-external/"));
        Assert.Null(OidcLogoutHost.Resolve(request, Canonical + "?x=1"));
    }

    [Fact]
    public void NoBodyReadOrUnboundedHintAndNoDuplicateParameters()
    {
        var context = Request("http://127.0.0.1:8081/application/o/whaledeck-external/");
        var original = context.Request.QueryString;
        context.Request.Method = "POST"; Assert.Null(Resolve(context.Request));
        context.Request.Method = "GET"; context.Request.QueryString = original.Add(original); Assert.Null(Resolve(context.Request));
        foreach (var value in new[] { "", "a.b", "a.b.c.d", "e30.%%.c2ln", "e30.Zg=.c2ln", "e30.e30.", new string('A', 32769), Hint(new string('x', 16385)) })
        { context.Request.QueryString = QueryString.Create("id_token_hint", value); Assert.Null(Resolve(context.Request)); }
    }

    [Fact]
    public async Task ActualProxyChangesOnlyIssuerHostAndNeverUsesItAsNetworkDestination()
    {
        var upstreamBuilder = Builder(); await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/{**path}", (HttpContext ctx) => Results.Json(new { host = ctx.Request.Host.Value, forwarded = ctx.Request.Headers["X-Forwarded-Host"].ToString(), path = ctx.Request.Path.Value, query = ctx.Request.QueryString.Value }));
        await upstream.StartAsync();
        var builder = Builder();
        builder.Services.AddReverseProxy().LoadFromMemory([new RouteConfig { RouteId = "fixture", ClusterId = "authentik", Match = new() { Path = "{**path}" }, Transforms = [new Dictionary<string,string> { ["RequestHeaderOriginalHost"] = "true" }] }],
            [new ClusterConfig { ClusterId = "authentik", Destinations = new Dictionary<string,DestinationConfig> { ["fixed"] = new() { Address = Assert.Single(upstream.Urls) } } }]).AddTransforms(context => OidcLogoutHost.Configure(context, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Maintenance:Oidc:ExternalIssuer"] = Canonical }).Build()));
        await using var proxy = builder.Build(); proxy.MapReverseProxy(); await proxy.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(Assert.Single(proxy.Urls)) };
        var hint = Hint("{\"iss\":\"http://192.168.100.13:8081/application/o/whaledeck-external/\"}");
        var query = QueryString.Create(new Dictionary<string,string?> { ["id_token_hint"] = hint, ["state"] = "opaque-fixture", ["post_logout_redirect_uri"] = "http://127.0.0.1:8080/signout-callback-oidc/external" }).Value!;
        using var result = JsonDocument.Parse(await client.GetStringAsync(End + query));
        Assert.Equal("192.168.100.13:8081", result.RootElement.GetProperty("host").GetString());
        Assert.Equal("192.168.100.13:8081", result.RootElement.GetProperty("forwarded").GetString());
        Assert.Equal(End, result.RootElement.GetProperty("path").GetString()); Assert.Equal(query, result.RootElement.GetProperty("query").GetString());
        using var other = JsonDocument.Parse(await client.GetStringAsync("/api/v3/core/users/me/" + query));
        Assert.Equal(client.BaseAddress.Authority, other.RootElement.GetProperty("host").GetString());
        Assert.Equal(client.BaseAddress.Authority, other.RootElement.GetProperty("forwarded").GetString());
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)); return builder;
    }
}
