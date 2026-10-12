using Docker.DotNet.Models;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerStabilityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T08:00:10Z", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-10-12T08:00:09Z", false)]
    [InlineData("2026-10-12T08:00:04Z", true)]
    [InlineData("2026-10-12T08:00:00.123456789Z", true)]
    [InlineData("invalid", false)]
    [InlineData("2026-10-12T08:01:00Z", false)]
    public void RunningMustHaveAStableStartIdentity(string startedAt, bool expected) =>
        Assert.Equal(expected, DockerEngine.IsStableContainerState(new ContainerState { Running = true, StartedAt = startedAt }, Now));

    [Theory]
    [InlineData("starting", false)]
    [InlineData("unhealthy", false)]
    [InlineData("healthy", true)]
    public void ExplicitHealthcheckMustPass(string health, bool expected) =>
        Assert.Equal(expected, DockerEngine.IsStableContainerState(new ContainerState
        { Running = true, StartedAt = "2026-10-12T08:00:00Z", Health = new Health { Status = health } }, Now));

    [Fact]
    public void RestartingOrPausedIsNeverStable()
    {
        Assert.False(DockerEngine.IsStableContainerState(null, Now));
        Assert.False(DockerEngine.IsStableContainerState(new ContainerState { Running = true, Restarting = true, StartedAt = "2026-10-12T08:00:00Z" }, Now));
        Assert.False(DockerEngine.IsStableContainerState(new ContainerState { Running = true, Paused = true, StartedAt = "2026-10-12T08:00:00Z" }, Now));
    }
}
