using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Resumable bounded UTC aggregation. Dirty revisions are acknowledged only after the same committed rebuild.</summary>
public static class MetricAggregation
{
    public const string StateKey = "retained-history-v1";
    public static string MigrationUp => ReadResource("Up");
    public static string MigrationDown => ReadResource("Down");

    public static Task<int> ApplyAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => ApplyPassAsync(db, now, cancellationToken));

    private static async Task<int> ApplyPassAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var end = MetricHistoryPolicy.AlignUtc(now, 60);
        var cutoff = now.ToUniversalTime().AddDays(-14);
        var firstHour = MetricHistoryPolicy.AlignUtc(cutoff, 3600);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(82592831620705) AS \"Value\"").SingleAsync(cancellationToken)) return 0;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'; SET LOCAL statement_timeout = '15s'; SET LOCAL TimeZone = 'UTC';", cancellationToken);
        await db.MetricAggregationPending.Where(item => item.WindowStartUtc < firstHour).ExecuteDeleteAsync(cancellationToken);
        await db.MetricAggregationWindows.Where(item => item.WindowStartUtc < firstHour).ExecuteDeleteAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metric_aggregation_state(key, next_hour_utc) VALUES ({StateKey}, {firstHour}) ON CONFLICT(key) DO NOTHING
            """, cancellationToken);
        var state = await db.MetricAggregationStates.AsNoTracking().SingleAsync(item => item.Key == StateKey, cancellationToken);
        var cursor = state.NextHourUtc < firstHour ? firstHour : state.NextHourUtc;
        // At most two dirty hours plus one backfill hour. No full-retention scan on each six-second cycle.
        var dirtyHours = await db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT DISTINCT date_bin(INTERVAL '1 hour', window_start_utc, TIMESTAMPTZ '1970-01-01') AS "Value"
            FROM metric_aggregation_pending WHERE window_start_utc >= {firstHour} AND window_start_utc < {end}
            ORDER BY "Value" LIMIT 2
            """).ToArrayAsync(cancellationToken);
        var backfillEnd = MetricHistoryPolicy.AlignUtc(end, 3600);
        var hours = dirtyHours.Concat(cursor < backfillEnd ? [cursor] : Array.Empty<DateTimeOffset>()).Distinct().Order().ToArray();
        if (hours.Length == 0) { await transaction.CommitAsync(cancellationToken); return 0; }
        var pending = await db.MetricAggregationPending.FromSqlInterpolated($"""
            SELECT * FROM metric_aggregation_pending
            WHERE window_start_utc < {end} AND date_bin(INTERVAL '1 hour', window_start_utc, TIMESTAMPTZ '1970-01-01') = ANY({hours})
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        // Snapshot revisions BEFORE reading raw data: racing inserts remain dirty, even when their raw data is visible.
        await RebuildAsync(db, hours, cutoff, end, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metric_aggregation_windows(window_start_utc, completed_through_utc)
            SELECT start, LEAST(start + INTERVAL '1 hour', {end}) FROM unnest({hours}) AS start
            ON CONFLICT(window_start_utc) DO UPDATE SET completed_through_utc = EXCLUDED.completed_through_utc
            """, cancellationToken);
        if (pending.Length > 0)
        {
            var minutes = pending.Select(item => item.WindowStartUtc).ToArray();
            var revisions = pending.Select(item => item.Revision).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM metric_aggregation_pending p USING unnest({minutes}, {revisions}) AS observed(start, revision)
                WHERE p.window_start_utc = observed.start AND p.revision = observed.revision
                """, cancellationToken);
        }
        await db.MetricAggregationStates.Where(item => item.Key == StateKey).ExecuteUpdateAsync(
            setters => setters.SetProperty(item => item.NextHourUtc, cursor < backfillEnd ? cursor.AddHours(1) : cursor), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return hours.Length;
    }

    private static async Task RebuildAsync(PlatformDbContext db, DateTimeOffset[] hours, DateTimeOffset cutoff, DateTimeOffset end, CancellationToken cancellationToken)
    {
        // Only replace selected windows, within this transaction. Readers see either the old or the committed new set.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM metric_rollups r USING unnest({hours}) AS h(start)
            WHERE r.resolution IN ('1m', '1h') AND r.window_start_utc >= h.start
                AND r.window_start_utc < LEAST(h.start + INTERVAL '1 hour', {end})
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH samples AS MATERIALIZED (
                SELECT sample.*, source.metric_kind,
                    date_bin(INTERVAL '1 minute', sample.sampled_at_utc, TIMESTAMPTZ '1970-01-01') AS minute,
                    h.start AS hour,
                    sample.quality = 'Good' AND sample.value_double > '-Infinity'::float8 AND sample.value_double < 'Infinity'::float8
                        AND (source.metric_kind NOT IN ('disk.read.latency', 'disk.write.latency')
                            OR (weight.quality = 'Good' AND weight.value_double > 0 AND weight.value_double < 'Infinity'::float8)) AS valid,
                    CASE WHEN source.metric_kind IN ('disk.read.latency', 'disk.write.latency') THEN weight.value_double ELSE 1.0 END AS weight
                FROM unnest({hours}) AS h(start)
                JOIN metric_samples sample ON sample.sampled_at_utc >= GREATEST(h.start, {cutoff})
                    AND sample.sampled_at_utc < LEAST(h.start + INTERVAL '1 hour', {end})
                JOIN metric_series source ON source.id = sample.series_id
                LEFT JOIN metric_series weights ON weights.resource_id = source.resource_id AND weights.dimensions_json = source.dimensions_json
                    AND weights.metric_kind = CASE source.metric_kind WHEN 'disk.read.latency' THEN 'disk.read.iops' WHEN 'disk.write.latency' THEN 'disk.write.iops' END
                LEFT JOIN metric_samples weight ON weight.series_id = weights.id AND weight.sampled_at_utc = sample.sampled_at_utc
            )
            INSERT INTO metric_rollups(series_id, window_start_utc, resolution, minimum, maximum, average, sum, sample_count,
                observed_sample_count, idle_count, warmup_count, unavailable_count, first, last, first_sampled_at_utc, last_sampled_at_utc, weighted_sum, weight_sum)
            SELECT series_id, CASE WHEN GROUPING(minute) = 0 THEN minute ELSE hour END,
                CASE WHEN GROUPING(minute) = 0 THEN '1m' ELSE '1h' END,
                MIN(value_double) FILTER(WHERE valid), MAX(value_double) FILTER(WHERE valid),
                SUM(value_double * weight) FILTER(WHERE valid) / NULLIF(SUM(weight) FILTER(WHERE valid), 0),
                SUM(value_double) FILTER(WHERE valid), COUNT(*) FILTER(WHERE valid), COUNT(*),
                COUNT(*) FILTER(WHERE quality = 'Idle'), COUNT(*) FILTER(WHERE quality = 'Warmup'),
                COUNT(*) FILTER(WHERE quality IN ('Unavailable', 'Stopped') OR (quality = 'Good' AND NOT COALESCE(valid, false))),
                (ARRAY_AGG(value_double ORDER BY sampled_at_utc) FILTER(WHERE valid))[1],
                (ARRAY_AGG(value_double ORDER BY sampled_at_utc DESC) FILTER(WHERE valid))[1],
                MIN(sampled_at_utc) FILTER(WHERE valid), MAX(sampled_at_utc) FILTER(WHERE valid),
                SUM(value_double * weight) FILTER(WHERE valid), SUM(weight) FILTER(WHERE valid)
            FROM samples GROUP BY GROUPING SETS ((series_id, minute), (series_id, hour))
            HAVING (GROUPING(minute) = 0 AND minute >= {cutoff}) OR (GROUPING(minute) = 1 AND hour >= {cutoff} AND hour + INTERVAL '1 hour' <= {end})
            """, cancellationToken);
    }

    private static string ReadResource(string direction)
    {
        using var stream = typeof(MetricAggregation).Assembly.GetManifestResourceStream($"WhaleDeck.Infrastructure.Persistence.Sql.MetricAggregation.{direction}.sql")
            ?? throw new InvalidOperationException("The metric aggregation migration resource is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
