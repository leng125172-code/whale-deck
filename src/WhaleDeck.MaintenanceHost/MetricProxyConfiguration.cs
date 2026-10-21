using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Historical queries have a bounded database budget longer than ordinary fail-fast HTTP.</summary>
public static class MetricProxyConfiguration
{
    public static RouteConfig CreateRoute() => new()
    {
        RouteId = "metric-history", ClusterId = "metric-history", Order = -50,
        // Authentik's port-specific route still wins. Do not lengthen arbitrary API/write requests.
        Match = new RouteMatch { Path = "/api/v1/metrics", Methods = ["GET"] },
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };

    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "metric-history",
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(35) },
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["gateway"] = new() { Address = "http://127.0.0.1:18080/" }
        }
    };
}
