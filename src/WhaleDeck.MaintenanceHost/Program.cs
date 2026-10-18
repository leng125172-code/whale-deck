using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Formatting.Json;
using WhaleDeck.MaintenanceHost;

if (WhaleDeck.Contracts.BuildIdentity.TryPrint(args, typeof(Program).Assembly)) return;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestHeadersTotalSize = 64 * 1024);
builder.WebHost.UseUrls(builder.Configuration["Maintenance:Urls"]
    ?? "http://0.0.0.0:8080;http://0.0.0.0:8081");
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter()));
// The stable maintenance proxy has no cookies or persisted protected state.
// Make that explicit so it never writes key material or emits key-repository warnings.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddHttpClient("maintenance-status", client => client.Timeout = TimeSpan.FromSeconds(2))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseProxy = false, AllowAutoRedirect = false, UseCookies = false,
        MaxConnectionsPerServer = 2, MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1), MaxResponseDrainSize = 1024
    });
builder.Services.AddSingleton(services => new MaintenanceStatusReader(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("maintenance-status"), TimeProvider.System));

builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [
            TerminalProxyConfiguration.CreateRoute(),
            FileProxyConfiguration.CreateRoute(),
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "authentik",
                ClusterId = "authentik",
                Order = -100,
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                {
                    Hosts = ["*:8081"],
                    Path = "{**catch-all}"
                },
                Transforms = PreserveOriginalHost()
            },
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "gateway",
                ClusterId = "gateway",
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "{**catch-all}" },
                Transforms = PreserveOriginalHost()
            }
        ],
        [
        TerminalProxyConfiguration.CreateCluster(),
        FileProxyConfiguration.CreateCluster(),
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "gateway",
            HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(5)
            },
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                ["gateway"] = new() { Address = "http://127.0.0.1:18080/" }
            }
        },
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "authentik",
            HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(5)
            },
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                ["authentik"] = new() { Address = "http://127.0.0.1:18081/" }
            }
        }]);

var app = builder.Build();
var statusPath = builder.Configuration["Maintenance:StatusPath"]
    ?? "/run/whaledeck/maintenance/status.json";
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.Use(async (context, next) =>
{
    if (!LocalEndpointPolicy.IsAllowed(context.Connection.LocalIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Response.StatusCode is not (502 or 503 or 504))
    {
        return;
    }

    context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
    var status = await context.HttpContext.RequestServices.GetRequiredService<MaintenanceStatusReader>()
        .ReadAsync(statusPath, context.HttpContext.RequestAborted);
    await context.HttpContext.Response.WriteAsync(MaintenancePage(status.Body.Message));
});
app.MapGet("/maintenance/status", async (MaintenanceStatusReader reader, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var status = await reader.ReadAsync(statusPath, context.RequestAborted);
    return Results.Json(status.Body, statusCode: status.StatusCode);
}).WithOrder(-1000);
app.MapReverseProxy();
await app.RunAsync();

static IReadOnlyList<IReadOnlyDictionary<string, string>> PreserveOriginalHost() =>
    [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }];

static string MaintenancePage(string status)
{
    return $$"""
        <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta http-equiv="refresh" content="6"><title>Whale Deck 维护中</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#0f172a;color:#dbeafe;font:14px ui-monospace,monospace}.card{padding:32px;border:1px solid #1d4ed8;border-radius:16px;background:#111827;box-shadow:0 20px 60px #02061780}h1{font-size:20px;color:#60a5fa}</style></head>
        <body><main class="card"><h1>Whale Deck</h1><p>{{System.Net.WebUtility.HtmlEncode(status)}}</p><p>页面将在 6 秒后自动重试。</p></main></body></html>
        """;
}
