using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerSummaryTests
{
    private static ResourceRegistry Registry() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Agent:ResourceRegistryFile"] = "missing-unit-test-registry.json" }).Build());

    [Fact]
    public void RegistryProtectionWinsEvenIfLabelsAreRemovedOrMisleading()
    {
        var container = Container();
        container.Names = ["/whaledeck-api"];
        container.Labels["io.whaledeck.protection-level"] = "CriticalData";
        var summary = DockerGrpcService.BuildContainerSummary(container, Registry());
        Assert.True(summary.ProtectedResource);
        Assert.Equal("ControlPlane", summary.ProtectionLevel);
        container.Labels.Clear();
        Assert.True(DockerGrpcService.BuildContainerSummary(container, Registry()).ProtectedResource);
    }

    [Fact]
    public void SummaryProjectsAddressesPortsAndCreationWithoutInspectCalls()
    {
        var container = Container();
        container.Created = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        container.Ports = [new Port { IP = "127.0.0.1", PublicPort = 8088, PrivatePort = 80, Type = "tcp" },
            new Port { PrivatePort = 443, Type = "tcp" }];
        container.NetworkSettings = new SummaryNetworkSettings { Networks = new Dictionary<string, EndpointSettings>
        { ["app-net"] = new() { IPAddress = "172.18.0.2" } } };
        container.Labels["password"] = "must-not-be-returned";
        var summary = DockerGrpcService.BuildContainerSummary(container, Registry());
        Assert.False(summary.ProtectedResource);
        Assert.Contains("127.0.0.1:8088 → 80/tcp", summary.Ports);
        Assert.Contains("443/tcp", summary.Ports);
        Assert.Equal("172.18.0.2", Assert.Single(summary.Addresses));
        Assert.Equal("app-net", Assert.Single(summary.Networks));
        Assert.Equal(container.Created, summary.CreatedAtUtc.ToDateTime());
        Assert.DoesNotContain("password", summary.Labels.Keys);
    }

    private static ContainerListResponse Container() => new()
    {
        ID = "container-id", Names = ["/ordinary-app"], Image = "nginx:alpine", State = "running", Status = "Up 2 hours",
        Labels = new Dictionary<string, string>()
    };
}
