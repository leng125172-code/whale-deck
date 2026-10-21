using Grpc.Core;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

/// <summary>All-or-nothing decoding: truncated streams must never acknowledge a partial batch.</summary>
public static class HostMetricReplayReader
{
    public static async Task<IReadOnlyCollection<MetricValueDto>> ReadAsync(IAsyncStreamReader<MetricPoint> stream,
        DateTimeOffset? afterUtc, CancellationToken cancellationToken)
    {
        var values = new List<MetricValueDto>();
        var identities = new HashSet<(string Kind, string Device)>();
        DateTimeOffset? current = null;
        var previous = afterUtc;
        var batches = 0;
        while (await stream.MoveNext(cancellationToken))
        {
            var point = stream.Current;
            if (point.SampledAtUtc is null) throw Invalid();
            var timestamp = point.SampledAtUtc.ToDateTimeOffset();
            if (point.MetricKind == "batch.complete")
            {
                if (current != timestamp || identities.Count == 0 || ++batches > 120) throw Invalid();
                previous = timestamp;
                current = null;
                identities.Clear();
                continue;
            }
            if (values.Count >= 65_536 || point.MetricKind.Length is 0 or > 128 || point.DeviceId.Length is 0 or > 256 ||
                point.Unit.Length is 0 or > 64 || point.Quality.Length is 0 or > 32 ||
                previous is not null && timestamp <= previous.Value || current is not null && timestamp != current.Value ||
                !identities.Add((point.MetricKind, point.DeviceId))) throw Invalid();
            current = timestamp;
            values.Add(new(point.MetricKind, point.DeviceId, point.Value, point.Unit, point.Quality, timestamp));
        }
        if (current is not null) throw Invalid();
        return values;
    }

    private static InvalidOperationException Invalid() => new("HOST_METRIC_REPLAY_INVALID");
}
