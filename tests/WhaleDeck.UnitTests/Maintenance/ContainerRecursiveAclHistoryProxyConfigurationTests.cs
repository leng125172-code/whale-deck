using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.MaintenanceHost;
using Yarp.ReverseProxy.Configuration;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class ContainerRecursiveAclHistoryProxyConfigurationTests
{
    [Fact]
    public void ReadBudgetIsBoundedAndDoesNotBroadenOtherApiOrAuthentikRoutes()
    {
        var routes = ContainerRecursiveAclHistoryProxyConfiguration.CreateRoutes(); Assert.Equal(2, routes.Count);
        foreach (var route in routes)
        {
            Assert.Equal("GET", Assert.Single(route.Match.Methods!)); Assert.Equal(0, route.MaxRequestBodySize);
            Assert.Equal(-50, route.Order); Assert.True(route.Order > -100);
            Assert.Equal("true", Assert.Single(route.Transforms!)["RequestHeaderOriginalHost"]);
            Assert.Equal("container-recursive-acl-history", route.ClusterId);
        }
        var cluster = ContainerRecursiveAclHistoryProxyConfiguration.CreateCluster();
        Assert.Equal(TimeSpan.FromSeconds(35), cluster.HttpRequest!.ActivityTimeout);
        Assert.Equal("http://127.0.0.1:18080/", Assert.Single(cluster.Destinations!).Value.Address);
    }

    [Fact]
    public async Task ActualProxyKeepsDelayedListAndReadJsonAndRejectsWritesAndNoncanonicalPaths()
    {
        var upstreamBuilder = Builder(); await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/{**path}", async (CancellationToken token) =>
        { await Task.Delay(TimeSpan.FromSeconds(6), token); return new { verifiedFixture = true }; });
        await upstream.StartAsync();
        var cluster = ContainerRecursiveAclHistoryProxyConfiguration.CreateCluster() with
        { Destinations = new Dictionary<string, DestinationConfig> { ["fixture"] = new() { Address = Assert.Single(upstream.Urls) } } };
        var builder = Builder(); builder.Services.AddReverseProxy().LoadFromMemory(ContainerRecursiveAclHistoryProxyConfiguration.CreateRoutes(), [cluster]);
        await using var proxy = builder.Build(); proxy.MapReverseProxy(); await proxy.StartAsync();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(Assert.Single(proxy.Urls)), Timeout = TimeSpan.FromSeconds(15) };
        var path = "/api/v1/containers/" + new string('a', 64) + "/files/recursive-acls/history";
        foreach (var read in new[] { path, path + "/" + new string('b', 32) })
        {
            using var response = await client.GetAsync(read); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        }
        foreach (var other in new[] { path + "/extra", path + "/" + new string('B', 32), path + "/" + new string('b', 32) + "/extra",
            path.Replace(new string('a', 64), "name", StringComparison.Ordinal), path.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal),
            path.Replace("history", "restores", StringComparison.Ordinal), "/api/v1/metrics" })
        { using var rejected = await client.GetAsync(other); Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode); }
        using var write = await client.PostAsync(path, null); Assert.Equal(HttpStatusCode.MethodNotAllowed, write.StatusCode);
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)); return builder;
    }
}
