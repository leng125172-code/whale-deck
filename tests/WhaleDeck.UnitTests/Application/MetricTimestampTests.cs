using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.UnitTests.Application;

public sealed class MetricTimestampTests
{
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
