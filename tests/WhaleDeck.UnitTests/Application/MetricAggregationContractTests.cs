using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.UnitTests.Application;

public sealed class MetricAggregationContractTests
{
    [Fact]
    public void EmptyQualityWindowsHaveNullableStatisticsAndStableQueueIdentities()
    {
        using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=whaledeck_design;Username=whaledeck_design").Options);
        var model = db.Model.FindEntityType(typeof(MetricRollup))!;
        foreach (var name in new[] { nameof(MetricRollup.Average), nameof(MetricRollup.Minimum), nameof(MetricRollup.Maximum),
            nameof(MetricRollup.Sum), nameof(MetricRollup.First), nameof(MetricRollup.Last), nameof(MetricRollup.WeightSum), nameof(MetricRollup.WeightedSum) })
            Assert.True(model.FindProperty(name)!.IsNullable);
        Assert.Equal(nameof(MetricAggregationPending.WindowStartUtc),
            Assert.Single(db.Model.FindEntityType(typeof(MetricAggregationPending))!.FindPrimaryKey()!.Properties).Name);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void SampleDirtyQueueIsAtomicStatementLevelInvokerOnlyAndTracksOldAndNewTimestamps()
    {
        var sql = MetricAggregation.MigrationUp;
        Assert.Contains("FOR EACH STATEMENT", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR EACH ROW", sql, StringComparison.Ordinal);
        Assert.Contains("OLD TABLE AS old_samples NEW TABLE AS new_samples", sql, StringComparison.Ordinal);
        Assert.Contains("revision = public.metric_aggregation_pending.revision + 1", sql, StringComparison.Ordinal);
        Assert.Contains("SECURITY INVOKER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TRIGGER", MetricAggregation.MigrationDown, StringComparison.Ordinal);
    }
}
