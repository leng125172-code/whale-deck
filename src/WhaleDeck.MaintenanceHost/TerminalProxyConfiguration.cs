using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace WhaleDeck.MaintenanceHost;

/// <summary>Keep terminal streams alive between 20-second backend WebSocket keep-alives.</summary>
public static class TerminalProxyConfiguration
{
    public static RouteConfig CreateRoute() => new()
    {
        RouteId = "host-terminal",
        ClusterId = "host-terminal",
        // Authentik's port-specific route (-100) must still take precedence.
        Order = -50,
        Match = new RouteMatch { Path = "/api/v1/terminals/{sessionId}/connect" },
        Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
    };

    public static ClusterConfig CreateCluster() => new()
    {
        ClusterId = "host-terminal",
        // Ordinary HTTP retains its short failure timeout. The application
        // continues enforcing terminal leases, idle limits and revocation.
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(45) },
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["gateway"] = new() { Address = "http://127.0.0.1:18080/" }
        }
    };
}
