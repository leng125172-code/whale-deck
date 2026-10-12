using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostMetricBatchTests
{
    [Fact]
    public void ReadersNeverAdvanceTheCanonicalSamplingBaseline()
    {
        var clock = new ManualClock();
        var reader = new HostReader(clock);
        var first = reader.SampleMetrics();
        clock.Advance(TimeSpan.FromSeconds(12));
        Assert.Same(first, reader.SampleMetrics());
        var next = reader.CollectMetrics();
        Assert.NotSame(first, next);
        Assert.Equal(TimeSpan.FromSeconds(12), next.SampledAtUtc - first.SampledAtUtc);
        Assert.Same(next, reader.SampleMetrics());
        Assert.Contains(next.Metrics, metric => metric.Kind == "cpu.utilization");
    }

    [Fact]
    public async Task ConcurrentRpcReadersShareOneImmutableBatchIdentity()
    {
        var reader = new HostReader();
        var first = reader.SampleMetrics();
        var reads = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(reader.SampleMetrics)));
        Assert.All(reads, batch => Assert.Same(first, batch));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan interval) => _ticks += interval.Ticks;
    }
}
