using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed record RegisteredResource(
    string Id,
    string Type,
    string ExternalId,
    string ProtectionLevel,
    string? Path = null,
    string[]? AllowedActions = null);

public sealed class ResourceRegistry
{
    private static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, RegisteredResource> _resources;

    public ResourceRegistry(IConfiguration configuration)
    {
        var path = configuration["Agent:ResourceRegistryFile"] ?? "/etc/whaledeck/resources.json";
        var resources = File.Exists(path)
            ? JsonSerializer.Deserialize<RegisteredResource[]>(
                File.ReadAllText(path),
                RegistryJsonOptions) ?? []
            : DefaultResources();
        _resources = resources.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<RegisteredResource> All => _resources.Values.ToArray();

    public RegisteredResource Require(string id, string action)
    {
        if (!_resources.TryGetValue(id, out var resource))
        {
            throw new InvalidOperationException($"Resource is not registered: {id}");
        }

        if (resource.AllowedActions is { Length: > 0 } &&
            !resource.AllowedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Action is not allowed for resource {id}: {action}");
        }

        return resource;
    }

    public bool IsProtectedContainer(string idOrName, IDictionary<string, string>? labels)
    {
        if (labels?.TryGetValue("io.whaledeck.protected", out var value) == true &&
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _resources.Values.Any(resource =>
            resource.Type == "Container" &&
            string.Equals(resource.ExternalId, idOrName.TrimStart('/'), StringComparison.Ordinal));
    }

    private static RegisteredResource[] DefaultResources() =>
    [
        new("whaledeck.api", "Container", "whaledeck-api", "ControlPlane"),
        new("whaledeck.worker", "Container", "whaledeck-worker", "ControlPlane"),
        new("whaledeck.gateway", "Container", "whaledeck-gateway", "ControlPlane"),
        new("database-platform.postgres", "Container", "database-platform-postgres", "CriticalData"),
        new("database-platform.mariadb", "Container", "database-platform-mariadb", "CriticalData"),
        new("database-platform.mongodb", "Container", "database-platform-mongodb", "CriticalData"),
        new("database-platform.sqlserver", "Container", "database-platform-sqlserver", "CriticalData"),
        new("database-platform.valkey", "Container", "database-platform-valkey", "CriticalData"),
        new("database-platform.valkey72", "Container", "database-platform-valkey72", "CriticalData"),
        new("database-platform.authentik.server", "Container", "database-platform-authentik-server", "IdentityCore"),
        new("database-platform.authentik.worker", "Container", "database-platform-authentik-worker", "IdentityCore"),
        new("whaledeck.agent", "SystemdUnit", "whaledeck-agent.service", "ControlPlane", AllowedActions: ["systemd-restart"]),
        new("whaledeck.maintenance", "SystemdUnit", "whaledeck-maintenance.service", "ControlPlane", AllowedActions: ["systemd-restart"]),
        new("database-platform.update", "SystemdUnit", "database-platform-workstation-update.timer", "ControlPlane", AllowedActions: ["systemd-start", "systemd-stop", "systemd-restart"]),
        new("whaledeck.host", "Host", "local", "ControlPlane", AllowedActions: ["update-check", "update-install", "reboot", "terminal-open"]),
        new("whaledeck.docker", "DockerEngine", "docker", "ControlPlane", AllowedActions: ["validate-settings", "apply-settings", "rollback"]),
        new("whaledeck.platform", "Platform", "whaledeck", "ControlPlane", AllowedActions: ["diagnose", "diagnostic-bundle", "plan-update", "apply-update", "rollback"]),
        new("database-platform.repository", "GitRepository", "database-platform", "CriticalData", "/data/GitRepos/database-platform", ["status", "snapshot", "commit-push"]),
        new("whaledeck.apps", "ManagedDirectory", "whaledeck-apps", "Managed", "/data/WhaleDeck/apps", ["install", "start", "stop", "restart", "update", "reinstall", "uninstall", "pull", "redeploy", "configure", "backup", "verify-backup", "cleanup-backups"])
    ];
}
