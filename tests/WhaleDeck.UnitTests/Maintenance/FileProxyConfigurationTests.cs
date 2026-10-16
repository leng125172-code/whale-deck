using WhaleDeck.MaintenanceHost;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class FileProxyConfigurationTests
{
    [Fact]
    public void StreamingBudgetIsScopedAndNeverOverridesAuthentikPortRoute()
    {
        var route = FileProxyConfiguration.CreateRoute();
        Assert.Equal("/api/v1/files/{**catch-all}", route.Match.Path); Assert.Equal(-50, route.Order); Assert.True(route.Order > -100);
        Assert.Equal(1073741824, route.MaxRequestBodySize);
        var cluster = FileProxyConfiguration.CreateCluster();
        Assert.Equal(TimeSpan.FromSeconds(60), cluster.HttpRequest!.ActivityTimeout);
        Assert.Equal("http://127.0.0.1:18080/", Assert.Single(cluster.Destinations!).Value.Address);
    }
}
