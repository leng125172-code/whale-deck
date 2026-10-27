using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Only canonical container upload content receives the raw streaming budget.</summary>
public static class ContainerUploadProxyConfiguration
{
    public static RouteConfig CreateRoute() => new()
    {
        RouteId = "container-upload-content", ClusterId = "container-upload-content", Order = -50,
        Match = new RouteMatch
        {
            Path = "/api/v1/containers/{containerId:regex((?-i:\\A[a-f0-9]+\\z)):length(64)}/files/uploads/{id:regex((?-i:\\A[a-f0-9-]+\\z)):length(36):guid}/content",
            Methods = ["PUT"]
        },
        MaxRequestBodySize = FileProxyConfiguration.MaximumBytes,
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };
    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "container-upload-content", HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(660) },
        Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = "http://127.0.0.1:18080/" } }
    };
}
