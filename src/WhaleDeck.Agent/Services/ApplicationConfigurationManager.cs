using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Docker.DotNet.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed record ApplicationConfigurationPreview(string Directory, string Project, string SourceFingerprint,
    string Fingerprint, ApplicationConfigurationCandidate Candidate);

/// <summary>Approved structured edits only; source and candidate values never leave the host in plans or snapshots.</summary>
public sealed class ApplicationConfigurationManager(ResourceRegistry registry, DockerEngine docker,
    BoundedProcessRunner processes, IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly string _keyFile = configuration["Agent:CapabilityKeyFile"] ?? "/etc/whaledeck/agent-capability.key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApplicationConfigurationResponse> ReadAsync(string resourceId, CancellationToken cancellationToken)
    {
        var source = await ReadSourceAsync(resourceId, cancellationToken);
        ApplicationConfigurationPatch.ValidateSafety(source.Model, source.Directory, source.Slug);
        return Describe(source.Model, source.Manifest, source.Environment, resourceId, source.Fingerprint,
            source.Directory, source.Networks);
    }

    public async Task<ApplicationConfigurationPreview> PreviewAsync(RegisteredActionRequest request, CancellationToken cancellationToken)
    {
        if (request.Action != "configure" || request.Parameters.Any(item => item.Key is not
            ("slug" or "expectedFingerprint" or "inputKind" or "inputTicket" or "environment")) ||
            request.Parameters.GetValueOrDefault("inputKind") != "environment") throw Invalid();
        var source = await ReadSourceAsync(request.Resource.ResourceId, cancellationToken);
        if (request.Parameters.GetValueOrDefault("slug") != source.Slug ||
            request.Parameters.GetValueOrDefault("expectedFingerprint") != source.Fingerprint) throw Conflict();
        var input = request.Parameters.GetValueOrDefault("environment") ?? throw Invalid();
        if (input.Length > 65_536) throw Invalid();
        JsonObject patch;
        try { patch = JsonNode.Parse(input)?.AsObject() ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
        var candidate = ApplicationConfigurationPatch.Build(source.Model, source.Environment, source.Manifest, patch,
            source.Directory, registry, source.Networks.ToHashSet(StringComparer.Ordinal));
        candidate.Manifest["configurationSavedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        // The timestamp is operational metadata, not part of the approved semantic change.
        var dependencies = candidate.Manifest["dependencies"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? [];
        foreach (var dependency in dependencies)
            _ = ComposeProjectManager.ResolveDirectory(registry.Require("whaledeck.apps", "configure").Path!, dependency);
        var validationBinding = await ValidateCandidateAsync(candidate, source.Directory, source.Project, source.Slug, source.Networks, cancellationToken);
        candidate.Manifest["image"] = validationBinding.PrimaryImage;
        var candidateHash = Seal(CandidateFingerprint(candidate, source.Fingerprint) + "\n" + validationBinding.Binding);
        if (await CurrentFingerprintAsync(request.Resource.ResourceId, cancellationToken) != source.Fingerprint) throw Conflict();
        return new(source.Directory, source.Project, source.Fingerprint, candidateHash, candidate);
    }

    public async Task<string?> ExecuteAsync(RegisteredActionRequest request, ApplicationConfigurationPreview approved,
        CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(approved.Project, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await PreviewAsync(request, cancellationToken);
            if (current.Fingerprint != approved.Fingerprint) throw Conflict();
            var candidate = current.Candidate;
            // Stage all files before replacing any. Each rename is atomic; partial I/O fails the job,
            // retains data and can be repaired in dev. No implicit configuration rollback or volume deletion.
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".env"] = candidate.Environment, ["compose.yml"] = candidate.Model.ToJsonString(JsonOptions),
                ["manifest.json"] = candidate.Manifest.ToJsonString(JsonOptions)
            };
            var staged = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var (name, content) in files)
                {
                    var temporary = Path.Combine(current.Directory, ".wd-config-" + Guid.NewGuid().ToString("N"));
                    staged[name] = temporary;
                    await PrivateWriteAsync(temporary, content, cancellationToken);
                }
                if (await CurrentFingerprintAsync(request.Resource.ResourceId, cancellationToken) != current.SourceFingerprint) throw Conflict();
                foreach (var (name, temporary) in staged) File.Move(temporary, Path.Combine(current.Directory, name), overwrite: true);
            }
            finally
            {
                foreach (var temporary in staged.Values) if (File.Exists(temporary)) File.Delete(temporary);
            }
            if (candidate.ApplyImmediately)
            {
                // Existing images only: no implicit build, pull, orphan removal or data-volume cleanup.
                await CommandAsync(current.Directory, ["up", "-d", "--no-build", "--pull", "never", "--force-recreate"], cancellationToken);
                var services = candidate.Model["services"]!.AsObject().Select(item => item.Key).ToArray();
                var healthy = 0;
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    var members = ComposeProjectManager.Members(await docker.ListContainersAsync(true, cancellationToken), current.Project);
                    var stable = ComposeProjectManager.IsHealthy(members, services);
                    foreach (var member in stable ? members : [])
                        if (!DockerEngine.IsStableContainerState((await docker.InspectContainerAsync(member.ID, cancellationToken)).State, DateTimeOffset.UtcNow)) stable = false;
                    healthy = stable ? healthy + 1 : 0;
                    if (healthy >= 3) break;
                    if (attempt == 29) throw new InvalidOperationException("Configured application did not become healthy. Data and new configuration are retained for repair.");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            var observed = ComposeProjectManager.Members(await docker.ListContainersAsync(true, cancellationToken), current.Project);
            return JsonSerializer.Serialize(new
            {
                slug = ComposeProjectManager.ParseResourceId(request.Resource.ResourceId), configurationSaved = true,
                applied = candidate.ApplyImmediately, desiredImage = candidate.Manifest["image"]?.ToString(),
                autoUpdate = candidate.Manifest["autoUpdate"]?.GetValue<bool>() ?? false,
                versionPolicy = candidate.Manifest["versionPolicy"]?.ToString() ?? "*",
                maintenanceWindow = candidate.Manifest["maintenanceWindow"]?.ToString() ?? "Sun@20:00-23:59",
                state = ComposeProjectManager.State(observed), dataRetained = true,
                actualImage = observed.FirstOrDefault()?.Image,
                dependencies = candidate.Manifest["dependencies"]?.DeepClone(),
                fingerprint = await CurrentFingerprintAsync(request.Resource.ResourceId, cancellationToken)
            });
        }
        finally { gate.Release(); }
    }

    private async Task<Source> ReadSourceAsync(string resourceId, CancellationToken cancellationToken)
    {
        var slug = ComposeProjectManager.ParseResourceId(resourceId);
        var directory = ComposeProjectManager.ResolveDirectory(registry.Require("whaledeck.apps", "configure").Path!, slug);
        var project = "whaledeck-app-" + slug;
        var files = ComposeProjectManager.ConfigurationFingerprint(directory);
        var containers = await docker.ListContainersAsync(true, cancellationToken);
        var members = ComposeProjectManager.Members(containers, project);
        if (members.Any(item => item.Names.Any(name => registry.IsProtectedContainer(name, item.Labels)))) throw Invalid();
        var material = await CommandAsync(directory, ["config", "--format", "json", "--no-interpolate", "--no-env-resolution"], cancellationToken);
        JsonObject model, manifest;
        try
        {
            model = JsonNode.Parse(material.StandardOutput)?.AsObject() ?? throw Invalid();
            manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"), cancellationToken))?.AsObject() ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
        if (manifest["slug"]?.ToString() != slug) throw Invalid();
        ComposeProjectManager.ValidateProjectIdentity(model.ToJsonString(), project, registry);
        var environment = await File.ReadAllTextAsync(Path.Combine(directory, ".env"), cancellationToken);
        _ = ApplicationEnvironmentPatch.Names(environment);
        var protectedNetworks = containers.Where(item => item.Names.Any(name => registry.IsProtectedContainer(name, item.Labels)))
            .SelectMany(item => item.NetworkSettings?.Networks?.Values.Select(net => net.NetworkID) ?? []).ToHashSet(StringComparer.Ordinal);
        var networks = (await docker.ListNetworksAsync(cancellationToken)).Where(item => item.Driver == "bridge" && item.Name != "bridge" &&
            !protectedNetworks.Contains(item.ID) && !item.Name.StartsWith("database-platform", StringComparison.Ordinal) &&
            (!item.Name.StartsWith("whaledeck", StringComparison.Ordinal) || item.Name.StartsWith(project + "_", StringComparison.Ordinal)) &&
            !(item.Labels?.TryGetValue("io.whaledeck.protected", out var protection) == true && protection.Equals("true", StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(256).ToArray();
        if (files != ComposeProjectManager.ConfigurationFingerprint(directory)) throw Conflict();
        var identity = files + "\n" + string.Join('\n', members.OrderBy(item => item.ID, StringComparer.Ordinal)
            .Select(item => item.ID + ":" + item.State + ":" + ComposeProjectManager.Label(item, "com.docker.compose.config-hash")));
        return new(directory, project, slug, Seal("source\n" + identity), model, manifest, environment, networks);
    }

    private async Task<string> CurrentFingerprintAsync(string resourceId, CancellationToken cancellationToken) =>
        (await ReadSourceAsync(resourceId, cancellationToken)).Fingerprint;

    private string CandidateFingerprint(ApplicationConfigurationCandidate candidate, string source)
    {
        var manifest = candidate.Manifest.DeepClone().AsObject();
        manifest.Remove("configurationSavedAtUtc");
        return Seal("candidate\n" + source + "\n" + candidate.Model.ToJsonString() + "\n" + candidate.Environment + "\n" + manifest.ToJsonString() + "\n" + candidate.ApplyImmediately);
    }

    private string Seal(string value)
    {
        var key = File.ReadAllBytes(_keyFile);
        if (key.Length < 32) throw Invalid();
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private async Task<(string Binding, string PrimaryImage)> ValidateCandidateAsync(ApplicationConfigurationCandidate candidate, string directory, string project,
        string slug, string[] approvedNetworks, CancellationToken cancellationToken)
    {
        var model = candidate.Model;
        var temporaryRoot = Path.Combine("/run/whaledeck", "config-preflight");
        Directory.CreateDirectory(temporaryRoot);
        var temporary = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var validationModel = model.DeepClone().AsObject();
            foreach (var service in validationModel["services"]!.AsObject().Select(item => item.Value!.AsObject()))
                if (service.ContainsKey("env_file")) service["env_file"] = new JsonArray(JsonValue.Create(Path.Combine(temporary, ".env")));
            await PrivateWriteAsync(Path.Combine(temporary, "compose.yml"), validationModel.ToJsonString(JsonOptions), cancellationToken);
            await PrivateWriteAsync(Path.Combine(temporary, ".env"), candidate.Environment, cancellationToken);
            var result = await CommandAsync(temporary, ["config", "--format", "json"], cancellationToken);
            // Native validation can resolve interpolation into dangerous paths/names. Check its resolved model again.
            var resolved = JsonNode.Parse(result.StandardOutput)?.AsObject() ?? throw Invalid();
            foreach (var service in resolved["services"]!.AsObject().Select(item => item.Value!.AsObject())) service.Remove("env_file");
            ComposeProjectManager.ValidateProjectIdentity(resolved.ToJsonString(), project, registry);
            ApplicationConfigurationPatch.ValidateSafety(resolved, directory, slug);
            var bindings = new List<string>();
            var nativeNetworks = await docker.ListNetworksAsync(cancellationToken);
            foreach (var service in resolved["services"]!.AsObject().Select(item => item.Value!.AsObject()))
            {
                var image = service["image"]?.ToString() ?? throw Invalid();
                ManagedActionExecutor.ValidateImage(image);
                if (candidate.ApplyImmediately) bindings.Add("image:" + (await docker.InspectImageAsync(image, false, cancellationToken)).ID);
                foreach (var network in service["networks"]?.AsObject() ?? [])
                {
                    var name = resolved["networks"]?[network.Key]?["name"]?.ToString() ?? project + "_" + network.Key;
                    if (!approvedNetworks.Contains(name, StringComparer.Ordinal) && name != project + "_default") throw Invalid();
                    bindings.Add("network:" + name + ":" + nativeNetworks.FirstOrDefault(item => item.Name == name)?.ID);
                }
            }
            var containers = await docker.ListContainersAsync(true, cancellationToken);
            var ownIds = ComposeProjectManager.Members(containers, project).Select(item => item.ID).ToHashSet(StringComparer.Ordinal);
            var ownPorts = containers.Where(item => ownIds.Contains(item.ID)).SelectMany(item => item.Ports ?? [])
                .Where(port => port.PublicPort > 0).Select(port => ((int)port.PublicPort, port.Type)).ToHashSet();
            var occupied = containers.Where(item => !ownIds.Contains(item.ID)).SelectMany(item => item.Ports ?? [])
                .Where(port => port.PublicPort > 0).Select(port => (port.IP ?? "0.0.0.0", (int)port.PublicPort, port.Type)).ToList();
            foreach (var listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (!ownPorts.Contains((listener.Port, "tcp"))) occupied.Add((listener.Address.ToString(), listener.Port, "tcp"));
            foreach (var listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                if (!ownPorts.Contains((listener.Port, "udp"))) occupied.Add((listener.Address.ToString(), listener.Port, "udp"));
            ValidatePublishedPorts(resolved, occupied);
            return (string.Join('\n', bindings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                resolved["services"]!.AsObject().First().Value!["image"]!.ToString());
        }
        finally
        {
            // Exact, freshly generated host-private directory; contains only our two regular temporary files.
            foreach (var name in new[] { "compose.yml", ".env" }) if (File.Exists(Path.Combine(temporary, name))) File.Delete(Path.Combine(temporary, name));
            Directory.Delete(temporary);
        }
    }

    internal static void ValidatePublishedPorts(JsonObject model, IEnumerable<(string Address, int Port, string Protocol)> occupied)
    {
        var requested = new List<(string Address, int Port, string Protocol)>();
        foreach (var service in model["services"]!.AsObject().Select(item => item.Value!.AsObject()))
            foreach (var port in service["ports"]?.AsArray() ?? [])
            {
                var address = port!["host_ip"]?.ToString() ?? "0.0.0.0";
                var protocol = port["protocol"]?.ToString() ?? "tcp";
                if (!IPAddress.TryParse(address, out _) || protocol is not ("tcp" or "udp")) throw Invalid();
                if (port["published"] is null) continue;
                if (!int.TryParse(port["published"]!.ToString(), out var published) || published is < 0 or > 65535 ||
                    published is 8080 or 8081 or 18080 or 18081) throw Invalid();
                if (published == 0) continue;
                if (occupied.Concat(requested).Any(other => other.Port == published && other.Protocol == protocol &&
                    (IPAddress.Parse(address).Equals(IPAddress.Parse(other.Address)) || address is "0.0.0.0" or "::" || other.Address is "0.0.0.0" or "::"))) throw Invalid();
                requested.Add((address, published, protocol));
            }
    }

    internal static ApplicationConfigurationResponse Describe(JsonObject model, JsonObject manifest, string environment,
        string resourceId, string fingerprint, string directory, string[] approvedNetworks)
    {
        var response = new ApplicationConfigurationResponse
        {
            ResourceId = resourceId, Fingerprint = fingerprint, AutoUpdate = manifest["autoUpdate"]?.GetValue<bool>() ?? false,
            VersionPolicy = manifest["versionPolicy"]?.ToString() ?? "*", MaintenanceWindow = manifest["maintenanceWindow"]?.ToString() ?? "Sun@20:00-23:59",
            OwnedDataDirectory = Path.Combine(directory, "data")
        };
        response.EnvironmentNames.AddRange(ApplicationEnvironmentPatch.Names(environment));
        response.ApprovedNetworks.AddRange(approvedNetworks);
        response.Dependencies.AddRange(manifest["dependencies"]?.AsArray().Select(item => item!.GetValue<string>()) ?? []);
        foreach (var (name, value) in model["services"]!.AsObject())
        {
            var service = value!.AsObject();
            var snapshot = new ApplicationServiceConfiguration
            {
                Name = name, Image = service["image"]?.ToString() ?? "", Cpus = Number(service["cpus"] ?? service["deploy"]?["resources"]?["limits"]?["cpus"]),
                MemoryMb = (long)(Number(service["mem_limit"] ?? service["deploy"]?["resources"]?["limits"]?["memory"]) / (1024 * 1024)),
                RestartPolicy = service["restart"]?.ToString() ?? "no", CommandPresent = service["command"] is not null,
                EntrypointPresent = service["entrypoint"] is not null, NetworkMode = service["network_mode"]?.ToString() ?? "bridge"
            };
            snapshot.EnvironmentNames.AddRange(service["environment"]?.AsObject().Select(item => item.Key) ?? []);
            foreach (var port in service["ports"]?.AsArray() ?? [])
            {
                var address = port!["host_ip"]?.ToString() ?? "0.0.0.0";
                if (address.Contains(':')) address = "[" + address + "]";
                snapshot.Ports.Add($"{address}:{(port["published"]?.ToString() ?? "0")}:{port["target"]}/{(port["protocol"]?.ToString() ?? "tcp")}");
            }
            foreach (var mount in service["volumes"]?.AsArray() ?? [])
                snapshot.Mounts.Add(new ApplicationConfigurationMount { Type = mount!["type"]!.ToString(),
                    Source = mount["type"]!.ToString() == "volume" ? model["volumes"]?[mount["source"]?.ToString() ?? ""]?["name"]?.ToString() ?? mount["source"]?.ToString() ?? "" : mount["source"]?.ToString() ?? "",
                    Target = mount["target"]?.ToString() ?? "", ReadOnly = mount["read_only"]?.GetValue<bool>() ?? false });
            snapshot.Networks.AddRange(service["networks"]?.AsObject().Select(item => model["networks"]?[item.Key]?["name"]?.ToString() ?? item.Key) ?? []);
            response.Services.Add(snapshot);
        }
        return response;
    }

    private static double Number(JsonNode? value) => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private Task<ProcessResult> CommandAsync(string directory, string[] arguments, CancellationToken cancellationToken) =>
        processes.RunAsync("/usr/bin/docker", ["compose", "--project-directory", directory, "--file", Path.Combine(directory, "compose.yml"),
            "--env-file", Path.Combine(directory, ".env"), .. arguments], null, TimeSpan.FromMinutes(5), "APPLICATION_CONFIGURATION_COMMAND_FAILED", cancellationToken, directory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                ["HOME"] = "/var/lib/whaledeck-agent", ["LANG"] = "C.UTF-8"
            }, clearEnvironment: true);

    private static async Task PrivateWriteAsync(string path, string value, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(value) > 65_536) throw Invalid();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(value), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static InvalidOperationException Invalid() => new("Application configuration is outside the approved resource scope.");
    private static InvalidOperationException Conflict() => new("Application configuration changed. Reload and confirm a fresh plan.");
    private sealed record Source(string Directory, string Project, string Slug, string Fingerprint, JsonObject Model,
        JsonObject Manifest, string Environment, string[] Networks);
}
