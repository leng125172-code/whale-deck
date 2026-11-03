using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Allow bounded Agent history reads to finish through the stable entry point.</summary>
public static class ContainerAclHistoryProxyConfiguration
{
    private const string Prefix = "/api/v1/containers/{containerId:regex((?-i:^[a-f0-9]+$)):length(64)}/files/acls/history";

    public static IReadOnlyList<RouteConfig> CreateRoutes() =>
    [
        Route("container-acl-history-list", Prefix),
        Route("container-acl-history-read", Prefix + "/{captureId:regex((?-i:^[a-f0-9]+$)):length(32)}")
    ];

    private static RouteConfig Route(string id, string path) => new()
    {
        RouteId = id, ClusterId = "container-acl-history", Order = -50,
        Match = new RouteMatch { Path = path, Methods = ["GET"] }, MaxRequestBodySize = 0,
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };

    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "container-acl-history",
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(35) },
        Destinations = new Dictionary<string, DestinationConfig> { ["gateway"] = new() { Address = "http://127.0.0.1:18080/" } }
    };
}
