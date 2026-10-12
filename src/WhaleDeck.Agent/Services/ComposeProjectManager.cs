using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Docker.DotNet.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed record ComposeProjectPreview(string Directory, string Project, string Fingerprint, string[] Changes, string[] Services);

/// <summary>Manages only installations beneath the host's registered application root.</summary>
public sealed partial class ComposeProjectManager(ResourceRegistry registry, DockerEngine docker, BoundedProcessRunner processes)
{
    private static readonly string[] ConfigurationFiles = ["compose.yml", ".env", "manifest.json"];
    public static string[] Actions => ["start", "stop", "restart", "pull", "redeploy"];

    public async Task<ComposeProjectPreview> PreviewAsync(RegisteredActionRequest request, CancellationToken cancellationToken)
    {
        if (!Actions.Contains(request.Action, StringComparer.Ordinal) || request.Parameters.Count != 0)
            throw new InvalidOperationException("The Compose project action or parameters are not approved.");
        var slug = ParseResourceId(request.Resource.ResourceId);
        var directory = ResolveDirectory(registry.Require("whaledeck.apps", request.Action).Path!, slug);
        var project = "whaledeck-app-" + slug;
        var containers = await docker.ListContainersAsync(true, cancellationToken);
        var members = Members(containers, project);
        if (members.Any(container => container.Names.Any(name => registry.IsProtectedContainer(name, container.Labels))))
            throw new InvalidOperationException("Protected Compose services cannot be managed here.");
        var fileFingerprint = ConfigurationFingerprint(directory);
        var model = await CommandAsync(directory, ["config", "--format", "json"], TimeSpan.FromSeconds(30), cancellationToken);
        ValidateProjectIdentity(model.StandardOutput, project, registry);
        var result = await CommandAsync(directory, ["config", "--hash", "*"], TimeSpan.FromSeconds(30), cancellationToken);
        var desired = ParseServiceHashes(result.StandardOutput);
        if (fileFingerprint != ConfigurationFingerprint(directory))
            throw new InvalidOperationException("Compose configuration changed during preflight.");
        var changes = DescribeDifferences(desired, members);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileFingerprint + "\n" +
            string.Join('\n', desired.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}:{item.Value}")) + "\n" +
            string.Join('\n', members.OrderBy(item => item.ID, StringComparer.Ordinal).Select(item =>
                $"{item.ID}:{Label(item, "com.docker.compose.config-hash")}"))))).ToLowerInvariant();
        return new(directory, project, fingerprint, changes, desired.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<string?> ExecuteAsync(RegisteredActionRequest request, ComposeProjectPreview approved, CancellationToken cancellationToken)
    {
        var current = await PreviewAsync(request, cancellationToken);
        if (current.Fingerprint != approved.Fingerprint)
            throw new InvalidOperationException("Compose configuration or service membership changed after confirmation.");
        string[] arguments = request.Action switch
        {
            "start" => ["up", "-d", "--no-build", "--pull", "never"],
            "stop" => ["stop"],
            "restart" => ["restart"],
            "pull" => ["pull"],
            "redeploy" => ["up", "-d", "--no-build", "--pull", "never", "--force-recreate"],
            _ => throw new InvalidOperationException("The Compose project action is not approved.")
        };
        await CommandAsync(current.Directory, arguments, TimeSpan.FromHours(2), cancellationToken);
        if (request.Action is "start" or "restart" or "redeploy")
        {
            var consecutiveHealthy = 0;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var observed = Members(await docker.ListContainersAsync(true, cancellationToken), current.Project);
                var healthy = IsHealthy(observed, current.Services);
                if (healthy)
                {
                    foreach (var member in observed.Where(item => current.Services.Contains(Label(item, "com.docker.compose.service"), StringComparer.Ordinal)))
                    {
                        if (!DockerEngine.IsStableContainerState((await docker.InspectContainerAsync(member.ID, cancellationToken)).State, DateTimeOffset.UtcNow))
                        {
                            healthy = false;
                            break;
                        }
                    }
                }
                consecutiveHealthy = healthy ? consecutiveHealthy + 1 : 0;
                if (consecutiveHealthy >= 3) break;
                if (attempt == 19) throw new InvalidOperationException("Compose services did not become healthy; configuration and volumes are retained.");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        // Never remove orphan services or volumes implicitly. Failed jobs retain
        // their configuration and bounded error code for repair and retry.
        var members = Members(await docker.ListContainersAsync(true, cancellationToken), current.Project);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            project = current.Project, action = request.Action, containerCount = members.Length,
            state = State(members), configurationRetained = true, volumesRetained = true
        });
    }

    private Task<ProcessResult> CommandAsync(string directory, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        processes.RunAsync("/usr/bin/docker", ["compose", "--project-directory", directory, "--file", Path.Combine(directory, "compose.yml"),
            "--env-file", Path.Combine(directory, ".env"), .. arguments], null, timeout, "COMPOSE_PROJECT_COMMAND_FAILED", cancellationToken, directory);

    public static string ParseResourceId(string resourceId)
    {
        const string prefix = "application:";
        if (!resourceId.StartsWith(prefix, StringComparison.Ordinal) || !SlugPattern().IsMatch(resourceId[prefix.Length..]))
            throw new InvalidOperationException("Only registered application project IDs are accepted.");
        return resourceId[prefix.Length..];
    }

    public static string ResolveDirectory(string root, string slug)
    {
        if (!SlugPattern().IsMatch(slug)) throw new InvalidOperationException("Invalid Compose project identifier.");
        var fullRoot = Path.GetFullPath(root);
        var directory = Path.Combine(fullRoot, slug);
        // Check every ancestor, not just the final child: a symlinked root would
        // otherwise allow the approved directory to resolve outside the registry.
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked Compose directories are not approved.");
        if (!Directory.Exists(directory)) throw new InvalidOperationException("The managed Compose installation was not found.");
        foreach (var name in ConfigurationFiles)
        {
            var file = new FileInfo(Path.Combine(directory, name));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 64 * 1024)
                throw new InvalidOperationException("Compose inputs must be bounded regular installation files.");
        }
        return directory;
    }

    public static string ConfigurationFingerprint(string directory)
    {
        var hashes = new List<string>();
        foreach (var name in ConfigurationFiles)
        {
            var file = new FileInfo(Path.Combine(directory, name));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 65536)
                throw new InvalidOperationException("Compose inputs changed or exceed their bounds.");
            using var stream = file.OpenRead();
            var bytes = new byte[65537];
            var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (read > 65536) throw new InvalidOperationException("Compose input exceeds its bounds.");
            hashes.Add(name + ":" + Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, read))));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', hashes)))).ToLowerInvariant();
    }

    public static void ValidateProjectIdentity(string model, string project, ResourceRegistry registry)
    {
        using var document = JsonDocument.Parse(model);
        var root = document.RootElement;
        if (!root.TryGetProperty("name", out var name) || name.GetString() != project ||
            !root.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Object ||
            services.EnumerateObject().Count() is < 1 or > 64)
            throw new InvalidOperationException("The configuration no longer describes the registered Compose project.");
        foreach (var service in services.EnumerateObject())
        {
            if (!ServicePattern().IsMatch(service.Name)) throw new InvalidOperationException("Invalid Compose service identifier.");
            var value = service.Value;
            var containerName = value.TryGetProperty("container_name", out var configuredName) ? configuredName.GetString() ?? string.Empty : string.Empty;
            if (registry.IsProtectedContainer(containerName, null) ||
                value.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Object &&
                labels.TryGetProperty("io.whaledeck.protected", out var protectedLabel) && protectedLabel.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Compose configuration targets protected resources.");
        }
    }

    public static Dictionary<string, string> ParseServiceHashes(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !ServicePattern().IsMatch(parts[0]) || parts[1].Length != 64 || parts[1].Any(character => !Uri.IsHexDigit(character)) ||
                !result.TryAdd(parts[0], parts[1]) || result.Count > 64)
                throw new InvalidOperationException("Compose configuration hash output is invalid or exceeds its bounds.");
        }
        if (result.Count == 0) throw new InvalidOperationException("Compose configuration has no enabled services.");
        return result;
    }

    public static string[] DescribeDifferences(IReadOnlyDictionary<string, string> desired, IEnumerable<ContainerListResponse> members)
    {
        var containers = members.ToArray();
        var changes = new List<string>();
        foreach (var (service, hash) in desired.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var instances = containers.Where(item => Label(item, "com.docker.compose.service") == service).ToArray();
            var state = instances.Length == 0 ? "Missing" : instances.All(item => Label(item, "com.docker.compose.config-hash") == hash)
                ? "Unchanged" : "ConfigurationChanged";
            changes.Add($"{service}: {state}; affected containers: {instances.Length}");
        }
        foreach (var service in containers.Select(item => Label(item, "com.docker.compose.service", "unknown"))
                     .Distinct(StringComparer.Ordinal).Where(service => !desired.ContainsKey(service)).Order(StringComparer.Ordinal))
            changes.Add($"{service}: Orphaned; retained (no automatic deletion)");
        return changes.ToArray();
    }

    public static ContainerListResponse[] Members(IEnumerable<ContainerListResponse> containers, string project) =>
        containers.Where(item => Label(item, "com.docker.compose.project") == project).ToArray();

    public static string Label(ContainerListResponse container, string name, string fallback = "") =>
        container.Labels.TryGetValue(name, out var value) ? value : fallback;

    public static bool IsHealthy(IEnumerable<ContainerListResponse> members, IEnumerable<string> expectedServices)
    {
        var containers = members.ToArray();
        return expectedServices.All(service =>
        {
            var instances = containers.Where(item => Label(item, "com.docker.compose.service") == service).ToArray();
            return instances.Length > 0 && instances.All(item => item.State == "running" &&
                !(item.Status ?? string.Empty).Contains("unhealthy", StringComparison.OrdinalIgnoreCase) &&
                !(item.Status ?? string.Empty).Contains("health: starting", StringComparison.OrdinalIgnoreCase));
        });
    }

    public static string State(IReadOnlyCollection<ContainerListResponse> members) => members.Count == 0 ? "Missing" :
        members.Any(item => item.State == "restarting") ? "Restarting" : members.All(item => item.State == "running") ? "Running" :
        members.All(item => item.State is "exited" or "created") ? "Stopped" : "Partial";

    [GeneratedRegex("\\A[a-z0-9][a-z0-9-]{0,62}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
    [GeneratedRegex("\\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ServicePattern();
}
