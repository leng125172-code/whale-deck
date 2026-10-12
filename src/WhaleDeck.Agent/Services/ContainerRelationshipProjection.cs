using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Projects selected native Docker inspect fields, never the raw Config or driver options.</summary>
public static class ContainerRelationshipProjection
{
    public static ContainerRelationships Build(Docker.DotNet.Models.ContainerInspectResponse metadata)
    {
        var response = new ContainerRelationships();
        var remainingCharacters = 262_144;
        string Text(string? value)
        {
            if (value is null) return string.Empty;
            var length = Math.Min(value.Length, Math.Min(4096, remainingCharacters));
            if (length < value.Length) response.IsTruncated = true;
            remainingCharacters -= length;
            return value[..length];
        }
        response.ImageId = Text(metadata.Image);
        response.ComposeProject = Text(metadata.Config?.Labels?.TryGetValue("com.docker.compose.project", out var project) == true ? project : null);
        response.ComposeService = Text(metadata.Config?.Labels?.TryGetValue("com.docker.compose.service", out var service) == true ? service : null);
        response.NetworkMode = Text(metadata.HostConfig?.NetworkMode);
        var networks = metadata.NetworkSettings?.Networks;
        if (networks?.Count > 128) response.IsTruncated = true;
        foreach (var (name, endpoint) in (networks?.AsEnumerable() ?? []).OrderBy(item => item.Key, StringComparer.Ordinal).Take(128))
            response.Endpoints.Add(new ContainerNetworkEndpoint
            {
                Name = Text(name), NetworkId = Text(endpoint.NetworkID), Ipv4Address = Text(endpoint.IPAddress),
                Ipv4PrefixLength = endpoint.IPPrefixLen, Ipv4Gateway = Text(endpoint.Gateway),
                Ipv6Address = Text(endpoint.GlobalIPv6Address), Ipv6PrefixLength = endpoint.GlobalIPv6PrefixLen,
                Ipv6Gateway = Text(endpoint.IPv6Gateway), MacAddress = Text(endpoint.MacAddress)
            });
        if (metadata.Mounts?.Count > 256) response.IsTruncated = true;
        foreach (var mount in (metadata.Mounts ?? []).OrderBy(item => item.Destination, StringComparer.Ordinal).Take(256))
            response.Mounts.Add(new ContainerMount
            {
                Type = Text(mount.Type), Name = Text(mount.Name), Source = Text(mount.Source),
                Destination = Text(mount.Destination), ReadWrite = mount.RW
            });
        // NetworkSettings includes resolved ephemeral host ports. Stopped containers can fall back to configured bindings.
        var runtimeBindings = metadata.NetworkSettings?.Ports;
        var bindings = runtimeBindings is { Count: > 0 } ? runtimeBindings : metadata.HostConfig?.PortBindings;
        var ports = (metadata.Config?.ExposedPorts?.Keys ?? []).Concat(bindings?.Keys ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var port in ports)
        {
            var published = bindings?.TryGetValue(port, out var configured) == true ? configured : null;
            if (published is not { Count: > 0 })
                Add(new ContainerPortBinding { ContainerPort = Text(port) });
            else foreach (var binding in published)
                Add(new ContainerPortBinding { ContainerPort = Text(port), HostAddress = Text(binding.HostIP),
                    HostPort = Text(binding.HostPort), Published = true });
        }
        return response;

        void Add(ContainerPortBinding binding)
        {
            if (response.PortBindings.Count < 256) response.PortBindings.Add(binding);
            else response.IsTruncated = true;
        }
    }
}
