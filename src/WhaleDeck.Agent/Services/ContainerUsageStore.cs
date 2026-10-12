using Docker.DotNet.Models;
using Google.Protobuf.WellKnownTypes;
using WhaleDeck.Contracts.Agent.V1;
using NativeStats = Docker.DotNet.Models.ContainerStatsResponse;

namespace WhaleDeck.Agent.Services;

public interface IContainerUsageSource
{
    Task<IList<ContainerListResponse>> ListContainersAsync(bool all, CancellationToken cancellationToken);
    Task<NativeStats> ReadContainerUsageAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Bounded native Docker sampling cache. RPC reads never change counter baselines.</summary>
public sealed class ContainerUsageStore(IContainerUsageSource source, TimeProvider clock, DockerEventBuffer? events = null) : IDisposable
{
    public const int MaximumContainers = 256;
    private readonly SemaphoreSlim _collector = new(1, 1);
    private readonly object _sync = new();
    private Dictionary<string, NativeStats> _previous = new(StringComparer.Ordinal);
    private ContainerUsageBatch _snapshot = new() { IsStale = true, IntervalSeconds = 6 };

    public ContainerUsageBatch Snapshot()
    {
        lock (_sync)
        {
            var result = _snapshot.Clone();
            result.IsStale = result.SampledAtUtc is null || clock.GetUtcNow() - result.SampledAtUtc.ToDateTimeOffset() > TimeSpan.FromSeconds(18);
            return result;
        }
    }

    public async Task CollectAsync(CancellationToken cancellationToken)
    {
        await _collector.WaitAsync(cancellationToken);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(5));
            var containers = await source.ListContainersAsync(true, budget.Token);
            var selected = containers.OrderBy(item => item.ID, StringComparer.Ordinal).Take(MaximumContainers).ToArray();
            var output = new ContainerUsageSnapshot[selected.Length];
            var baselines = new NativeStats?[selected.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, selected.Length), new ParallelOptions
            { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (index, token) =>
            {
                var container = selected[index];
                var row = new ContainerUsageSnapshot { ContainerId = container.ID, State = container.State ?? "unknown", Quality = "Stopped" };
                if (container.State is "running" or "paused")
                {
                    try
                    {
                        budget.Token.ThrowIfCancellationRequested();
                        var current = await source.ReadContainerUsageAsync(container.ID, budget.Token);
                        var previous = _previous.GetValueOrDefault(container.ID);
                        if (previous is not null && events is not null)
                        {
                            var recent = events.Snapshot(new DateTimeOffset(previous.Read.ToUniversalTime()), 2048);
                            if (recent.Count >= 2048 || recent.Any(item => item.ResourceId == container.ID && item.EventType == "container" && item.Action is "start" or "restart" or "die"))
                                previous = null;
                        }
                        row = Calculate(container.ID, container.State, current, previous);
                        baselines[index] = current;
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        // A disappearing container or deadline failure is a missing value, not measured zero.
                        row.Quality = "Unavailable";
                    }
                }
                output[index] = row;
            });
            _previous = Enumerable.Range(0, selected.Length).Where(index => baselines[index] is not null)
                .ToDictionary(index => selected[index].ID, index => baselines[index]!, StringComparer.Ordinal);
            var snapshot = new ContainerUsageBatch
            {
                SampledAtUtc = Timestamp.FromDateTimeOffset(clock.GetUtcNow()), IntervalSeconds = 6,
                TotalCount = containers.Count, IsTruncated = containers.Count > MaximumContainers,
                SkippedCount = Math.Max(0, containers.Count - selected.Length) + output.Count(item => item.Quality == "Unavailable")
            };
            snapshot.Items.AddRange(output);
            lock (_sync) _snapshot = snapshot;
        }
        finally { _collector.Release(); }
    }

    public static ContainerUsageSnapshot Calculate(string id, string state, NativeStats current, NativeStats? previous)
    {
        if (current.Read == default) throw new InvalidOperationException("Docker did not provide a sample timestamp.");
        if (!string.IsNullOrEmpty(current.ID) && current.ID != id) throw new InvalidOperationException("Docker sample identity does not match the requested container.");
        var result = new ContainerUsageSnapshot
        {
            ContainerId = id, State = state, SampledAtUtc = Timestamp.FromDateTime(current.Read.ToUniversalTime()), Quality = "Warmup"
        };
        var seconds = previous is null ? 0 : (current.Read.ToUniversalTime() - previous.Read.ToUniversalTime()).TotalSeconds;
        var processors = current.CPUStats?.OnlineCPUs ?? 0;
        if (processors == 0) processors = checked((uint)(current.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 0));
        var valid = previous?.CPUStats?.CPUUsage is not null && current.CPUStats?.CPUUsage is not null &&
            seconds is > 0 and <= 18 && processors > 0 &&
            current.CPUStats.CPUUsage.TotalUsage >= previous.CPUStats.CPUUsage.TotalUsage &&
            current.CPUStats.SystemUsage > previous.CPUStats.SystemUsage;
        if (valid)
        {
            result.CpuPercent = (current.CPUStats!.CPUUsage.TotalUsage - previous!.CPUStats.CPUUsage.TotalUsage) /
                (double)(current.CPUStats.SystemUsage - previous.CPUStats.SystemUsage) * processors * 100;
            result.CpuWindowSeconds = seconds;
            result.Quality = "Good";
        }
        if (current.MemoryStats is { } memory)
        {
            var cache = memory.Stats is { } values && values.TryGetValue("total_inactive_file", out var legacyCache) ? legacyCache :
                memory.Stats is { } currentValues && currentValues.TryGetValue("inactive_file", out var currentCache) ? currentCache : 0;
            result.MemoryUsedBytes = cache < memory.Usage ? memory.Usage - cache : memory.Usage;
            if (memory.Limit > 0)
            {
                result.MemoryLimitBytes = memory.Limit;
                result.MemoryPercent = result.MemoryUsedBytes / (double)memory.Limit * 100;
            }
        }
        if (current.Networks is { Count: > 0 } networks)
        {
            result.NetworkReceiveBytes = Sum(networks.Values.Select(item => item.RxBytes));
            result.NetworkSendBytes = Sum(networks.Values.Select(item => item.TxBytes));
        }
        if (current.BlkioStats is { } block)
        {
            result.BlockReadBytes = Sum((block.IoServiceBytesRecursive ?? []).Where(item => string.Equals(item.Op, "Read", StringComparison.OrdinalIgnoreCase)).Select(item => item.Value));
            result.BlockWriteBytes = Sum((block.IoServiceBytesRecursive ?? []).Where(item => string.Equals(item.Op, "Write", StringComparison.OrdinalIgnoreCase)).Select(item => item.Value));
        }
        if (current.PidsStats is { } pids) result.Pids = pids.Current;
        return result;
    }

    private static ulong Sum(IEnumerable<ulong> values)
    {
        ulong total = 0;
        foreach (var value in values) total = checked(total + value);
        return total;
    }

    public void Dispose() => _collector.Dispose();
}

public sealed partial class ContainerUsageSampler(ContainerUsageStore store, ILogger<ContainerUsageSampler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextWarning = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        do
        {
            try { await store.CollectAsync(stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextWarning)
                {
                    LogSamplingFailure(logger, exception);
                    nextWarning = DateTimeOffset.UtcNow.AddMinutes(1);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 4013, Level = LogLevel.Warning, Message = "Container usage sampling failed; cached values become stale and the next scheduled batch will retry")]
    private static partial void LogSamplingFailure(ILogger logger, Exception exception);
}
