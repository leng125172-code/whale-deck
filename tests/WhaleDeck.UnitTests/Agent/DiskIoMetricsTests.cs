using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DiskIoMetricsTests
{
    [Fact]
    public void ParsesCompletedOperationsSectorsAndTimeFromKernelColumns()
    {
        Assert.True(DiskIoMetrics.TryParse("259 0 nvme0n1 100 0 200 300 400 0 500 600 0 700 800 0 0 0 0", out var device, out var counters));
        Assert.Equal("nvme0n1", device);
        Assert.Equal(new DiskIoCounters(100, 200, 300, 400, 500, 600), counters);
        Assert.False(DiskIoMetrics.TryParse("bad", out _, out _));
        Assert.False(DiskIoMetrics.TryParse("259 0 ../bad 100 0 200 300 400 0 500 600 0 700 800", out _, out _));
    }

    [Fact]
    public void DerivesRatesAndAverageCompletedIoLatencyWithoutSectorSizeOverflow()
    {
        var values = DiskIoMetrics.Calculate("disk", new(112, 224, 336, 406, 560, 660), new(100, 200, 300, 400, 500, 600), 6).ToDictionary(item => item.Kind);
        Assert.Equal(2, values["disk.read.iops"].Value);
        Assert.Equal(1, values["disk.write.iops"].Value);
        Assert.Equal(2048, values["disk.read"].Value);
        Assert.Equal(5120, values["disk.write"].Value);
        Assert.Equal(3, values["disk.read.latency"].Value);
        Assert.Equal(10, values["disk.write.latency"].Value);
        Assert.All(values.Values, item => Assert.Equal("Good", item.Quality));
    }

    [Fact]
    public void BaselineResetAndIdleAreNotMeasuredZeroLatency()
    {
        var current = new DiskIoCounters(10, 20, 30, 40, 50, 60);
        Assert.All(DiskIoMetrics.Calculate("disk", current, null, 6), item => Assert.Equal("Warmup", item.Quality));
        Assert.All(DiskIoMetrics.Calculate("disk", current, current with { ReadOperations = 11 }, 6), item => Assert.Equal("Warmup", item.Quality));
        Assert.All(DiskIoMetrics.Calculate("disk", current, current, double.NaN), item => Assert.Equal("Warmup", item.Quality));
        var idle = DiskIoMetrics.Calculate("disk", current, current, 6);
        Assert.All(idle.Where(item => item.Kind.EndsWith("latency", StringComparison.Ordinal)), item => Assert.Equal("Idle", item.Quality));
        Assert.All(idle.Where(item => !item.Kind.EndsWith("latency", StringComparison.Ordinal)), item => Assert.Equal("Good", item.Quality));
    }
}
