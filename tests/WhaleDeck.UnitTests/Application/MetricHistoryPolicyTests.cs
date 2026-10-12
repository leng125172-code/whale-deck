using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class MetricHistoryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 21, 17, TimeSpan.Zero);

    [Theory]
    [InlineData("1h", 30, 121)]
    [InlineData("24h", 300, 289)]
    [InlineData("7d", 1800, 337)]
    public void WindowsAreBoundedAlignedAndIncludeTheCurrentPartialBucket(string window, int resolution, int count)
    {
        var plan = MetricHistoryPolicy.Create(["disk.read.latency", "disk.read.iops"], window, 600, Now);
        Assert.True(plan.Aggregate);
        Assert.Equal(resolution, plan.ResolutionSeconds);
        Assert.Equal(count, plan.PointLimit);
        Assert.Equal(0, plan.StartUtc.ToUnixTimeSeconds() % resolution);
        Assert.Equal(Now, plan.EndUtc);
        Assert.True(plan.StartUtc.AddSeconds((count - 1) * resolution) <= Now);
    }

    [Fact]
    public void LiveBoundsAndApprovedKindsRemainStable()
    {
        Assert.Equal(600, MetricHistoryPolicy.Create(["disk.read"], "live", 9999, Now).PointLimit);
        Assert.Equal(1, MetricHistoryPolicy.Create(["disk.read"], "live", 0, Now).PointLimit);
        Assert.Throws<ArgumentException>(() => MetricHistoryPolicy.Create([], "live", 60, Now));
        Assert.Throws<ArgumentException>(() => MetricHistoryPolicy.Create(["custom.sql"], "live", 60, Now));
        Assert.Throws<ArgumentException>(() => MetricHistoryPolicy.Create(["disk.read"], "14d", 60, Now));
    }
}
