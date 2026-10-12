namespace WhaleDeck.Application.Services;

public sealed record MetricHistoryPlan(string[] Kinds, string Window, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, int ResolutionSeconds, int PointLimit, bool Aggregate);

public static class MetricHistoryPolicy
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        "cpu.utilization", "memory.utilization", "disk.utilization", "disk.read", "disk.write",
        "disk.read.iops", "disk.write.iops", "disk.read.latency", "disk.write.latency",
        "network.receive", "network.send", "network.receive.total", "network.send.total"
    };

    public static MetricHistoryPlan Create(IReadOnlyCollection<string> kinds, string window, int take, DateTimeOffset now)
    {
        var normalized = kinds.Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0 || normalized.Length > 16 || normalized.Any(kind => !AllowedKinds.Contains(kind)))
            throw new ArgumentException("One or more metric kinds are not approved.");
        now = now.ToUniversalTime();
        if (window == "live")
        {
            var limit = Math.Clamp(take, 1, 600);
            return new(normalized, window, now.AddSeconds(-(limit * 6 + 18)), now, 6, limit, false);
        }
        var (duration, resolution) = window switch
        {
            "1h" => (3600, 30), "24h" => (86400, 300), "7d" => (604800, 1800),
            _ => throw new ArgumentException("Metric window must be live, 1h, 24h or 7d.")
        };
        var lastBucket = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / resolution * resolution);
        return new(normalized, window, lastBucket.AddSeconds(-duration), now, resolution, duration / resolution + 1, true);
    }
}
