using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerTerminalPolicyTests
{
    private static readonly string Id = new('a', 64);
    private static ResourceRegistry Registry() => new(new ConfigurationBuilder().Build());
    private static Docker.DotNet.Models.ContainerInspectResponse Container() => new()
    {
        ID = Id, Name = "/ordinary-test", Image = "sha256:" + new string('b', 64),
        Config = new() { User = "1000", Labels = new Dictionary<string, string>(), Env = ["PRIVATE_VALUE=not-for-a-response"] },
        HostConfig = new(), State = new() { Running = true, Pid = 777, StartedAt = "2026-10-16T01:02:03.000000000Z" }
    };

    [Theory]
    [InlineData("ordinary-test")][InlineData("abc123")][InlineData("../target")][InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void AliasesShortIdsAndNoncanonicalIdsAreNotTargets(string id) =>
        Assert.Throws<ArgumentException>(() => ContainerTerminalPolicy.RequireId(id));

    [Fact]
    public void SnapshotBindsExactStartupAndNeverSerializesEnvironment()
    {
        var target = ContainerTerminalPolicy.Inspect(Container(), Registry());
        Assert.Equal(Id, target.ContainerId); Assert.Equal("777:2026-10-16T01:02:03.000000000Z", target.InstanceId);
        Assert.Equal("1000", target.ConfiguredUser);
        Assert.DoesNotContain("PRIVATE_VALUE", target.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryProtectionWorksWithoutDockerLabelAndLabelWorksWithoutNameMatch()
    {
        var container = Container(); container.Name = "/whaledeck-api";
        Assert.Throws<UnauthorizedAccessException>(() => ContainerTerminalPolicy.Inspect(container, Registry()));
        container.Name = "/ordinary-test"; container.Config.Labels["io.whaledeck.protected"] = "TRUE";
        Assert.Throws<UnauthorizedAccessException>(() => ContainerTerminalPolicy.Inspect(container, Registry()));
    }

    [Theory]
    [InlineData("privileged")][InlineData("capabilities")][InlineData("devices")][InlineData("device-rules")]
    [InlineData("pid")][InlineData("network")][InlineData("ipc")][InlineData("userns")][InlineData("cgroupns")]
    [InlineData("shared-pid")][InlineData("seccomp")]
    public void HostEscapeSurfacesAreRefused(string field)
    {
        var container = Container(); var config = container.HostConfig;
        switch (field)
        {
            case "privileged": config.Privileged = true; break;
            case "capabilities": config.CapAdd = ["SYS_ADMIN"]; break;
            case "devices": config.Devices = [new() { PathOnHost = "/dev/sda" }]; break;
            case "device-rules": config.DeviceCgroupRules = ["a *:* rwm"]; break;
            case "pid": config.PidMode = "host"; break;
            case "network": config.NetworkMode = "host"; break;
            case "ipc": config.IpcMode = "host"; break;
            case "userns": config.UsernsMode = "host"; break;
            case "cgroupns": config.CgroupnsMode = "host"; break;
            case "shared-pid": config.PidMode = "container:another"; break;
            case "seccomp": config.SecurityOpt = ["seccomp=unconfined"]; break;
        }
        Assert.Throws<UnauthorizedAccessException>(() => ContainerTerminalPolicy.Inspect(container, Registry()));
    }

    [Theory]
    [InlineData("paused")][InlineData("restarting")][InlineData("dead")][InlineData("stopped")]
    public void NoninteractiveContainerStatesRefused(string state)
    {
        var container = Container();
        container.State.Paused = state == "paused"; container.State.Restarting = state == "restarting";
        container.State.Dead = state == "dead"; container.State.Running = state != "stopped";
        Assert.Throws<InvalidOperationException>(() => ContainerTerminalPolicy.Inspect(container, Registry()));
    }

    [Theory]
    [InlineData("sh")][InlineData("bash")][InlineData("ash")]
    public void OnlyFixedShellNamesWithHostFrameValidationAreAccepted(string shell)
    {
        var frame = Open(shell);
        Assert.Same(frame.Payload, ContainerTerminalPolicy.Open(frame));
        frame.Payload.Context.ActorSubject = "";
        Assert.Throws<ArgumentException>(() => ContainerTerminalPolicy.Open(frame));
    }

    [Theory]
    [InlineData("/bin/sh")][InlineData("sh -c id")][InlineData("python")][InlineData("")]
    public void CallerCannotSupplyExecutableOrArguments(string shell) =>
        Assert.Throws<ArgumentException>(() => ContainerTerminalPolicy.Open(Open(shell)));

    [Fact]
    public void SubsequentFrameCannotSwitchTargetInstanceOrShell()
    {
        Assert.Throws<ArgumentException>(() => ContainerTerminalPolicy.Follow(Open("sh")));
        var frame = new ContainerTerminalFrame { Payload = new() { Kind = "close" } };
        Assert.Same(frame.Payload, ContainerTerminalPolicy.Follow(frame));
        frame.InstanceId = "another"; Assert.Throws<ArgumentException>(() => ContainerTerminalPolicy.Follow(frame));
    }

    private static ContainerTerminalFrame Open(string shell) => new()
    {
        ContainerId = Id, InstanceId = "777:2026-10-16T01:02:03Z", Shell = shell,
        Payload = new() { SessionId = Guid.NewGuid().ToString("N"), Kind = "open", Columns = 80, Rows = 24, Context = new() { ActorSubject = "admin" } }
    };
}
