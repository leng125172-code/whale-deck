using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Stage one canonical Agent batch using bounded bulk lookups and idempotent sample identities.</summary>
public static class MetricSampleWriter
{
    public static Task StageAsync(PlatformDbContext db, Guid resourceId, IReadOnlyCollection<MetricValueDto> metrics, CancellationToken cancellationToken) =>
        StageManyAsync(db, metrics.Select(metric => new ResourceMetric(resourceId, metric)).ToArray(), cancellationToken);

    public static async Task StageManyAsync(PlatformDbContext db, IReadOnlyCollection<ResourceMetric> metrics, CancellationToken cancellationToken)
    {
        var batch = Canonicalize(metrics);
        if (batch.Length == 0) return;
        var kinds = batch.Select(item => item.Metric.Kind).Distinct(StringComparer.Ordinal).ToArray();
        var resourceIds = batch.Select(item => item.ResourceId).Distinct().ToArray();
        var saved = await db.MetricSeries.Where(item => resourceIds.Contains(item.ResourceId) && kinds.Contains(item.MetricKind)).ToArrayAsync(cancellationToken);
        var series = saved.GroupBy(item => (item.ResourceId, item.MetricKind, DeviceId(item.DimensionsJson)))
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.LastSeenAtUtc)!);
        var ids = saved.Select(item => item.Id).ToArray();
        var start = batch.Min(item => item.Metric.SampledAtUtc);
        var end = batch.Max(item => item.Metric.SampledAtUtc);
        var existing = ids.Length == 0 ? [] : await db.MetricSamples.AsNoTracking()
            .Where(item => ids.Contains(item.SeriesId) && item.SampledAtUtc >= start && item.SampledAtUtc <= end)
            .Select(item => new { item.SeriesId, item.SampledAtUtc }).ToArrayAsync(cancellationToken);
        var identities = existing.Select(item => (item.SeriesId, item.SampledAtUtc)).ToHashSet();
        foreach (var entry in batch)
        {
            var metric = entry.Metric;
            var key = (entry.ResourceId, metric.Kind, metric.DeviceId);
            if (!series.TryGetValue(key, out var target))
            {
                target = new MetricSeries { ResourceId = entry.ResourceId, MetricKind = metric.Kind, Unit = metric.Unit,
                    DimensionsJson = JsonSerializer.Serialize(new { metric.DeviceId }), LastSeenAtUtc = metric.SampledAtUtc };
                series.Add(key, target);
                db.MetricSeries.Add(target);
            }
            if (metric.SampledAtUtc > target.LastSeenAtUtc) target.LastSeenAtUtc = metric.SampledAtUtc;
            if (identities.Add((target.Id, metric.SampledAtUtc)))
                db.MetricSamples.Add(new MetricSample { SeriesId = target.Id, SampledAtUtc = metric.SampledAtUtc,
                    ValueDouble = metric.Value, Quality = metric.Quality });
        }
    }

    // PostgreSQL timestamptz stores microseconds, while DateTimeOffset can contain 100-ns ticks.
    // Normalize BEFORE lookup and HashSet identity comparison, not only when the provider writes SQL.
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().AddTicks(-(value.UtcTicks % 10));

    public static ResourceMetric[] Canonicalize(IReadOnlyCollection<ResourceMetric> metrics) => metrics
        .Select(item => item with { Metric = item.Metric with { SampledAtUtc = NormalizeTimestamp(item.Metric.SampledAtUtc) } })
        .GroupBy(item => (item.ResourceId, item.Metric.Kind, item.Metric.DeviceId, item.Metric.SampledAtUtc))
        .Select(group => group.Distinct().Count() == 1 ? group.First() : throw new InvalidOperationException("METRIC_IDENTITY_CONFLICT"))
        .OrderBy(item => item.Metric.SampledAtUtc).ToArray();

    private static string DeviceId(string dimensions)
    {
        using var json = JsonDocument.Parse(dimensions);
        return json.RootElement.TryGetProperty("DeviceId", out var value) ? value.GetString() ?? "host" : "host";
    }
}

public sealed record ResourceMetric(Guid ResourceId, MetricValueDto Metric);
