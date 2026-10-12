using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

/// <summary>Projects canonical Docker samples into the existing metric store without inventing unknown values.</summary>
public static class ContainerMetricProjection
{
    public static IReadOnlyCollection<MetricValueDto> Project(ContainerUsageDto row, DateTimeOffset now)
    {
        if (row.SampledAtUtc is not { } timestamp || now - timestamp > TimeSpan.FromSeconds(18) ||
            row.Quality is "Stopped" or "Unavailable") return [];
        var values = new List<MetricValueDto>
        {
            new("container.cpu.utilization", row.ContainerId, row.CpuPercent ?? 0, "percent", row.CpuPercent is null ? "Warmup" : "Good", timestamp)
        };
        Add("container.memory.used", row.MemoryUsedBytes, "bytes");
        Add("container.memory.limit", row.MemoryLimitBytes, "bytes");
        Add("container.memory.utilization", row.MemoryPercent, "percent");
        Add("container.network.receive.total", row.NetworkReceiveBytes, "bytes");
        Add("container.network.send.total", row.NetworkSendBytes, "bytes");
        Add("container.block.read.total", row.BlockReadBytes, "bytes");
        Add("container.block.write.total", row.BlockWriteBytes, "bytes");
        Add("container.pids.count", row.Pids, "count");
        return values;

        void Add(string kind, double? value, string unit)
        {
            if (value is { } number && double.IsFinite(number) && number >= 0)
                values.Add(new(kind, row.ContainerId, number, unit, "Good", timestamp));
        }
    }
}
