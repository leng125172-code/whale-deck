using System.Globalization;

namespace WhaleDeck.Agent.Services;

public sealed record DiskIoCounters(ulong ReadOperations, ulong ReadSectors, ulong ReadMilliseconds,
    ulong WriteOperations, ulong WriteSectors, ulong WriteMilliseconds);

/// <summary>Linux completed I/O counters; latency includes kernel request waiting time.</summary>
public static class DiskIoMetrics
{
    public static bool TryParse(string line, out string device, out DiskIoCounters counters)
    {
        device = string.Empty;
        counters = new(0, 0, 0, 0, 0, 0);
        if (line.Length > 4096) return false;
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 14 || fields[2].Length > 128 || fields[2].Contains('/') || fields[2].Contains('\\')) return false;
        var positions = new[] { 3, 5, 6, 7, 9, 10 };
        var values = new ulong[positions.Length];
        for (var index = 0; index < positions.Length; index++)
            if (!ulong.TryParse(fields[positions[index]], NumberStyles.None, CultureInfo.InvariantCulture, out values[index])) return false;
        device = fields[2];
        counters = new(values[0], values[1], values[2], values[3], values[4], values[5]);
        return true;
    }

    public static IReadOnlyCollection<HostMetric> Calculate(string device, DiskIoCounters current, DiskIoCounters? previous, double elapsed)
    {
        var valid = previous is not null && double.IsFinite(elapsed) && elapsed > 0 &&
            current.ReadOperations >= previous.ReadOperations && current.WriteOperations >= previous.WriteOperations &&
            current.ReadSectors >= previous.ReadSectors && current.WriteSectors >= previous.WriteSectors &&
            current.ReadMilliseconds >= previous.ReadMilliseconds && current.WriteMilliseconds >= previous.WriteMilliseconds;
        var readOps = valid ? current.ReadOperations - previous!.ReadOperations : 0;
        var writeOps = valid ? current.WriteOperations - previous!.WriteOperations : 0;
        var quality = valid ? "Good" : "Warmup";
        return [
            new("disk.read", device, valid ? (current.ReadSectors - previous!.ReadSectors) * 512d / elapsed : 0, "bytesPerSecond", quality),
            new("disk.write", device, valid ? (current.WriteSectors - previous!.WriteSectors) * 512d / elapsed : 0, "bytesPerSecond", quality),
            new("disk.read.iops", device, valid ? readOps / elapsed : 0, "operationsPerSecond", quality),
            new("disk.write.iops", device, valid ? writeOps / elapsed : 0, "operationsPerSecond", quality),
            new("disk.read.latency", device, readOps > 0 ? (current.ReadMilliseconds - previous!.ReadMilliseconds) / (double)readOps : 0, "milliseconds", !valid ? "Warmup" : readOps == 0 ? "Idle" : "Good"),
            new("disk.write.latency", device, writeOps > 0 ? (current.WriteMilliseconds - previous!.WriteMilliseconds) / (double)writeOps : 0, "milliseconds", !valid ? "Warmup" : writeOps == 0 ? "Idle" : "Good")
        ];
    }
}
