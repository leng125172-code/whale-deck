using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace WhaleDeck.Agent.Services;

public sealed record ApplicationConfigurationCandidate(JsonObject Model, string Environment, JsonObject Manifest,
    bool ApplyImmediately, IReadOnlyCollection<string> Changes);

/// <summary>Overlays approved parameters; never accepts arbitrary Compose YAML or host paths.</summary>
public static class ApplicationConfigurationPatch
{
    private static readonly HashSet<string> ServiceFields = new(StringComparer.Ordinal)
        { "image", "cpus", "memoryMb", "restartPolicy", "ports", "command", "entrypoint", "volumes", "networks", "networkMode" };

    public static ApplicationConfigurationCandidate Build(JsonObject source, string environment, JsonObject manifest,
        JsonObject input, string directory, ResourceRegistry registry, IReadOnlySet<string> approvedNetworks)
    {
        if (input.Any(item => item.Key is not ("services" or "environment" or "autoUpdate" or "versionPolicy" or "maintenanceWindow" or "dependencies" or "apply"))) throw Invalid();
        var model = source.DeepClone().AsObject();
        var nextManifest = manifest.DeepClone().AsObject();
        var slug = manifest["slug"]?.GetValue<string>() ?? throw Invalid();
        _ = ComposeProjectManager.ParseResourceId("application:" + slug);
        ComposeProjectManager.ValidateProjectIdentity(model.ToJsonString(), "whaledeck-app-" + slug, registry);
        var services = model["services"]?.AsObject() ?? throw Invalid();
        var patches = input["services"] as JsonObject ?? new JsonObject();
        if (input.ContainsKey("services") && input["services"] is not JsonObject || patches.Count > 64) throw Invalid();
        var changes = new List<string>();
        foreach (var (name, node) in patches)
        {
            var service = services[name]?.AsObject() ?? throw Invalid();
            var fields = node?.AsObject() ?? throw Invalid();
            if (fields.Any(item => !ServiceFields.Contains(item.Key))) throw Invalid();
            foreach (var (field, value) in fields)
            {
                switch (field)
                {
                    case "image":
                        var image = Text(value, 256);
                        if (image.Any(char.IsControl)) throw Invalid();
                        ManagedActionExecutor.ValidateImage(image);
                        service["image"] = image;
                        break;
                    case "cpus":
                        var cpus = value?.GetValue<double>() ?? throw Invalid();
                        if (!double.IsFinite(cpus) || cpus < 0 || cpus > 4096) throw Invalid();
                        SetLimit(service, "cpus", "cpus", cpus == 0 ? null : JsonValue.Create(cpus));
                        break;
                    case "memoryMb":
                        var memory = value?.GetValue<long>() ?? throw Invalid();
                        if (memory < 0 || memory > 1_048_576) throw Invalid();
                        SetLimit(service, "mem_limit", "memory", memory == 0 ? null : JsonValue.Create(checked(memory * 1024 * 1024)));
                        break;
                    case "restartPolicy":
                        var policy = Text(value, 32);
                        if (policy is not ("no" or "always" or "unless-stopped" or "on-failure")) throw Invalid();
                        service["restart"] = policy;
                        break;
                    case "ports":
                        service["ports"] = new JsonArray(Strings(value, 128, 96).Select(Port).Cast<JsonNode?>().ToArray());
                        break;
                    case "command":
                    case "entrypoint":
                        if (value is null) service.Remove(field);
                        else service[field] = new JsonArray(Strings(value, 256, 4096).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
                        break;
                    case "volumes":
                        var volumes = value?.AsArray() ?? throw Invalid();
                        if (volumes.Count > 128) throw Invalid();
                        service["volumes"] = new JsonArray(volumes.Select(item => Mount(item?.AsObject() ?? throw Invalid(), model, directory, slug)).Cast<JsonNode?>().ToArray());
                        break;
                    case "networks":
                        var networks = Strings(value, 64, 256);
                        var attachments = new JsonObject();
                        foreach (var network in networks)
                        {
                            if (!approvedNetworks.Contains(network)) throw Invalid();
                            var definitions = model["networks"] as JsonObject;
                            if (definitions is null) { definitions = new JsonObject(); model["networks"] = definitions; }
                            // Stable logical keys cannot collide with unrelated existing definitions.
                            var key = "wd_attach_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(network)))[..16].ToLowerInvariant();
                            var definition = new JsonObject { ["name"] = network, ["external"] = true };
                            if (definitions[key] is { } existing && !JsonNode.DeepEquals(existing, definition)) throw Invalid();
                            definitions[key] = definition;
                            attachments[key] = null;
                        }
                        service.Remove("network_mode");
                        service["networks"] = attachments;
                        break;
                    case "networkMode":
                        var mode = Text(value, 16);
                        if (mode is not ("bridge" or "none")) throw Invalid();
                        if (mode == "none") { service.Remove("networks"); service["network_mode"] = "none"; }
                        else service.Remove("network_mode");
                        break;
                }
                changes.Add($"Service {name}: {field} override changed (values redacted)");
            }
        }
        if (input.ContainsKey("environment") && input["environment"] is not JsonObject) throw Invalid();
        var envPatch = input["environment"]?.AsObject();
        var nextEnvironment = ApplicationEnvironmentPatch.Apply(environment, envPatch);
        if (envPatch is not null)
        {
            var modified = (envPatch["set"]?.AsObject().Select(item => item.Key) ?? [])
                .Concat(envPatch["remove"]?.AsArray().Select(item => Text(item, 128)) ?? []).ToHashSet(StringComparer.Ordinal);
            foreach (var service in services.Select(item => item.Value?.AsObject() ?? throw Invalid()))
                if (service["environment"] is JsonObject inline)
                    foreach (var key in modified) inline.Remove(key);
            changes.Add("Project environment overrides changed for all services (values redacted)");
        }
        if (input["autoUpdate"] is { } enabled)
        {
            var enabledValue = enabled.GetValue<bool>();
            nextManifest["autoUpdate"] = enabledValue;
            foreach (var service in services.Select(item => item.Value?.AsObject() ?? throw Invalid()))
            {
                var labels = service["labels"] as JsonObject;
                if (labels is null) { labels = new JsonObject(); service["labels"] = labels; }
                labels["autoupdate"] = enabledValue ? "true" : "false";
                labels["io.whaledeck.autoupdate"] = enabledValue ? "true" : "false";
            }
            changes.Add("Application automatic-update policy changed");
        }
        foreach (var field in new[] { "versionPolicy", "maintenanceWindow" })
            if (input[field] is { } setting)
            {
                var value = Text(setting, 128);
                if (value.Any(char.IsControl)) throw Invalid();
                if (field == "maintenanceWindow") ManagedActionExecutor.ValidateMaintenanceWindow(value);
                nextManifest[field] = value;
                changes.Add("Application " + field + " changed");
            }
        if (input["dependencies"] is { } dependencies)
        {
            var values = Strings(dependencies, 64, 64);
            foreach (var dependency in values) _ = ComposeProjectManager.ParseResourceId("application:" + dependency);
            if (values.Contains(slug, StringComparer.Ordinal)) throw Invalid();
            nextManifest["dependencies"] = new JsonArray(values.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
            changes.Add("Application dependencies changed");
        }
        ValidateSafety(model, directory, slug);
        ComposeProjectManager.ValidateProjectIdentity(model.ToJsonString(), "whaledeck-app-" + slug, registry);
        var apply = input["apply"]?.GetValue<bool>() ?? false;
        if (model.ToJsonString().Length > 65_536 || nextManifest.ToJsonString().Length > 65_536) throw Invalid();
        return new(model, nextEnvironment, nextManifest, apply, changes);
    }

    public static void ValidateSafety(JsonObject model, string directory, string slug)
    {
        if (model["secrets"] is JsonObject { Count: > 0 } || model["configs"] is JsonObject { Count: > 0 }) throw Invalid();
        foreach (var service in model["services"]!.AsObject().Select(item => item.Value!.AsObject()))
        {
            if (service["privileged"]?.GetValue<bool>() == true || service["build"] is not null ||
                service["devices"] is JsonArray { Count: > 0 } || service["cap_add"] is JsonArray { Count: > 0 } ||
                service["device_cgroup_rules"] is JsonArray { Count: > 0 } ||
                service["pid"]?.ToString() == "host" || service["ipc"]?.ToString() == "host" ||
                service["network_mode"] is { } mode && mode.ToString() is not ("none" or "bridge")) throw Invalid();
            foreach (var file in service["env_file"]?.AsArray() ?? [])
            {
                var path = file is JsonObject entry ? entry["path"]?.GetValue<string>() : file?.GetValue<string>();
                if (path is null || Path.GetFullPath(Path.Combine(directory, path)) != Path.Combine(Path.GetFullPath(directory), ".env")) throw Invalid();
            }
            foreach (var mount in service["volumes"]?.AsArray() ?? [])
                ValidateMount(mount!.AsObject(), model, directory, slug);
        }
    }

    private static void SetLimit(JsonObject service, string field, string deployField, JsonNode? value)
    {
        if (value is null) service.Remove(field); else service[field] = value.DeepClone();
        if (service["deploy"]?["resources"]?["limits"] is JsonObject limits && limits.ContainsKey(deployField))
        {
            if (value is null) limits.Remove(deployField); else limits[deployField] = value.DeepClone();
        }
    }

    private static JsonObject Mount(JsonObject requested, JsonObject model, string directory, string slug)
    {
        if (requested.Any(item => item.Key is not ("type" or "source" or "target" or "readOnly"))) throw Invalid();
        var type = Text(requested["type"], 16);
        var source = requested["source"]?.GetValue<string>() ?? "";
        var target = Text(requested["target"], 1024);
        if (!target.StartsWith('/') || target.Contains('\0') || target.Contains("..", StringComparison.Ordinal)) throw Invalid();
        var mount = new JsonObject { ["type"] = type, ["target"] = target, ["read_only"] = requested["readOnly"]?.GetValue<bool>() ?? false };
        if (type == "volume" && source.Length > 0)
        {
            ValidateVolumeName(source, slug);
            var definitions = model["volumes"] as JsonObject;
            if (definitions is null) { definitions = new JsonObject(); model["volumes"] = definitions; }
            if (definitions[source] is { } existing && existing["name"]?.ToString() != source) throw Invalid();
            if (!definitions.ContainsKey(source)) definitions[source] = new JsonObject { ["name"] = source };
            mount["source"] = source;
        }
        else if (source.Length > 0) mount["source"] = source;
        ValidateMount(mount, model, directory, slug);
        return mount;
    }

    private static void ValidateMount(JsonObject mount, JsonObject model, string directory, string slug)
    {
        var type = mount["type"]?.ToString();
        var source = mount["source"]?.ToString() ?? "";
        if (type == "bind")
        {
            // Mount only the stable data directory. A container cannot replace its parent configuration or mount root.
            if (Path.GetFullPath(source) != Path.Combine(Path.GetFullPath(directory), "data")) throw Invalid();
            for (var ancestor = new DirectoryInfo(source); ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw Invalid();
        }
        else if (type == "volume" && source.Length > 0)
        {
            var definition = model["volumes"]?[source];
            if (definition?["driver_opts"] is JsonObject { Count: > 0 } ||
                definition?["driver"] is { } driver && driver.ToString() != "local") throw Invalid();
            ValidateVolumeName(definition?["name"]?.ToString() ?? source, slug);
        }
        else if (type != "volume" && type != "tmpfs" || type == "tmpfs" && source.Length > 0) throw Invalid();
    }

    private static void ValidateVolumeName(string name, string slug)
    {
        if (!name.StartsWith("whaledeck-app-" + slug + "-", StringComparison.Ordinal) || name.Length > 256 ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))) throw Invalid();
    }

    private static JsonObject Port(string value)
    {
        var split = value.Split('/');
        if (split.Length > 2) throw Invalid();
        var protocol = split.Length == 2 ? split[1] : "tcp";
        if (protocol is not ("tcp" or "udp")) throw Invalid();
        var address = "0.0.0.0";
        var reference = split[0];
        if (reference.StartsWith('['))
        {
            var end = reference.IndexOf(']');
            if (end < 1 || reference.Length <= end + 1 || reference[end + 1] != ':') throw Invalid();
            address = reference[1..end]; reference = reference[(end + 2)..];
        }
        var parts = reference.Split(':');
        if (parts.Length == 3) { address = parts[0]; parts = parts[1..]; }
        if (parts.Length != 2 || !IPAddress.TryParse(address, out _) ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var host) || host is < 0 or > 65535 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var target) || target is < 1 or > 65535 ||
            host is 8080 or 8081 or 18080 or 18081) throw Invalid();
        return new() { ["target"] = target, ["published"] = host.ToString(CultureInfo.InvariantCulture), ["host_ip"] = address, ["protocol"] = protocol };
    }

    private static string[] Strings(JsonNode? value, int count, int length)
    {
        var values = value?.AsArray() ?? throw Invalid();
        if (values.Count > count) throw Invalid();
        return values.Select(item => Text(item, length)).ToArray();
    }
    private static string Text(JsonNode? value, int maximum)
    {
        var text = value?.GetValue<string>() ?? throw Invalid();
        if (text.Length > maximum || text.Contains('\0')) throw Invalid();
        return text;
    }
    private static InvalidOperationException Invalid() => new("Application configuration is invalid or outside its approved template and resource scope.");
}
