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
                item.Points, plan.StartUtc, plan.EndUtc, 6)).ToArray();
        }

        var series = await query.ToArrayAsync(cancellationToken);
        if (series.Length == 0) return [];
        var ids = series.Select(item => item.Id).ToArray();
        // Aggregate on the server; completed-I/O latency is weighted by matching IOPS.
        var buckets = await db.Database.SqlQuery<MetricBucketRow>($"""
            SELECT sample.series_id AS "SeriesId",
                date_bin(make_interval(secs => {plan.ResolutionSeconds}), sample.sampled_at_utc, TIMESTAMPTZ '1970-01-01') AS "SampledAtUtc",
                CASE WHEN source.metric_kind IN ('disk.read.latency', 'disk.write.latency') THEN
                    SUM(sample.value_double * weight.value_double) FILTER (WHERE sample.quality = 'Good' AND weight.quality = 'Good') /
                    NULLIF(SUM(weight.value_double) FILTER (WHERE sample.quality = 'Good' AND weight.quality = 'Good'), 0)
                ELSE AVG(sample.value_double) FILTER (WHERE sample.quality = 'Good') END AS "Value",
                COUNT(*) FILTER (WHERE sample.quality = 'Idle') AS "IdleCount"
            FROM metric_samples sample
            JOIN metric_series source ON source.id = sample.series_id
            LEFT JOIN metric_series weights ON weights.resource_id = source.resource_id AND weights.dimensions_json = source.dimensions_json
                AND weights.metric_kind = CASE source.metric_kind WHEN 'disk.read.latency' THEN 'disk.read.iops' WHEN 'disk.write.latency' THEN 'disk.write.iops' ELSE NULL END
            LEFT JOIN metric_samples weight ON weight.series_id = weights.id AND weight.sampled_at_utc = sample.sampled_at_utc
            WHERE sample.series_id = ANY({ids}) AND sample.sampled_at_utc >= {plan.StartUtc} AND sample.sampled_at_utc <= {plan.EndUtc}
            GROUP BY sample.series_id, source.metric_kind, "SampledAtUtc"
            ORDER BY "SampledAtUtc"
            """).ToArrayAsync(cancellationToken);
        var indexed = buckets.ToDictionary(item => (item.SeriesId, item.SampledAtUtc));
        return series.Select(item => new MetricSeriesSnapshotDto(item.MetricKind, DeviceId(item.DimensionsJson), item.Unit,
            Enumerable.Range(0, plan.PointLimit).Select(index =>
            {
                var timestamp = plan.StartUtc.AddSeconds(index * plan.ResolutionSeconds);
                var bucket = indexed.GetValueOrDefault((item.Id, timestamp));
                return new MetricPointDto(timestamp, bucket?.Value,
                    bucket?.Value is not null ? "Good" : bucket?.IdleCount > 0 ? "Idle" : "Missing");
            }).ToArray(), plan.StartUtc, plan.EndUtc, plan.ResolutionSeconds)).ToArray();
    }

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
}
