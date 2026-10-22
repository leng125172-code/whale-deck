using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>The fixed read engine captures and validates the file before sending metadata.</summary>
public static class ContainerFileProxyConfiguration
{
    public static RouteConfig CreateRoute() => new()
    {
        RouteId = "container-file-download", ClusterId = "container-file-download", Order = -50,
        // ASP.NET regex route constraints default to IgnoreCase. Disable it
        // inside this pattern so the proxy budget matches the canonical IDs.
        Match = new RouteMatch { Path = "/api/v1/containers/{containerId:regex((?-i:^[a-f0-9]+$)):length(64)}/files/download", Methods = ["GET"] },
        MaxRequestBodySize = 0,
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };
    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "container-file-download",
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(315) },
        Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = "http://127.0.0.1:18080/" } }
    };
}
