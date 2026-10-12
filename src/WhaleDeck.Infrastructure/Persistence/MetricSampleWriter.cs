using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Stage one canonical Agent batch using bounded bulk lookups and idempotent sample identities.</summary>
public static class MetricSampleWriter
{
    public static async Task StageAsync(PlatformDbContext db, Guid resourceId, IReadOnlyCollection<MetricValueDto> metrics, CancellationToken cancellationToken)
    {
        var batch = metrics.GroupBy(item => (item.Kind, item.DeviceId)).Select(group => group.MaxBy(item => item.SampledAtUtc)!).ToArray();
        if (batch.Length == 0) return;
        var kinds = batch.Select(item => item.Kind).Distinct(StringComparer.Ordinal).ToArray();
        var saved = await db.MetricSeries.Where(item => item.ResourceId == resourceId && kinds.Contains(item.MetricKind)).ToArrayAsync(cancellationToken);
        var series = saved.GroupBy(item => (item.MetricKind, DeviceId(item.DimensionsJson)))
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.LastSeenAtUtc)!);
        var ids = saved.Select(item => item.Id).ToArray();
        var start = batch.Min(item => item.SampledAtUtc);
        var end = batch.Max(item => item.SampledAtUtc);
        var existing = ids.Length == 0 ? [] : await db.MetricSamples.AsNoTracking()
            .Where(item => ids.Contains(item.SeriesId) && item.SampledAtUtc >= start && item.SampledAtUtc <= end)
            .Select(item => new { item.SeriesId, item.SampledAtUtc }).ToArrayAsync(cancellationToken);
        var identities = existing.Select(item => (item.SeriesId, item.SampledAtUtc)).ToHashSet();
        foreach (var metric in batch)
        {
            var key = (metric.Kind, metric.DeviceId);
            if (!series.TryGetValue(key, out var target))
            {
                target = new MetricSeries { ResourceId = resourceId, MetricKind = metric.Kind, Unit = metric.Unit,
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

    private static string DeviceId(string dimensions)
    {
        using var json = JsonDocument.Parse(dimensions);
        return json.RootElement.TryGetProperty("DeviceId", out var value) ? value.GetString() ?? "host" : "host";
    }
}
