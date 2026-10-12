using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerMetricProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static ContainerUsageDto Sample => new("id", "running", Now, 120, 6, 800, 2000, 40, null, null, 0, 10, 1, "Good");

    [Fact]
    public void MissingCountersAreOmittedAndWarmupCpuCannotBecomeMeasuredZero()
    {
        var values = ContainerMetricProjection.Project(Sample with { CpuPercent = null, Quality = "Warmup" }, Now);
        Assert.DoesNotContain(values, item => item.Kind.StartsWith("container.network", StringComparison.Ordinal));
        var cpu = Assert.Single(values, item => item.Kind == "container.cpu.utilization");
        Assert.Equal("Warmup", cpu.Quality);
        Assert.Equal("Good", Assert.Single(values, item => item.Kind == "container.memory.used").Quality);
        Assert.All(values, item => Assert.Equal("id", item.DeviceId));
        Assert.NotNull(MetricHistoryPolicy.Create(values.Select(item => item.Kind).ToArray(), "24h", 60, Now));
    }

    [Fact]
    public void StoppedUnavailableOrStaleSamplesDoNotCreateMisleadingHistory()
    {
        Assert.Empty(ContainerMetricProjection.Project(Sample with { Quality = "Stopped" }, Now));
        Assert.Empty(ContainerMetricProjection.Project(Sample with { Quality = "Unavailable" }, Now));
        Assert.Empty(ContainerMetricProjection.Project(Sample with { SampledAtUtc = Now.AddSeconds(-19) }, Now));
        Assert.Empty(ContainerMetricProjection.Project(Sample with { SampledAtUtc = null }, Now));
        Assert.Equal(120, Assert.Single(ContainerMetricProjection.Project(Sample, Now), item => item.Kind == "container.cpu.utilization").Value);
    }
}
