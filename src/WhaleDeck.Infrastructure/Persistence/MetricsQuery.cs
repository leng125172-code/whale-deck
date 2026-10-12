using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class MetricsQuery(PlatformDbContext db) : IMetricsQuery
{
    public async Task<IReadOnlyCollection<MetricSeriesSnapshotDto>> GetHistoryAsync(
        IReadOnlyCollection<string> kinds,
        int take,
        CancellationToken cancellationToken)
    {
        var normalized = kinds.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Take(12).ToArray();
        if (normalized.Length == 0) return [];
        var pointLimit = Math.Clamp(take, 1, 600);
        var since = DateTimeOffset.UtcNow.AddSeconds(-(pointLimit * 6 + 18));
        // Stale Docker interfaces must not consume the bounded series budget.
        // LastSeen is written with each sample; prioritize live series instead
        // of alphabetically taking old bridges before physical network devices.
        // Project correlated points in one SQL query, avoiding N+1 queries on
        // the dashboard's six-second refresh loop.
        var series = await db.MetricSeries.AsNoTracking()
            .Where(item => normalized.Contains(item.MetricKind) && item.LastSeenAtUtc >= since)
            .OrderByDescending(item => item.LastSeenAtUtc).ThenBy(item => item.MetricKind)
            .ThenBy(item => item.DimensionsJson).Take(256)
            .Select(item => new
            {
                item.MetricKind, item.DimensionsJson, item.Unit,
                Points = db.MetricSamples.AsNoTracking().Where(value => value.SeriesId == item.Id && value.SampledAtUtc >= since)
                    .OrderByDescending(value => value.SampledAtUtc).Take(pointLimit).OrderBy(value => value.SampledAtUtc)
                    .Select(value => new MetricPointDto(value.SampledAtUtc, value.ValueDouble, value.Quality)).ToArray()
            }).ToArrayAsync(cancellationToken);
        var result = new List<MetricSeriesSnapshotDto>(series.Length);
        foreach (var item in series)
        {
            var deviceId = "host";
            try
            {
                using var dimensions = JsonDocument.Parse(item.DimensionsJson);
                if (dimensions.RootElement.TryGetProperty("DeviceId", out var device)) deviceId = device.GetString() ?? deviceId;
            }
            catch (JsonException) { }
            result.Add(new MetricSeriesSnapshotDto(item.MetricKind, deviceId, item.Unit, item.Points));
        }
        return result;
    }
}
