using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

public static class FileProxyConfiguration
{
    public const long MaximumBytes = 1024L * 1024 * 1024;
    public static RouteConfig CreateRoute() => new()
    {
        RouteId = "file-transfers", ClusterId = "file-transfers", Order = -50,
        Match = new RouteMatch { Path = "/api/v1/files/{**catch-all}" },
        MaxRequestBodySize = MaximumBytes,
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };
    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "file-transfers", HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(60) },
        Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = "http://127.0.0.1:18080/" } }
    };
}
