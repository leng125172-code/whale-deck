using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class MetricsQuery(PlatformDbContext db) : IMetricsQuery
{
    public async Task<IReadOnlyCollection<MetricSeriesSnapshotDto>> GetHistoryAsync(
        IReadOnlyCollection<string> kinds,
        int take,
        CancellationToken cancellationToken, string window = "live")
    {
        var plan = MetricHistoryPolicy.Create(kinds, window, take, DateTimeOffset.UtcNow);
        // Filter legacy virtual/partition views before applying the bounded series budget.
        var query = db.MetricSeries.FromSqlInterpolated($"""
            SELECT * FROM metric_series
            WHERE metric_kind = ANY({plan.Kinds}) AND last_seen_at_utc >= {plan.StartUtc}
            AND (metric_kind NOT LIKE 'network.%' OR COALESCE(dimensions_json->>'DeviceId', '') !~ '^(lo$|veth|docker|br-|virbr|cni|flannel|tun|tap)')
            AND (metric_kind NOT LIKE 'disk.%' OR metric_kind = 'disk.utilization' OR COALESCE(dimensions_json->>'DeviceId', '') !~ '^(loop|ram|dm-|md[0-9]+$|sd[a-z]+[0-9]+$|.*p[0-9]+$)')
            """).AsNoTracking().OrderByDescending(item => item.LastSeenAtUtc).ThenBy(item => item.MetricKind)
            .ThenBy(item => item.DimensionsJson).Take(256);
        if (!plan.Aggregate)
        {
            var live = await query.Select(item => new
            {
                item.MetricKind, item.DimensionsJson, item.Unit,
                Points = db.MetricSamples.AsNoTracking().Where(value => value.SeriesId == item.Id &&
                        value.SampledAtUtc >= plan.StartUtc && value.SampledAtUtc <= plan.EndUtc)
                    .OrderByDescending(value => value.SampledAtUtc).Take(plan.PointLimit).OrderBy(value => value.SampledAtUtc)
                    .Select(value => new MetricPointDto(value.SampledAtUtc, value.Quality == "Good" ? value.ValueDouble : null, value.Quality)).ToArray()
            }).ToArrayAsync(cancellationToken);
            return live.Select(item => new MetricSeriesSnapshotDto(item.MetricKind, DeviceId(item.DimensionsJson), item.Unit,
                item.Points.Select(SanitizeLivePoint).ToArray(), plan.StartUtc, plan.EndUtc, 6)).ToArray();
        }

        var series = await query.ToArrayAsync(cancellationToken);
        if (series.Length == 0) return [];
        var ids = series.Select(item => item.Id).ToArray();
        var lastBucket = MetricHistoryPolicy.AlignUtc(plan.EndUtc, plan.ResolutionSeconds);
        var persistedResolution = plan.ResolutionSeconds == 3600 ? "1h" : "1m";
        // Trust only completed receipts without dirty minutes. Missing/backfilling/current buckets use bounded raw ranges.
        var buckets = await db.Database.SqlQuery<MetricBucketRow>($"""
            WITH wanted AS MATERIALIZED (
                SELECT generate_series({plan.StartUtc}, {lastBucket}, make_interval(secs => {plan.ResolutionSeconds})) AS start
            ), eligible AS MATERIALIZED (
                SELECT w.start FROM wanted w JOIN metric_aggregation_windows receipt
                    ON receipt.window_start_utc = date_bin(INTERVAL '1 hour', w.start, TIMESTAMPTZ '1970-01-01')
                WHERE {plan.ResolutionSeconds} >= 60 AND receipt.completed_through_utc >= w.start + make_interval(secs => {plan.ResolutionSeconds})
                    AND w.start + make_interval(secs => {plan.ResolutionSeconds}) <= {plan.EndUtc}
                    AND NOT EXISTS(SELECT 1 FROM metric_aggregation_pending p WHERE p.window_start_utc >= w.start
                        AND p.window_start_utc < w.start + make_interval(secs => {plan.ResolutionSeconds}))
            ), raw AS MATERIALIZED (
                SELECT sample.*, source.metric_kind, w.start,
                    sample.quality = 'Good' AND sample.value_double > '-Infinity'::float8 AND sample.value_double < 'Infinity'::float8
                        AND (source.metric_kind NOT IN ('disk.read.latency', 'disk.write.latency')
                            OR (weight.quality = 'Good' AND weight.value_double > 0 AND weight.value_double < 'Infinity'::float8)) AS valid,
                    CASE WHEN source.metric_kind IN ('disk.read.latency', 'disk.write.latency') THEN weight.value_double ELSE 1.0 END AS weight
                FROM wanted w JOIN metric_samples sample ON sample.sampled_at_utc >= w.start
                    AND sample.sampled_at_utc < w.start + make_interval(secs => {plan.ResolutionSeconds}) AND sample.sampled_at_utc <= {plan.EndUtc}
                JOIN metric_series source ON source.id = sample.series_id
                LEFT JOIN metric_series weights ON weights.resource_id = source.resource_id AND weights.dimensions_json = source.dimensions_json
                    AND weights.metric_kind = CASE source.metric_kind WHEN 'disk.read.latency' THEN 'disk.read.iops' WHEN 'disk.write.latency' THEN 'disk.write.iops' END
                LEFT JOIN metric_samples weight ON weight.series_id = weights.id AND weight.sampled_at_utc = sample.sampled_at_utc
                WHERE sample.series_id = ANY({ids}) AND NOT EXISTS(SELECT 1 FROM eligible e WHERE e.start = w.start)
            ), combined AS (
                SELECT r.series_id AS "SeriesId", e.start AS "SampledAtUtc",
                    CASE WHEN source.metric_kind LIKE '%.total' THEN
                        (ARRAY_AGG(r.last ORDER BY r.last_sampled_at_utc DESC) FILTER(WHERE r.last IS NOT NULL))[1]
                    ELSE SUM(r.weighted_sum) / NULLIF(SUM(r.weight_sum), 0) END AS "Value",
                    SUM(r.idle_count)::bigint AS "IdleCount", SUM(r.warmup_count)::bigint AS "WarmupCount", SUM(r.unavailable_count)::bigint AS "UnavailableCount"
                FROM eligible e JOIN metric_rollups r ON r.window_start_utc >= e.start
                    AND r.window_start_utc < e.start + make_interval(secs => {plan.ResolutionSeconds}) AND r.resolution = {persistedResolution}
                JOIN metric_series source ON source.id = r.series_id
                WHERE r.series_id = ANY({ids}) AND r.observed_sample_count > 0
                GROUP BY r.series_id, source.metric_kind, e.start
                UNION ALL
                SELECT series_id, start,
                    CASE WHEN metric_kind LIKE '%.total' THEN
                        (ARRAY_AGG(value_double ORDER BY sampled_at_utc DESC) FILTER(WHERE valid))[1]
                    ELSE SUM(value_double * weight) FILTER(WHERE valid) / NULLIF(SUM(weight) FILTER(WHERE valid), 0) END,
                    COUNT(*) FILTER(WHERE quality = 'Idle'), COUNT(*) FILTER(WHERE quality = 'Warmup'),
                    COUNT(*) FILTER(WHERE quality IN ('Unavailable', 'Stopped') OR (quality = 'Good' AND NOT COALESCE(valid, false)))
                FROM raw GROUP BY series_id, metric_kind, start
            ) SELECT * FROM combined
            ORDER BY "SampledAtUtc"
            """).ToArrayAsync(cancellationToken);
        var indexed = buckets.ToDictionary(item => (item.SeriesId, item.SampledAtUtc));
        return series.Select(item => new MetricSeriesSnapshotDto(item.MetricKind, DeviceId(item.DimensionsJson), item.Unit,
            Enumerable.Range(0, plan.PointLimit).Select(index =>
            {
                var timestamp = plan.StartUtc.AddSeconds(index * plan.ResolutionSeconds);
                var bucket = indexed.GetValueOrDefault((item.Id, timestamp));
                return new MetricPointDto(timestamp, bucket?.Value,
                    bucket?.Value is not null ? "Good" : bucket?.IdleCount > 0 ? "Idle" : bucket?.WarmupCount > 0 ? "Warmup" : bucket?.UnavailableCount > 0 ? "Unavailable" : "Missing");
            }).ToArray(), plan.StartUtc, plan.EndUtc, plan.ResolutionSeconds)).ToArray();
    }

    private static MetricPointDto SanitizeLivePoint(MetricPointDto point) => point.Quality == "Good" &&
        (point.Value is null || !double.IsFinite(point.Value.Value)) ? point with { Value = null, Quality = "Unavailable" } : point;

    private static string DeviceId(string json)
    {
        try
        {
            using var dimensions = JsonDocument.Parse(json);
            return dimensions.RootElement.TryGetProperty("DeviceId", out var device) ? device.GetString() ?? "host" : "host";
        }
        catch (JsonException) { return "host"; }
    }
}

public sealed class MetricBucketRow
{
    public Guid SeriesId { get; set; }
    public DateTimeOffset SampledAtUtc { get; set; }
    public double? Value { get; set; }
    public long IdleCount { get; set; }
    public long WarmupCount { get; set; }
    public long UnavailableCount { get; set; }
}
