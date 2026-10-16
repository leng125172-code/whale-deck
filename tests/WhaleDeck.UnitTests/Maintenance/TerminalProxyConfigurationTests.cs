using WhaleDeck.MaintenanceHost;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class TerminalProxyConfigurationTests
{
    [Fact]
    public void OnlyTheTerminalConnectRouteGetsTheStreamingCluster()
    {
        var route = TerminalProxyConfiguration.CreateRoute();
        Assert.Equal("/api/v1/terminals/{sessionId}/connect", route.Match.Path);
        Assert.Equal("host-terminal", route.ClusterId);
        Assert.Equal(-50, route.Order);
        Assert.True(route.Order > -100); // Authentik's port route wins.
        Assert.Equal("true", Assert.Single(route.Transforms!)["RequestHeaderOriginalHost"]);
    }

    [Fact]
    public void StreamingTimeoutExceedsKeepAliveAndRevocationIntervalsButStaysBounded()
    {
        var cluster = TerminalProxyConfiguration.CreateCluster();
        Assert.Equal("host-terminal", cluster.ClusterId);
        var timeout = cluster.HttpRequest!.ActivityTimeout!.Value;
        Assert.True(timeout > TimeSpan.FromSeconds(20));
        Assert.True(timeout <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TerminalUpstreamIsFixedLoopbackAndDoesNotAcceptCallerDestinations()
    {
        var destination = Assert.Single(TerminalProxyConfiguration.CreateCluster().Destinations!);
        Assert.Equal("gateway", destination.Key);
        Assert.Equal("http://127.0.0.1:18080/", destination.Value.Address);
    }
}
