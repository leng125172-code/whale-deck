using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class AgentGrpcService(DockerEngine docker) : AgentService.AgentServiceBase
{
    public override Task<CapabilitiesResponse> GetCapabilities(Empty request, ServerCallContext context)
    {
        var response = new CapabilitiesResponse
        {
            AgentVersion = typeof(AgentGrpcService).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            ProtocolMajor = 1,
            ProtocolMinor = 27,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        };
        response.Capabilities.AddRange([
            "files.registered-streams",
            "files.staged-publication",
            "files.registered-mutations",
            "files.mutation-tombstones",
            "files.bounded-browser",
            "files.hdd-versions",
            "files.archive-intents",
            "files.tree-copy-intents",
            "host.terminal.pty",
            "docker.terminal.exec",
            "notifications.private-configuration",
            "docker.usage",
            "host.load-average",
            "docker.relationships",
            "applications.configuration",
            "applications.backups",
            "applications.backup-retention",
            "backups.capacity",
            "host.read", "host.addresses", "host.metrics", "host.disk-io", "host.processes", "docker.read", "docker.container-summary", "docker.lifecycle", "docker.resource-management", "docker.image-metadata", "docker.logs", "docker.stats", "docker.events",
            "database.registered", "backup.registered", "compose.registered", "compose.projects", "managed.plan",
            "systemd.registered", "config-repository.registered", "maintenance.registered"
        ]);
        return Task.FromResult(response);
    }

    public override async Task<HealthResponse> GetHealth(Empty request, ServerCallContext context) =>
        await BuildHealth(context.CancellationToken);

    public override async Task WatchHealth(Empty request, IServerStreamWriter<HealthResponse> responseStream, ServerCallContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(await BuildHealth(context.CancellationToken));
            await Task.Delay(TimeSpan.FromSeconds(15), context.CancellationToken);
        }
    }

    private async Task<HealthResponse> BuildHealth(CancellationToken cancellationToken)
    {
        var dockerAvailable = await docker.IsAvailableAsync(cancellationToken);
        return new HealthResponse
        {
            Healthy = dockerAvailable,
            DockerAvailable = dockerAvailable,
            SystemdAvailable = Directory.Exists("/run/systemd/system"),
            Status = dockerAvailable ? "Ready" : "DockerUnavailable",
            ObservedAtUtc = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };
    }
}
