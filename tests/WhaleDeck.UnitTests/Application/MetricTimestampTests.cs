using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Application.Models;

namespace WhaleDeck.UnitTests.Application;

public sealed class MetricTimestampTests
{
    [Fact]
    public void ReplayKeepsEveryTimestampAndDeduplicatesOnlyTheSameCanonicalIdentity()
    {
        var resource = Guid.NewGuid();
        var start = DateTimeOffset.UnixEpoch.AddTicks(1234567);
        var points = Enumerable.Range(0, 120).Select(index => new ResourceMetric(resource,
            new MetricValueDto("network.receive", "eth0", index, "bytesPerSecond", index == 0 ? "Warmup" : "Good", start.AddSeconds(index * 6)))).ToArray();
        var result = MetricSampleWriter.Canonicalize(points.Concat(points).ToArray());
        Assert.Equal(120, result.Length);
        Assert.Equal(Enumerable.Range(0, 120).Select(index => (double)index), result.Select(item => item.Metric.Value));
        Assert.All(result, item => Assert.Equal(0, item.Metric.SampledAtUtc.UtcTicks % 10));
        Assert.Equal("Warmup", result[0].Metric.Quality);
    }

    [Fact]
    public void ConflictingValuesAtOneMicrosecondIdentityFailRatherThanSilentlyChoosingOne()
    {
        var item = new ResourceMetric(Guid.NewGuid(), new("disk.write", "nvme0n1", 1, "bytesPerSecond", "Good", DateTimeOffset.UnixEpoch));
        Assert.Throws<InvalidOperationException>(() => MetricSampleWriter.Canonicalize([item, item with { Metric = item.Metric with { Value = 2, SampledAtUtc = item.Metric.SampledAtUtc.AddTicks(1) } }]));
        Assert.Equal(2, MetricSampleWriter.Canonicalize([item, item with { ResourceId = Guid.NewGuid() }]).Length);
        Assert.Equal(2, MetricSampleWriter.Canonicalize([item, item with { Metric = item.Metric with { DeviceId = "nvme1n1" } }]).Length);
    }

    [Fact]
    public void CanonicalIdentityMatchesPostgresMicrosecondsAcrossRepeatedBatchesAndOffsets()
    {
        var timestamp = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567);
        var normalized = MetricSampleWriter.NormalizeTimestamp(timestamp);
        Assert.Equal(TimeSpan.Zero, normalized.Offset);
        Assert.Equal(0, normalized.UtcTicks % 10);
        Assert.Equal(timestamp.UtcTicks - 7, normalized.UtcTicks);
        Assert.Equal(normalized, MetricSampleWriter.NormalizeTimestamp(normalized));
        Assert.Equal(normalized, MetricSampleWriter.NormalizeTimestamp(timestamp.ToOffset(TimeSpan.FromHours(-5))));
        var identities = new HashSet<DateTimeOffset> { normalized };
        Assert.False(identities.Add(MetricSampleWriter.NormalizeTimestamp(timestamp)));
    }
}
