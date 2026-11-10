using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class MaintenanceUnavailableResponseTests
{
    private const string UpstreamBody = "<html>upstream body must not replace the maintenance page</html>";

    [Fact]
    public async Task ExistingHtmlBodyBypassesEmptyBodyStatusCodePages()
    {
        await using var fixture = await Fixture.Start("gateway", 502, "text/html", UpstreamBody, applyTransform: false);
        using var response = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(UpstreamBody, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, fixture.Requests);
    }

    [Theory]
    [InlineData("gateway", 502, "GET")]
    [InlineData("gateway", 504, "GET")]
    [InlineData("authentik", 502, "GET")]
    [InlineData("authentik", 504, "POST")]
    [InlineData("gateway", 502, "HEAD")]
    public async Task HtmlGatewayFailureBecomesMaintenanceBeforeItsExistingBodyStarts(string cluster, int status, string method)
    {
        await using var fixture = await Fixture.Start(cluster, status, "text/html", UpstreamBody);
        using var response = await fixture.Client.SendAsync(new(new HttpMethod(method), "/health/live"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Empty(response.Content.Headers.ContentEncoding); Assert.Null(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        if (method == "HEAD") Assert.Empty(body);
        else { Assert.Contains("Whale Deck 维护中", body); Assert.Contains("&lt;update&gt;", body); Assert.DoesNotContain(UpstreamBody, body); }
        Assert.Equal(1, fixture.Requests);
        fixture.Status = 200; fixture.Body = "healthy-again"; fixture.MediaType = "text/plain";
        using var ready = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode); Assert.Equal("healthy-again", await ready.Content.ReadAsStringAsync());
        Assert.Equal(2, fixture.Requests);
    }

    [Theory]
    [InlineData("gateway", 503, "application/problem+json")]
    [InlineData("gateway", 502, "application/problem+json")]
    [InlineData("gateway", 401, "text/html")]
    [InlineData("gateway", 200, "text/html")]
    [InlineData("host-terminal", 502, "text/html")]
    public async Task ApplicationProblemsAndDedicatedStreamsKeepTheirOriginalResponse(string cluster, int status, string mediaType)
    {
        await using var fixture = await Fixture.Start(cluster, status, mediaType, "original-response");
        using var response = await fixture.Client.GetAsync("/api/v1/example");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("original-response", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, fixture.Requests);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "whaledeck-maintenance-response-" + Guid.NewGuid().ToString("N"));
        private WebApplication upstream = null!, proxy = null!;
        public HttpClient Client { get; private set; } = null!;
        public int Status, Requests;
        public string Body = "", MediaType = "";
        public static async Task<Fixture> Start(string cluster, int status, string mediaType, string body, bool applyTransform = true)
        {
            var fixture = new Fixture { Status = status, Body = body, MediaType = mediaType };
            try
            {
                Directory.CreateDirectory(fixture.directory);
                var path = Path.Combine(fixture.directory, "status.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { state = "updating", message = "<update>" }));
                fixture.upstream = Builder().Build();
                fixture.upstream.Run(async context =>
                {
                    Interlocked.Increment(ref fixture.Requests);
                    context.Response.StatusCode = fixture.Status; context.Response.ContentType = fixture.MediaType;
                    context.Response.Headers.ETag = "\"upstream-error\"";
                    context.Response.Headers.ContentEncoding = "identity";
                    await context.Response.WriteAsync(fixture.Body);
                });
                await fixture.upstream.StartAsync();
                var builder = Builder();
                builder.Services.AddSingleton(_ => new MaintenanceStatusReader(new HttpClient(new NoProbe()), TimeProvider.System));
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Maintenance:StatusPath"] = path }).Build();
                builder.Services.AddReverseProxy().LoadFromMemory(
                    [new RouteConfig { RouteId = "fixture", ClusterId = cluster, Match = new() { Path = "{**path}" } }],
                    [new ClusterConfig { ClusterId = cluster, Destinations = new Dictionary<string, DestinationConfig> { ["fixed"] = new() { Address = Assert.Single(fixture.upstream.Urls) } } }])
                    .AddTransforms(context => { if (applyTransform) MaintenanceUnavailableResponse.Configure(context, configuration); });
                fixture.proxy = builder.Build();
                fixture.proxy.UseStatusCodePages(async context =>
                {
                    context.HttpContext.Response.StatusCode = 503;
                    context.HttpContext.Response.ContentType = "text/html";
                    await context.HttpContext.Response.WriteAsync(MaintenanceUnavailableResponse.Page("<update>"));
                });
                fixture.proxy.MapReverseProxy(); await fixture.proxy.StartAsync();
                fixture.Client = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new(Assert.Single(fixture.proxy.Urls)) };
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (proxy is not null) await proxy.DisposeAsync();
            if (upstream is not null) await upstream.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private sealed class NoProbe : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Recorded update must not probe or reforward an upstream request.");
    }
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)); return builder;
    }
}
