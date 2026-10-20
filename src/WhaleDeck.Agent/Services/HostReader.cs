using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace WhaleDeck.Agent.Services;

public sealed record HostSnapshot(
    string HostName,
    string Distribution,
    string Kernel,
    string Architecture,
    long BootTimeUnixSeconds,
    long UptimeSeconds,
    uint ProcessorCount,
    ulong TotalMemoryBytes,
    IReadOnlyCollection<HostAddressSnapshot> Addresses);

public sealed record HostAddressSnapshot(string InterfaceName, string Address);

public sealed class HostReader(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _sync = new();
    private CpuCounters? _previousCpu;
    private long? _previousIoTimestamp;
    private HostMetricBatch? _cachedMetrics;
    private Dictionary<string, (ulong Received, ulong Sent)> _previousNetwork = new(StringComparer.Ordinal);
    private Dictionary<string, DiskIoCounters> _previousDisk = new(StringComparer.Ordinal);

    public HostMetricBatch SampleMetrics()
    {
        lock (_sync)
        {
            return _cachedMetrics ?? CollectMetrics();
        }
    }

    public HostMetricBatch CollectMetrics()
    {
        lock (_sync)
        {
            var usage = ReadUsage();
            var values = new List<HostMetric> { new("cpu.utilization", "host", usage.CpuPercent, "percent"), new("memory.utilization", "host", usage.MemoryPercent, "percent") };
            string? load = null;
            if (OperatingSystem.IsLinux())
            {
                try { load = File.ReadAllText("/proc/loadavg"); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            values.AddRange(HostLoadMetrics.Parse(load, Environment.ProcessorCount));
            values.AddRange(ReadDeviceMetrics());
            _cachedMetrics = new(_clock.GetUtcNow(), values);
            return _cachedMetrics;
        }
    }

    public HostSnapshot Read()
    {
        var uptime = ReadUptime();
        return new HostSnapshot(
            Environment.MachineName,
            ReadDistribution(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - uptime,
            uptime,
            (uint)Environment.ProcessorCount,
            ReadTotalMemory(),
            ReadHostAddresses());
    }

    private (double CpuPercent, double MemoryPercent) ReadUsage()
    {
        var memory = ReadMemoryInfo();
        var used = memory.Total == 0 ? 0 : (memory.Total - memory.Available) * 100d / memory.Total;
        return (ReadCpuPercent(), used);
    }

    private List<HostMetric> ReadDeviceMetrics()
    {
        lock (_sync)
        {
            var timestamp = _clock.GetTimestamp();
            var elapsed = _previousIoTimestamp is null ? 0 : _clock.GetElapsedTime(_previousIoTimestamp.Value, timestamp).TotalSeconds;
            var result = new List<HostMetric>();
            var network = ReadNetworkCounters();
            foreach (var (device, value) in network)
            {
                var hasPrevious = _previousNetwork.TryGetValue(device, out var previous) && elapsed > 0 &&
                    value.Received >= previous.Received && value.Sent >= previous.Sent;
                result.Add(new("network.receive", device, hasPrevious ? Rate(value.Received, previous.Received, elapsed) : 0, "bytesPerSecond", hasPrevious ? "Good" : "Warmup"));
                result.Add(new("network.send", device, hasPrevious ? Rate(value.Sent, previous.Sent, elapsed) : 0, "bytesPerSecond", hasPrevious ? "Good" : "Warmup"));
                result.Add(new("network.receive.total", device, value.Received, "bytes"));
                result.Add(new("network.send.total", device, value.Sent, "bytes"));
            }
            var disks = ReadDiskCounters();
            foreach (var (device, value) in disks)
            {
                result.AddRange(DiskIoMetrics.Calculate(device, value, _previousDisk.GetValueOrDefault(device), elapsed));
            }
            result.AddRange(ReadDiskCapacityMetrics());
            _previousIoTimestamp = timestamp;
            _previousNetwork = network;
            _previousDisk = disks;
            return result;
        }
    }

    private static List<HostMetric> ReadDiskCapacityMetrics()
    {
        var result = new List<HostMetric>();
        IReadOnlyDictionary<string, LinuxFileSystemMount>? mounts = null;
        try
        {
            if (OperatingSystem.IsLinux())
                mounts = DiskCapacityMetrics.ParseMountInfo(File.ReadLines("/proc/self/mountinfo"));
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    var mount = mounts?.GetValueOrDefault(drive.Name);
                    if (OperatingSystem.IsLinux() && mount is null) continue;
                    var snapshot = new FileSystemCapacitySnapshot(drive.Name, drive.DriveFormat, drive.DriveType,
                        drive.TotalSize, drive.AvailableFreeSpace);
                    if (DiskCapacityMetrics.TryCalculate(snapshot, mount, out var metric)) result.Add(metric);
                }
                // A disappearing USB/network mount must not discard healthy disks or the rest of the batch.
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return result;
    }

    private static long ReadUptime()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/uptime"))
        {
            return Environment.TickCount64 / 1000;
        }

        var value = File.ReadAllText("/proc/uptime").Split(' ', 2)[0];
        return long.TryParse(value.Split('.')[0], out var seconds) ? seconds : 0;
    }

    private static string ReadDistribution()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/etc/os-release"))
        {
            return RuntimeInformation.OSDescription;
        }

        var line = File.ReadLines("/etc/os-release").FirstOrDefault(item => item.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
        return line?["PRETTY_NAME=".Length..].Trim('"') ?? RuntimeInformation.OSDescription;
    }

    private static ulong ReadTotalMemory() => ReadMemoryInfo().Total;

    private static HostAddressSnapshot[] ReadHostAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                           item.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                           IsHostNetworkDevice(item.Name))
            .SelectMany(item => item.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(address.Address))
                .Select(address => new HostAddressSnapshot(item.Name, address.Address.ToString())))
            .OrderBy(item => item.InterfaceName, StringComparer.Ordinal)
            .ThenBy(item => item.Address, StringComparer.Ordinal)
            .ToArray();

    private static (ulong Total, ulong Available) ReadMemoryInfo()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo"))
        {
            var info = GC.GetGCMemoryInfo();
            return ((ulong)Math.Max(info.TotalAvailableMemoryBytes, 0), 0);
        }

        ulong total = 0;
        ulong available = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !ulong.TryParse(parts[1], out var kilobytes))
            {
                continue;
            }

            if (parts[0] == "MemTotal:") total = kilobytes * 1024;
            if (parts[0] == "MemAvailable:") available = kilobytes * 1024;
        }
        return (total, available);
    }

    private double ReadCpuPercent()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/stat"))
        {
            return 0;
        }
        var parts = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(value => ulong.TryParse(value, out var parsed) ? parsed : 0).ToArray();
        if (parts.Length < 5) return 0;
        var current = new CpuCounters(parts.Aggregate(0UL, (sum, value) => sum + value), parts[3] + (parts.Length > 4 ? parts[4] : 0));
        lock (_sync)
        {
            var previous = _previousCpu;
            _previousCpu = current;
            if (previous is null || current.Total <= previous.Total) return 0;
            var total = current.Total - previous.Total;
            var idle = current.Idle >= previous.Idle ? current.Idle - previous.Idle : 0;
            return Math.Clamp((total - Math.Min(idle, total)) * 100d / total, 0, 100);
        }
    }

    private static Dictionary<string, (ulong Received, ulong Sent)> ReadNetworkCounters()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/net/dev")) return [];
        var result = new Dictionary<string, (ulong, ulong)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
        {
            var halves = line.Split(':', 2);
            if (halves.Length != 2) continue;
            var device = halves[0].Trim();
            if (!IsHostNetworkDevice(device)) continue;
            var values = halves[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 16 || !ulong.TryParse(values[0], out var received) || !ulong.TryParse(values[8], out var sent)) continue;
            result[device] = (received, sent);
        }
        return result;
    }

    private static Dictionary<string, DiskIoCounters> ReadDiskCounters()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/diskstats")) return [];
        var result = new Dictionary<string, DiskIoCounters>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines("/proc/diskstats"))
        {
            if (!DiskIoMetrics.TryParse(line, out var name, out var counters)) continue;
            // Whole hardware devices only: summing partitions or device-mapper views double counts I/O.
            if (File.Exists($"/sys/class/block/{name}/partition") || !Directory.Exists($"/sys/class/block/{name}/device")) continue;
            result[name] = counters;
        }
        return result;
    }

    private static double Rate(ulong current, ulong previous, double elapsed) =>
        elapsed <= 0 || current < previous ? 0 : (current - previous) / elapsed;

    private static bool IsHostNetworkDevice(string name) =>
        !name.Equals("lo", StringComparison.Ordinal) &&
        !name.StartsWith("veth", StringComparison.Ordinal) &&
        !name.StartsWith("docker", StringComparison.Ordinal) &&
        !name.StartsWith("br-", StringComparison.Ordinal) &&
        !name.StartsWith("virbr", StringComparison.Ordinal) &&
        !name.StartsWith("cni", StringComparison.Ordinal) &&
        !name.StartsWith("flannel", StringComparison.Ordinal) &&
        !name.StartsWith("tun", StringComparison.Ordinal) &&
        !name.StartsWith("tap", StringComparison.Ordinal);

    private sealed record CpuCounters(ulong Total, ulong Idle);
}

public sealed record HostMetric(string Kind, string DeviceId, double Value, string Unit, string Quality = "Good");
public sealed record HostMetricBatch(DateTimeOffset SampledAtUtc, IReadOnlyCollection<HostMetric> Metrics);
