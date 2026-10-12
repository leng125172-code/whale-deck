using Docker.DotNet.Models;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRelationshipTests
{
    [Fact]
    public void NativeProjectionSeparatesNetworksMountsAndPublishedPortsWithoutSecrets()
    {
        var metadata = Metadata();
        var result = ContainerRelationshipProjection.Build(metadata);
        Assert.Equal("sha256:actual-image", result.ImageId);
        Assert.Equal("team", result.ComposeProject);
        Assert.Equal("web", result.ComposeService);
        var network = Assert.Single(result.Endpoints);
        Assert.Equal("network-id", network.NetworkId);
        Assert.Equal("172.19.0.2", network.Ipv4Address);
        Assert.Equal(24, network.Ipv4PrefixLength);
        Assert.Equal("fd00::2", network.Ipv6Address);
        Assert.Contains(result.Mounts, mount => mount.Type == "bind" && !mount.ReadWrite && mount.Name == "");
        Assert.Contains(result.Mounts, mount => mount.Type == "volume" && mount.Name == "team-data" && mount.ReadWrite);
        Assert.Contains(result.PortBindings, port => port.ContainerPort == "80/tcp" && port.HostPort == "18088" && port.Published);
        Assert.Contains(result.PortBindings, port => port.ContainerPort == "443/tcp" && !port.Published);
        Assert.DoesNotContain("must-not-leak", result.ToString());
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void StoppedAndEphemeralMappingsPreserveConfigurationButRuntimePortsWin()
    {
        var metadata = Metadata();
        metadata.NetworkSettings.Ports = new Dictionary<string, IList<PortBinding>>();
        metadata.HostConfig.PortBindings["80/tcp"] = [new() { HostIP = "::", HostPort = "" }];
        var configured = ContainerRelationshipProjection.Build(metadata);
        Assert.Contains(configured.PortBindings, port => port.Published && port.HostAddress == "::" && port.HostPort == "");
        metadata.NetworkSettings.Ports["80/tcp"] = [new() { HostIP = "127.0.0.1", HostPort = "32768" }];
        Assert.Contains(ContainerRelationshipProjection.Build(metadata).PortBindings,
            port => port.Published && port.HostPort == "32768");
    }

    [Fact]
    public void MissingAndOldAgentMetadataDoesNotInventRelationships()
    {
        var result = ContainerRelationshipProjection.Build(new());
        Assert.Empty(result.Endpoints);
        Assert.Empty(result.Mounts);
        Assert.Empty(result.PortBindings);
        Assert.Equal("", result.ComposeProject);
        var old = new WhaleDeck.Contracts.Agent.V1.ContainerInspectResponse { Id = "old" };
        Assert.Null(WhaleDeck.Contracts.Agent.V1.ContainerInspectResponse.Parser.ParseFrom(old.ToByteArray()).Relationships);
    }

    [Fact]
    public void RelationCollectionsAndStringsAreBoundedAndExplicitlyTruncated()
    {
        var metadata = Metadata();
        metadata.Image = new string('a', 5000);
        metadata.Mounts = Enumerable.Range(0, 300).Select(index => new MountPoint { Type = "volume", Name = "v" + index, Destination = "/" + index }).ToArray();
        metadata.NetworkSettings.Networks = Enumerable.Range(0, 140).ToDictionary(index => "n" + index, _ => new EndpointSettings());
        metadata.Config.ExposedPorts = Enumerable.Range(1, 300).ToDictionary(index => index + "/tcp", _ => default(EmptyStruct));
        var result = ContainerRelationshipProjection.Build(metadata);
        Assert.True(result.IsTruncated);
        Assert.Equal(4096, result.ImageId.Length);
        Assert.Equal(128, result.Endpoints.Count);
        Assert.Equal(256, result.Mounts.Count);
        Assert.Equal(256, result.PortBindings.Count);
        metadata.Mounts = Enumerable.Range(0, 256).Select(index => new MountPoint
            { Type = "bind", Source = new string('a', 4096), Destination = new string('b', 4096) + index }).ToArray();
        var large = ContainerRelationshipProjection.Build(metadata);
        Assert.True(large.IsTruncated);
        Assert.True(large.Mounts.Sum(mount => mount.Source.Length + mount.Destination.Length + mount.Type.Length + mount.Name.Length) <= 262_144);
    }

    [Fact]
    public void SummaryUsesExactImageNetworkAndVolumeIdentitiesWithoutExtraInspect()
    {
        var registry = new ResourceRegistry(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:ResourceRegistryFile"] = "missing-unit-test-registry.json" }).Build());
        var summary = DockerGrpcService.BuildContainerSummary(new ContainerListResponse
        {
            ID = "ordinary", Names = ["/demo"], Image = "tag", ImageID = "sha256:image", State = "running", Status = "Up",
            Labels = new Dictionary<string, string>(),
            Mounts = [new() { Type = "volume", Name = "named" }, new() { Type = "bind", Source = "/data/private" }],
            NetworkSettings = new() { Networks = new Dictionary<string, EndpointSettings> { ["net"] = new() { NetworkID = "network" } } }
        }, registry);
        Assert.Equal("sha256:image", summary.ImageId);
        Assert.Equal("named", Assert.Single(summary.VolumeNames));
        Assert.Equal("network", Assert.Single(summary.NetworkIds));
    }

    private static ContainerInspectResponse Metadata() => new()
    {
        Image = "sha256:actual-image",
        Config = new() { Image = "nginx:tag", Env = ["PASSWORD=must-not-leak"],
            Labels = new Dictionary<string, string> { ["com.docker.compose.project"] = "team", ["com.docker.compose.service"] = "web", ["secret"] = "must-not-leak" },
            ExposedPorts = new Dictionary<string, EmptyStruct> { ["80/tcp"] = default, ["443/tcp"] = default } },
        HostConfig = new() { NetworkMode = "team-net", PortBindings = new Dictionary<string, IList<PortBinding>>
            { ["80/tcp"] = [new() { HostIP = "127.0.0.1", HostPort = "18088" }] } },
        NetworkSettings = new() { Networks = new Dictionary<string, EndpointSettings> { ["team-net"] = new()
            { NetworkID = "network-id", IPAddress = "172.19.0.2", IPPrefixLen = 24, Gateway = "172.19.0.1", GlobalIPv6Address = "fd00::2", GlobalIPv6PrefixLen = 64 } } },
        Mounts = [new() { Type = "volume", Name = "team-data", Source = "/var/lib/docker/volumes/team-data/_data", Destination = "/data", RW = true },
            new() { Type = "bind", Source = "/data/very/long/path", Destination = "/config", RW = false }]
    };
}
