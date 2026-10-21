using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class MetricProxyConfigurationTests
{
    [Fact]
    public void HistoryBudgetIsBoundedAndScopedToExactReadOnlyRoute()
    {
        var route = MetricProxyConfiguration.CreateRoute();
        Assert.Equal("/api/v1/metrics", route.Match.Path);
        Assert.Equal("GET", Assert.Single(route.Match.Methods!));
        Assert.Equal(-50, route.Order); Assert.True(route.Order > -100);
        Assert.Equal("true", Assert.Single(route.Transforms!)["RequestHeaderOriginalHost"]);
        var cluster = MetricProxyConfiguration.CreateCluster();
        Assert.Equal(route.ClusterId, cluster.ClusterId);
        Assert.Equal(TimeSpan.FromSeconds(35), cluster.HttpRequest!.ActivityTimeout);
        Assert.Equal("http://127.0.0.1:18080/", Assert.Single(cluster.Destinations!).Value.Address);
    }

    [Fact]
    public async Task RealLoopbackProxyAllowsHistoryPastFiveSecondsWithoutProxyingOtherPathsOrWrites()
    {
        var upstreamBuilder = Builder();
        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/api/v1/metrics", async (CancellationToken token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(6), token);
            return "isolated-delayed-history-fixture";
        });
        await upstream.StartAsync();
        var cluster = MetricProxyConfiguration.CreateCluster() with
        {
            Destinations = new Dictionary<string, DestinationConfig> { ["fixture"] = new() { Address = Assert.Single(upstream.Urls) } }
        };
        var builder = Builder();
        builder.Services.AddReverseProxy().LoadFromMemory([MetricProxyConfiguration.CreateRoute()], [cluster]);
        await using var proxy = builder.Build();
        proxy.MapReverseProxy();
        await proxy.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(Assert.Single(proxy.Urls)), Timeout = TimeSpan.FromSeconds(15) };
        using var response = await client.GetAsync("/api/v1/metrics?window=24h");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("isolated-delayed-history-fixture", await response.Content.ReadAsStringAsync());
        using var other = await client.GetAsync("/api/v1/metrics-other");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var write = await client.PostAsync("/api/v1/metrics", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, write.StatusCode);
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return builder;
    }
}
