using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Diagnostics;
using Npgsql;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Application.Models;
using WhaleDeck.Worker;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeMetricAggregationTests
{
    private static readonly string[] ReplayKinds = ["network.receive", "disk.write"];
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
        if (Environment.GetEnvironmentVariable("WHALEDECK_METRIC_LOAD_VALIDATION") == "1")
        {
            // A cold/backfilling seven-day query must work before any rollup receipt exists.
            // Synthetic rows live only in this disposable database, never production metric tables.
            var loadKinds = new[] { "network.receive", "network.send", "disk.read", "disk.write", "disk.read.iops", "disk.write.iops", "disk.read.latency", "disk.write.latency" };
            var loadResource = Guid.NewGuid();
            foreach (var kind in loadKinds)
                for (var device = 0; device < 4; device++)
                    db.MetricSeries.Add(new MetricSeries { ResourceId = loadResource, MetricKind = kind, Unit = "load-fixture",
                        DimensionsJson = "{\"DeviceId\":\"" + (kind.StartsWith("network.", StringComparison.Ordinal) ? "enobench" : "sd") + (char)('a' + device) + "\"}", LastSeenAtUtc = now });
            await db.SaveChangesAsync();
            var loadStart = MetricHistoryPolicy.AlignUtc(now, 3600).AddDays(-6);
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3)); // Fixture creation only, not the API/query budget.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metric_samples(series_id,sampled_at_utc,value_double,quality)
                SELECT series.id, stamp, 1.0 + (EXTRACT(EPOCH FROM stamp)::bigint % 100), 'Good'
                FROM metric_series series CROSS JOIN generate_series({loadStart}, {loadStart.AddDays(6).AddSeconds(-6)}, INTERVAL '6 seconds') stamp
                WHERE series.resource_id = {loadResource}
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE metric_samples; ANALYZE metric_series;");
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
            Assert.Equal(2_764_800, await db.MetricSamples.Join(db.MetricSeries.Where(item => item.ResourceId == loadResource), item => item.SeriesId, item => item.Id, (sample, _) => sample).CountAsync());
            var timer = Stopwatch.StartNew();
            var loaded = await history.GetHistoryAsync(loadKinds, 60, default, "7d");
            timer.Stop();
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(25), "Cold seven-day history exceeded its bounded query budget.");
            Assert.True(loaded.Count >= 32); Assert.All(loaded, item => Assert.Equal(169, item.Points.Count));
            Console.WriteLine($"METRIC_QUERY_BENCH_MS={timer.ElapsedMilliseconds}");
        }
        await VerifyCanonicalReplayAsync(options, connection.ConnectionString);
    }

    private static async Task VerifyCanonicalReplayAsync(DbContextOptions<PlatformDbContext> options, string connection)
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-5).AddTicks(7);
        var samples = Enumerable.Range(0, 40).SelectMany(index => ReplayKinds.Select(kind =>
            new MetricValueDto(kind, "replay-fixture", index, "bytesPerSecond", index == 0 ? "Warmup" : "Good", start.AddSeconds(index * 6)))).ToArray();
        await using (var first = new PlatformDbContext(options))
        {
            var acknowledged = await HostMetricIngestionWorker.PersistAsync(first, samples, null, DateTimeOffset.UtcNow, default);
            Assert.Equal(samples[^1].SampledAtUtc, acknowledged); // Raw 100-ns cursor, not the database-normalized timestamp.
        }
        await using (var restarted = new PlatformDbContext(options))
            Assert.Equal(samples[^1].SampledAtUtc, await HostMetricIngestionWorker.PersistAsync(restarted, samples, null, DateTimeOffset.UtcNow, default));
        await using var inspect = new PlatformDbContext(options);
        var host = await inspect.ManagedResources.AsNoTracking().SingleAsync(item => item.ResourceType == "Host" && item.ExternalId == "local");
        var series = await inspect.MetricSeries.AsNoTracking().Where(item => item.ResourceId == host.Id).ToArrayAsync();
        Assert.Equal(2, series.Length);
        foreach (var item in series)
        {
            var stored = await inspect.MetricSamples.AsNoTracking().Where(sample => sample.SeriesId == item.Id).OrderBy(sample => sample.SampledAtUtc).ToArrayAsync();
            Assert.Equal(40, stored.Length);
            Assert.Equal(Enumerable.Range(0, 40).Select(i => MetricSampleWriter.NormalizeTimestamp(start.AddSeconds(i * 6))), stored.Select(sample => sample.SampledAtUtc));
            Assert.Equal(Enumerable.Range(0, 40).Select(i => (double?)i), stored.Select(sample => sample.ValueDouble));
            Assert.Equal("Warmup", stored[0].Quality);
            Assert.All(stored.Skip(1), sample => Assert.Equal("Good", sample.Quality));
        }
        var next = samples[^1] with { SampledAtUtc = samples[^1].SampledAtUtc.AddSeconds(6), Value = 99 };
        var failingOptions = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection, provider => provider.EnableRetryOnFailure())
            .AddInterceptors(new RejectSave()).Options;
        await using (var failed = new PlatformDbContext(failingOptions))
            await Assert.ThrowsAsync<InvalidOperationException>(() => HostMetricIngestionWorker.PersistAsync(failed, [next], samples[^1].SampledAtUtc, DateTimeOffset.UtcNow, default));
        Assert.Equal(80, await inspect.MetricSamples.Join(inspect.MetricSeries.Where(item => item.ResourceId == host.Id), sample => sample.SeriesId, item => item.Id, (sample, _) => sample).CountAsync());
        // A concurrent ingestion process cannot advance its cursor while another owns the write transaction.
        await using (var held = new NpgsqlConnection(connection))
        {
            await held.OpenAsync();
            await using var transaction = await held.BeginTransactionAsync();
            await using var acquire = new NpgsqlCommand("SELECT pg_advisory_xact_lock(82592831620706)", held, transaction);
            await acquire.ExecuteNonQueryAsync();
            await using var blocked = new PlatformDbContext(options);
            Assert.Equal(samples[^1].SampledAtUtc, await HostMetricIngestionWorker.PersistAsync(blocked, [next], samples[^1].SampledAtUtc, DateTimeOffset.UtcNow, default));
            await transaction.RollbackAsync();
        }
        await using (var retry = new PlatformDbContext(options))
            Assert.Equal(next.SampledAtUtc, await HostMetricIngestionWorker.PersistAsync(retry, [next], samples[^1].SampledAtUtc, DateTimeOffset.UtcNow, default));
        Assert.Equal(81, await inspect.MetricSamples.Join(inspect.MetricSeries.Where(item => item.ResourceId == host.Id), sample => sample.SeriesId, item => item.Id, (sample, _) => sample).CountAsync());
        Console.WriteLine("METRIC_CANONICAL_REPLAY=80-preserved-restart-idempotent-failure-and-lock-retry-passed");
    }

    private sealed class RejectSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SYNTHETIC_METRIC_SAVE_REJECTION");
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
