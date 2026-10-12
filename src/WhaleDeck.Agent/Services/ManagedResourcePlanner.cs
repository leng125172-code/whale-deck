using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>
/// Creates read-only plans for actions that target registered host resources.
/// Keeping this separate from <see cref="DockerEngine"/> prevents database,
/// backup, Compose and host operations from being interpreted as container
/// creation requests.
/// </summary>
public sealed class ManagedResourcePlanner(ResourceRegistry registry, PlanStore plans)
{
    private static readonly string[] SensitiveParameterFragments =
        ["password", "secret", "token", "credential"];

    public PlanResponse Create(RegisteredActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var area = request.Resource?.ResourceType?.Trim().ToLowerInvariant() ?? string.Empty;
        var action = request.Action.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
            throw new InvalidOperationException("A registered action is required.");

        var category = ResolveCategory(area);
        var requestedResourceId = request.Resource?.ResourceId ?? string.Empty;
        var normalizedResourceId = NormalizeResourceId(category, action, requestedResourceId);
        var resource = registry.Require(normalizedResourceId, action);
        var parameters = SanitizeParameters(request.Parameters);
        ValidateBounds(parameters);

        var response = plans.Create(requestedResourceId, action, parameters);
        response.Changes.Clear();
        response.Changes.Add($"{area}:{action}:{normalizedResourceId}");
        if (IsDestructive(action) && !string.Equals(resource.ProtectionLevel, "Managed", StringComparison.Ordinal))
            response.Warnings.Add($"The action changes protected resource {normalizedResourceId} and requires explicit confirmation.");
        return response;
    }

    public static string ResolveCategory(string area) => area switch
    {
        "databases" or "backups" => "Database",
        "host" => "Systemd",
        "applications" => "Compose",
        "config-repository" => "ConfigRepository",
        "platform" or "docker" => "PlatformMaintenance",
        _ => throw new InvalidOperationException("The managed action area is unsupported.")
    };

    public static string NormalizeResourceId(string category, string action, string resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId)) return resourceId;
        return category switch
        {
            "Systemd" => "whaledeck.host",
            "Compose" => "whaledeck.apps",
            "ConfigRepository" => "database-platform.repository",
            "PlatformMaintenance" when action is "validate-settings" or "apply-settings" => "whaledeck.docker",
            "PlatformMaintenance" => "whaledeck.platform",
            _ => resourceId
        };
    }

    public static bool RequiresPlan(string action) => action is
        "create" or "delete" or "delete-principal" or "prune" or "apply-settings" or "install" or "update" or "rebuild" or
        "reinstall" or "uninstall" or "update-install" or "reboot" or "commit-push" or "apply-update" or "rollback" or
        "network-create" or "network-delete" or "volume-delete" or "batch-start" or "batch-stop" or "batch-restart" or "batch-delete";

    public static Dictionary<string, string> SanitizeParameters(IReadOnlyDictionary<string, string> parameters) =>
        parameters
            .Where(item => !IsSensitiveParameter(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static bool IsSensitiveParameter(string key) =>
        string.Equals(key, "environment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "inputTicket", StringComparison.OrdinalIgnoreCase) ||
        SensitiveParameterFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static void ValidateBounds(Dictionary<string, string> parameters)
    {
        if (parameters.Count > 64) throw new InvalidOperationException("The action has too many parameters.");
        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
                throw new InvalidOperationException("A plan parameter name is invalid.");
            if (value.Length > 64 * 1024 || value.Contains('\0'))
                throw new InvalidOperationException($"Plan parameter {key} exceeds the approved bounds.");
        }
    }

    private static bool IsDestructive(string action) => action is
        "delete" or "delete-principal" or "uninstall" or "apply-settings" or "update-install" or "reboot" or
        "commit-push" or "apply-update" or "rollback";
}
