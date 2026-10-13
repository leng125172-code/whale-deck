using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeMetricAggregationTests
{
    [NativePostgresFact]
    public async Task UpgradeBackfillQualityWeightedHistoryLateSamplesAndRestartAreCorrectInRealPostgres()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_metrics_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native metric tests require their isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString, provider => provider.EnableRetryOnFailure()).Options;
        await using var db = new PlatformDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012212225_ApplicationBackupConsistency");
        var now = DateTimeOffset.UtcNow;
        var hour = MetricHistoryPolicy.AlignUtc(now, 3600).AddHours(-5);
        var oldHour = MetricHistoryPolicy.AlignUtc(now.AddDays(-14), 3600).AddHours(1);
        var partialHour = MetricHistoryPolicy.AlignUtc(now, 3600);
        var resource = Guid.NewGuid();
        MetricSeries Series(string kind, string device = "sda") => new() { ResourceId = resource, MetricKind = kind,
            Unit = "native", DimensionsJson = "{\"DeviceId\":\"" + device + "\"}", LastSeenAtUtc = now };
        var gauge = Series("cpu.utilization", "cpu");
        var counter = Series("network.receive.total", "eno1");
        var latency = Series("disk.read.latency");
        var iops = Series("disk.read.iops");
        var idle = Series("disk.write.latency");
        var warmup = Series("memory.utilization", "memory");
        var unavailable = Series("container.pids.count", "fixture");
        db.MetricSeries.AddRange(gauge, counter, latency, iops, idle, warmup, unavailable);
        void Sample(MetricSeries series, DateTimeOffset at, double? value, string quality = "Good") =>
            db.MetricSamples.Add(new MetricSample { SeriesId = series.Id, SampledAtUtc = at, ValueDouble = value, Quality = quality });
        Sample(gauge, hour.AddSeconds(1), 2); Sample(gauge, hour.AddSeconds(61), 8); Sample(gauge, hour.AddSeconds(121), 20);
        Sample(gauge, oldHour.AddSeconds(1), 12);
        Sample(counter, hour.AddSeconds(1), 100); Sample(counter, hour.AddSeconds(61), 300);
        Sample(latency, hour.AddSeconds(1), 10); Sample(iops, hour.AddSeconds(1), 1);
        Sample(latency, hour.AddSeconds(61), 100); Sample(iops, hour.AddSeconds(61), 9);
        Sample(idle, hour.AddSeconds(1), null, "Idle"); Sample(warmup, hour.AddSeconds(1), 0, "Warmup");
        Sample(unavailable, hour.AddSeconds(1), null, "Unavailable");
        await db.SaveChangesAsync();
        var rawBefore = await db.MetricSamples.CountAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metric_rollups(series_id,window_start_utc,resolution,minimum,maximum,average,sum,sample_count)
            VALUES({gauge.Id},{hour},'1m',2,2,2,2,1)
            """);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(rawBefore, await db.MetricSamples.CountAsync());
        var legacy = await db.MetricRollups.AsNoTracking().SingleAsync();
        Assert.Equal(2, legacy.Average); Assert.Null(legacy.First); Assert.Equal(0, legacy.ObservedSampleCount);
        // Old retained data has no dirty trigger receipt: the durable cursor must backfill it after restart.
        Assert.Empty(await db.MetricAggregationPending.AsNoTracking().ToArrayAsync());
        await MetricAggregation.ApplyAsync(db, now, default);
        await using (var restarted = new PlatformDbContext(options))
            await MetricAggregation.ApplyAsync(restarted, now, default);
        Assert.Equal(12, (await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == gauge.Id && item.WindowStartUtc == oldHour && item.Resolution == "1h")).Last);
        // More than two-hour-old arrivals, mixed quality, and invalid nonfinite values enqueue atomically.
        Sample(gauge, hour.AddSeconds(181), 30); Sample(gauge, hour.AddSeconds(182), double.NaN);
        Sample(gauge, hour.AddSeconds(183), double.PositiveInfinity);
        Sample(gauge, hour.AddSeconds(184), null);
        var currentMinute = MetricHistoryPolicy.AlignUtc(now, 60);
        if (currentMinute > partialHour) Sample(gauge, partialHour.AddSeconds(1), 40);
        Sample(gauge, currentMinute.AddTicks(10), 50);
        Sample(gauge, currentMinute.AddTicks(1000), double.NaN);
        await db.SaveChangesAsync();
        Assert.NotEmpty(await db.MetricAggregationPending.AsNoTracking().ToArrayAsync());
        Assert.InRange(await MetricAggregation.ApplyAsync(db, now, default), 1, 3);
        var aggregate = await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == gauge.Id && item.WindowStartUtc == hour && item.Resolution == "1h");
        Assert.Equal(2, aggregate.First); Assert.Equal(30, aggregate.Last);
        Assert.Equal(hour.AddSeconds(1), aggregate.FirstSampledAtUtc); Assert.Equal(hour.AddSeconds(181), aggregate.LastSampledAtUtc);
        Assert.Equal(4, aggregate.SampleCount); Assert.Equal(7, aggregate.ObservedSampleCount);
        Assert.Equal(15, aggregate.Average); Assert.Equal(60, aggregate.Sum); Assert.Equal(2, aggregate.Minimum); Assert.Equal(30, aggregate.Maximum);
        var weighted = await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == latency.Id && item.WindowStartUtc == hour && item.Resolution == "1h");
        Assert.Equal(91, weighted.Average); Assert.Equal(910, weighted.WeightedSum); Assert.Equal(10, weighted.WeightSum);
        var noIo = await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == idle.Id && item.WindowStartUtc == hour && item.Resolution == "1h");
        Assert.Null(noIo.Average); Assert.Null(noIo.First); Assert.Equal(0, noIo.SampleCount); Assert.Equal(1, noIo.IdleCount);
        Assert.False(await db.MetricRollups.AnyAsync(item => item.Resolution == "1h" && item.WindowStartUtc == partialHour));
        Assert.True(await db.MetricAggregationPending.AnyAsync(item => item.WindowStartUtc == currentMinute));
        var history = new MetricsQuery(db);
        var live = await history.GetHistoryAsync([gauge.MetricKind], 60, default);
        Assert.Contains(live.Single().Points, item => item.Value is null && item.Quality == "Unavailable");
        var kinds = new[] { gauge.MetricKind, counter.MetricKind, latency.MetricKind, idle.MetricKind, warmup.MetricKind, unavailable.MetricKind };
        var sevenDays = await history.GetHistoryAsync(kinds, 60, default, "7d");
        Assert.All(sevenDays, item => { Assert.Equal(3600, item.ResolutionSeconds); Assert.Equal(169, item.Points.Count); });
        Assert.Equal(15, sevenDays.Single(item => item.Kind == gauge.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Value);
        Assert.Equal(300, sevenDays.Single(item => item.Kind == counter.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Value);
        Assert.Equal(91, sevenDays.Single(item => item.Kind == latency.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Value);
        Assert.Equal(10, sevenDays.Single(item => item.Kind == latency.MetricKind).Points.Single(item => item.SampledAtUtc == hour).AggregationWeight);
        Assert.Equal("Idle", sevenDays.Single(item => item.Kind == idle.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Quality);
        Assert.Equal("Warmup", sevenDays.Single(item => item.Kind == warmup.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Quality);
        Assert.Equal("Unavailable", sevenDays.Single(item => item.Kind == unavailable.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Quality);
        // Explicit fixture alteration of derived values proves completed queries read persisted rollups, not raw data.
        await db.MetricRollups.Where(item => item.SeriesId == gauge.Id && item.Resolution == "1h" && item.WindowStartUtc == hour)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WeightedSum, 400.0).SetProperty(item => item.WeightSum, 4.0));
        Assert.Equal(100, (await history.GetHistoryAsync([gauge.MetricKind], 60, default, "7d")).Single().Points.Single(item => item.SampledAtUtc == hour).Value);
        Sample(gauge, hour.AddSeconds(241), 40); await db.SaveChangesAsync();
        // Dirty completed buckets must use raw until acknowledged, never expose stale cached aggregates.
        Assert.Equal(20, (await history.GetHistoryAsync([gauge.MetricKind], 60, default, "7d")).Single().Points.Single(item => item.SampledAtUtc == hour).Value);
        await MetricAggregation.ApplyAsync(db, now, default);
        Assert.Equal(20, (await history.GetHistoryAsync([gauge.MetricKind], 60, default, "7d")).Single().Points.Single(item => item.SampledAtUtc == hour).Value);
        var day = await history.GetHistoryAsync([latency.MetricKind, counter.MetricKind], 60, default, "24h");
        Assert.Equal(91, day.Single(item => item.Kind == latency.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Value);
        Assert.Equal(300, day.Single(item => item.Kind == counter.MetricKind).Points.Single(item => item.SampledAtUtc == hour).Value);
        // Quality updates invalidate the old hour and do not freeze the original Good value.
        await db.MetricSamples.Where(item => item.SeriesId == gauge.Id && item.SampledAtUtc == hour.AddSeconds(241))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quality, "Unavailable"));
        await MetricAggregation.ApplyAsync(db, now, default);
        Assert.Equal(15, (await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == gauge.Id && item.Resolution == "1h" && item.WindowStartUtc == hour)).Average);
        // Insert on a second connection precisely between raw rebuild and revision acknowledgement.
        // This is a real concurrent database commit, not an in-memory substitute for MVCC behavior.
        Sample(gauge, hour.AddSeconds(361), 50); await db.SaveChangesAsync();
        var interceptor = new RacingArrival(connection.ConnectionString, gauge.Id, hour.AddSeconds(367));
        var racingOptions = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString,
            provider => provider.EnableRetryOnFailure()).AddInterceptors(interceptor).Options;
        await using (var racingDb = new PlatformDbContext(racingOptions))
            await MetricAggregation.ApplyAsync(racingDb, now, default);
        Assert.True(interceptor.Inserted);
        Assert.True(await db.MetricAggregationPending.AnyAsync(item => item.WindowStartUtc == hour.AddMinutes(6)));
        Assert.Equal(50, (await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == gauge.Id && item.Resolution == "1h" && item.WindowStartUtc == hour)).Last);
        await MetricAggregation.ApplyAsync(db, now, default);
        Assert.Equal(60, (await db.MetricRollups.AsNoTracking().SingleAsync(item => item.SeriesId == gauge.Id && item.Resolution == "1h" && item.WindowStartUtc == hour)).Last);
        await using (var lockConnection = new NpgsqlConnection(connection.ConnectionString))
        {
            await lockConnection.OpenAsync();
            await using var held = await lockConnection.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(82592831620705)", lockConnection, held);
            await hold.ExecuteNonQueryAsync();
            Assert.Equal(0, await MetricAggregation.ApplyAsync(db, now, default));
            await held.RollbackAsync();
        }
        var rawAfter = await db.MetricSamples.CountAsync();
        // Down/up affects derived data only; raw sample identities survive and pending triggers are reinstalled.
        await migrator.MigrateAsync("20261012212225_ApplicationBackupConsistency");
        Assert.Equal(rawAfter, await db.MetricSamples.CountAsync());
        await migrator.MigrateAsync();
        Assert.Equal(rawAfter, await db.MetricSamples.CountAsync());
        Sample(counter, hour.AddSeconds(301), 400); await db.SaveChangesAsync();
        Assert.NotEmpty(await db.MetricAggregationPending.AsNoTracking().ToArrayAsync());
    }

    private sealed class RacingArrival(string connection, Guid series, DateTimeOffset timestamp) : DbCommandInterceptor
    {
        public bool Inserted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Inserted && command.CommandText.Contains("DELETE FROM metric_aggregation_pending p USING", StringComparison.Ordinal))
            {
                await using var other = new NpgsqlConnection(connection);
                await other.OpenAsync(cancellationToken);
                await using var arrival = new NpgsqlCommand("INSERT INTO metric_samples(series_id,sampled_at_utc,value_double,quality) VALUES(@series,@timestamp,60,'Good')", other);
                arrival.Parameters.AddWithValue("series", series); arrival.Parameters.AddWithValue("timestamp", timestamp);
                await arrival.ExecuteNonQueryAsync(cancellationToken);
                Inserted = true;
            }
            return result;
        }
    }
}
