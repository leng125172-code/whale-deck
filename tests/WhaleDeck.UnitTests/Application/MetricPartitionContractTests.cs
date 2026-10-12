using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.UnitTests.Application;

public sealed class MetricPartitionContractTests
{
    [Fact]
    public void EfModelKeepsTheTimeBrinIndexInItsMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=localhost;Database=whaledeck_design;Username=whaledeck_design").Options;
        using var db = new PlatformDbContext(options);
        var index = Assert.Single(db.Model.FindEntityType(typeof(MetricSample))!.GetIndexes(),
            item => item.GetDatabaseName() == "IX_metric_samples_sampled_at_utc_brin");
        Assert.Equal("brin", index.FindAnnotation("Npgsql:IndexMethod")!.Value);
        Assert.Equal(nameof(MetricSample.SampledAtUtc), Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void MigrationResourcesAreEmbeddedAndPreserveDataBeforeDroppingLegacyTables()
    {
        var sql = MetricPartitions.MigrationUp;
        Assert.Contains("PARTITION BY RANGE (sampled_at_utc)", sql, StringComparison.Ordinal);
        Assert.Contains("PARTITION BY RANGE (window_start_utc)", sql, StringComparison.Ordinal);
        Assert.Contains("EXCEPT ALL", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("Metric migration data verification failed", StringComparison.Ordinal) <
            sql.IndexOf("DROP TABLE public.wd_metric_samples_legacy", StringComparison.Ordinal));
        Assert.Contains("EXCEPT ALL", MetricPartitions.MigrationDown, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceIsFixedSchemaInvokerOnlyAndBoundedToUtcDailyRetention()
    {
        var sql = MetricPartitions.MigrationUp;
        Assert.Contains("SECURITY INVOKER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY DEFINER\n", sql, StringComparison.Ordinal);
        Assert.Contains("INTERVAL '14 days'", sql, StringComparison.Ordinal);
        Assert.Contains("today + 2", sql, StringComparison.Ordinal);
        Assert.Contains("pg_try_advisory_xact_lock", sql, StringComparison.Ordinal);
        Assert.Contains("child.bounds <> expected", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON FUNCTION", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE public.%I CASCADE", sql, StringComparison.Ordinal);
    }
}
