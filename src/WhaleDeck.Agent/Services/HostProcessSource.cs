using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace WhaleDeck.Agent.Services;

public sealed record HostProcessSample(
    int Pid, int ParentPid, uint UserId, string Name, string State, int ThreadCount,
    DateTimeOffset StartedAtUtc, double CpuTimeSeconds, long MemoryBytes, long VirtualMemoryBytes)
{
    public string InstanceId => string.Create(CultureInfo.InvariantCulture, $"{Pid}:{StartedAtUtc.UtcTicks}");
}

public sealed record HostProcessBatch(IReadOnlyCollection<HostProcessSample> Items, int SkippedCount, bool IsTruncated);

public interface IHostProcessSource
{
    HostProcessBatch ReadAll(CancellationToken cancellationToken);
    HostProcessSample ReadProcess(int pid);
}

/// <summary>Reads fixed, non-secret process statistics; never argv, environ or executable paths.</summary>
public sealed class HostProcessSource : IHostProcessSource
{
    private const int MaximumProcesses = 32_768;

    public HostProcessBatch ReadAll(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux process sampling is required.");
        var ids = Directory.EnumerateDirectories("/proc")
            .Select(path => int.TryParse(Path.GetFileName(path), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : 0)
            .Where(pid => pid > 0).Take(MaximumProcesses + 1).ToArray();
        var samples = new List<HostProcessSample>();
        var skipped = 0;
        foreach (var pid in ids.Take(MaximumProcesses))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { samples.Add(ReadProcess(pid)); }
            catch (Exception exception) when (IsUnavailable(exception)) { skipped++; }
        }
        if (samples.Count == 0 && ids.Length > 0) throw new InvalidOperationException("Process statistics are unavailable.");
        return new(samples, skipped, ids.Length > MaximumProcesses);
    }

    public HostProcessSample ReadProcess(int pid)
    {
        if (pid <= 0) throw new ArgumentException("A positive process id is required.");
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux process sampling is required.");
        using var process = Process.GetProcessById(pid);
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
        var status = ReadStatus(pid);
        var sample = new HostProcessSample(pid, status.ParentPid, status.UserId,
            status.Name,
            status.State, status.ThreadCount, started, process.TotalProcessorTime.TotalSeconds,
            status.MemoryBytes, status.VirtualMemoryBytes);
        using var current = Process.GetProcessById(pid);
        if (current.StartTime.ToUniversalTime() != started.UtcDateTime)
            throw new KeyNotFoundException("The process identity changed during collection.");
        return sample;
    }

    public static bool IsUnavailable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException or KeyNotFoundException;

    private static HostProcessStatus ReadStatus(int pid)
    {
        using var reader = new StreamReader(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/status"));
        return ParseStatus(reader);
    }

    internal static HostProcessStatus ParseStatus(TextReader reader)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var count = 0; count < 128 && reader.ReadLine() is { } line; count++)
        {
            if (line.Length > 512) continue;
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator];
            if (name is "Name" or "PPid" or "Uid" or "State" or "Threads" or "VmRSS" or "VmSize")
            {
                var value = line[(separator + 1)..].Trim();
                if (name == "Name") { values[name] = value; continue; }
                var tokens = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0) values[name] = tokens[0];
            }
        }
        if (!int.TryParse(values.GetValueOrDefault("PPid"), NumberStyles.None, CultureInfo.InvariantCulture, out var parent) ||
            !uint.TryParse(values.GetValueOrDefault("Uid"), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) ||
            !int.TryParse(values.GetValueOrDefault("Threads"), NumberStyles.None, CultureInfo.InvariantCulture, out var threads) ||
            !values.TryGetValue("State", out var state) || !values.TryGetValue("Name", out var processName))
            throw new IOException("Process statistics disappeared during collection.");
        return new(parent, uid, state, threads,
            new string(processName.Where(character => !char.IsControl(character)).Take(128).ToArray()),
            MemoryBytes("VmRSS"), MemoryBytes("VmSize"));

        long MemoryBytes(string field)
        {
            // Kernel threads do not have address-space entries in status.
            if (!values.TryGetValue(field, out var raw)) return 0;
            if (!ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes) || kilobytes > long.MaxValue / 1024)
                throw new IOException("Process memory statistics are invalid.");
            return checked((long)kilobytes * 1024);
        }
    }
}

internal sealed record HostProcessStatus(int ParentPid, uint UserId, string State, int ThreadCount,
    string Name, long MemoryBytes, long VirtualMemoryBytes);
