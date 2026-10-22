using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class ContainerFileProxyConfigurationTests
{
    [Fact]
    public void CaptureBudgetIsBoundedToCanonicalContainerGetDownloadAndNoBody()
    {
        var route = ContainerFileProxyConfiguration.CreateRoute(); var cluster = ContainerFileProxyConfiguration.CreateCluster();
        Assert.Equal("/api/v1/containers/{containerId:regex((?-i:^[a-f0-9]+$)):length(64)}/files/download", route.Match.Path);
        Assert.Equal("GET", Assert.Single(route.Match.Methods!)); Assert.Equal(0, route.MaxRequestBodySize);
        Assert.Equal(-50, route.Order); Assert.True(route.Order > -100);
        Assert.Equal("true", Assert.Single(route.Transforms!)["RequestHeaderOriginalHost"]);
        Assert.Equal(route.ClusterId, cluster.ClusterId); Assert.Equal(TimeSpan.FromSeconds(315), cluster.HttpRequest!.ActivityTimeout);
        Assert.Equal("http://127.0.0.1:18080/", Assert.Single(cluster.Destinations!).Value.Address);
        Assert.Equal(TimeSpan.FromSeconds(60), FileProxyConfiguration.CreateCluster().HttpRequest!.ActivityTimeout);
    }

    [Fact]
    public async Task RealLoopbackAllowsCapturePastFiveSecondsButNotNamesOtherPathsOrWrites()
    {
        var upstreamBuilder = Builder(); await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/api/v1/containers/{id}/files/download", async (CancellationToken token) =>
        { await Task.Delay(TimeSpan.FromSeconds(6), token); return "isolated-container-capture"; });
        await upstream.StartAsync();
        var cluster = ContainerFileProxyConfiguration.CreateCluster() with
        { Destinations = new Dictionary<string, DestinationConfig> { ["fixture"] = new() { Address = Assert.Single(upstream.Urls) } } };
        var builder = Builder(); builder.Services.AddReverseProxy().LoadFromMemory([ContainerFileProxyConfiguration.CreateRoute()], [cluster]);
        await using var proxy = builder.Build(); proxy.MapReverseProxy(); await proxy.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(Assert.Single(proxy.Urls)), Timeout = TimeSpan.FromSeconds(15) };
        var path = "/api/v1/containers/" + new string('a', 64) + "/files/download";
        using var response = await client.GetAsync(path + "?instanceId=frozen");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("isolated-container-capture", await response.Content.ReadAsStringAsync());
        foreach (var other in new[] { path.Replace("download", "entries", StringComparison.Ordinal), path.Replace(new string('a', 64), "ordinary", StringComparison.Ordinal),
            path.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal), path + "/extra", "/api/v1/metrics" })
        { using var rejected = await client.GetAsync(other); Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode); }
        using var write = await client.PostAsync(path, null); Assert.Equal(HttpStatusCode.MethodNotAllowed, write.StatusCode);
    }
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)); return builder;
    }
}
