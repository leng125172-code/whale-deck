using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class HostProcessStore(IHostProcessSource source, TimeProvider time)
{
    private readonly object _sync = new();
    private HostProcessBatch? _previous;
    private long _previousTimestamp;
    private HostProcessesResponse? _snapshot;

    public void Sample(CancellationToken cancellationToken)
    {
        var batch = source.ReadAll(cancellationToken);
        lock (_sync)
        {
            var timestamp = time.GetTimestamp();
            var elapsed = _previous is null ? 0 : Math.Max(0, time.GetElapsedTime(_previousTimestamp, timestamp).TotalSeconds);
            var previous = _previous?.Items.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
            var snapshot = new HostProcessesResponse
            {
                SampledAtUtc = Timestamp.FromDateTimeOffset(time.GetUtcNow()), CpuWindowSeconds = elapsed,
                IsTruncated = batch.IsTruncated, SkippedCount = batch.SkippedCount
            };
            foreach (var item in batch.Items)
            {
                double? cpu = null;
                if (elapsed > 0 && previous?.TryGetValue(item.InstanceId, out var before) == true && item.CpuTimeSeconds >= before.CpuTimeSeconds)
                    cpu = Math.Max(0, item.CpuTimeSeconds - before.CpuTimeSeconds) * 100 / elapsed;
                snapshot.Items.Add(Map(item, cpu));
            }
            _snapshot = snapshot;
            _previous = batch;
            _previousTimestamp = timestamp;
        }
    }

    public HostProcessesResponse Query(HostProcessesRequest request)
    {
        if (request.Page is < 1 or > 10_000 || request.PageSize is < 1 or > 500 ||
            request.Sort is not ("cpu" or "memory" or "pid" or "name") ||
            request.Filter.Length > 128 || request.Filter.Any(char.IsControl))
            throw new ArgumentException("Invalid process query.");
        HostProcessesResponse snapshot;
        lock (_sync) snapshot = _snapshot?.Clone() ?? throw new RpcException(new Status(StatusCode.Unavailable, "Process sampling is warming up."));
        var items = snapshot.Items.Where(item => string.IsNullOrEmpty(request.Filter) ||
            item.Name.Contains(request.Filter, StringComparison.OrdinalIgnoreCase) ||
            item.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(request.Filter, StringComparison.Ordinal));
        var total = items.Count();
        IOrderedEnumerable<HostProcessSnapshot> ordered = request.Sort switch
        {
            "cpu" => request.Descending ? items.OrderBy(item => !item.HasCpuPercent).ThenByDescending(item => item.CpuPercent) : items.OrderBy(item => !item.HasCpuPercent).ThenBy(item => item.CpuPercent),
            "memory" => request.Descending ? items.OrderByDescending(item => item.MemoryBytes) : items.OrderBy(item => item.MemoryBytes),
            "name" => request.Descending ? items.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase) : items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            _ => request.Descending ? items.OrderByDescending(item => item.Pid) : items.OrderBy(item => item.Pid)
        };
        var response = new HostProcessesResponse
        {
            Total = total, Page = request.Page, PageSize = request.PageSize, SampledAtUtc = snapshot.SampledAtUtc,
            CpuWindowSeconds = snapshot.CpuWindowSeconds, IsTruncated = snapshot.IsTruncated, SkippedCount = snapshot.SkippedCount,
            IsStale = time.GetUtcNow() - snapshot.SampledAtUtc.ToDateTimeOffset() > TimeSpan.FromSeconds(18)
        };
        response.Items.AddRange(ordered.ThenBy(item => item.Pid).Skip(checked((int)((request.Page - 1) * request.PageSize))).Take(checked((int)request.PageSize)));
        return response;
    }

    public HostProcessSnapshot Get(HostProcessRequest request)
    {
        if (request.Pid <= 0 || request.InstanceId.Length is < 1 or > 64)
            throw new ArgumentException("A valid process identity is required.");
        HostProcessSample sample;
        try { sample = source.ReadProcess(request.Pid); }
        catch (Exception exception) when (HostProcessSource.IsUnavailable(exception))
        { throw new KeyNotFoundException("The process is no longer available."); }
        if (!string.Equals(sample.InstanceId, request.InstanceId, StringComparison.Ordinal))
            throw new KeyNotFoundException("The process identity has changed.");
        lock (_sync)
        {
            var previous = _snapshot?.Items.FirstOrDefault(item => item.InstanceId == sample.InstanceId);
            var fresh = _snapshot is not null && time.GetUtcNow() - _snapshot.SampledAtUtc.ToDateTimeOffset() <= TimeSpan.FromSeconds(18);
            return Map(sample, fresh && previous?.HasCpuPercent == true ? previous.CpuPercent : null);
        }
    }

    private static HostProcessSnapshot Map(HostProcessSample item, double? cpu)
    {
        var response = new HostProcessSnapshot
        {
            Pid = item.Pid, ParentPid = item.ParentPid, UserId = item.UserId, InstanceId = item.InstanceId,
            Name = item.Name, State = item.State, ThreadCount = item.ThreadCount,
            StartedAtUtc = Timestamp.FromDateTimeOffset(item.StartedAtUtc), CpuTimeSeconds = item.CpuTimeSeconds,
            MemoryBytes = item.MemoryBytes, VirtualMemoryBytes = item.VirtualMemoryBytes
        };
        if (cpu.HasValue) response.CpuPercent = cpu.Value;
        return response;
    }
}

public sealed class HostProcessSampler(HostProcessStore store, ILogger<HostProcessSampler> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> SamplingUnavailable = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(2301, "HostProcessSamplingUnavailable"), "Host process sampling is unavailable ({ErrorType}).");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reportedFailure = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { store.Sample(stoppingToken); reportedFailure = false; }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (!reportedFailure) SamplingUnavailable(logger, exception.GetType().Name, null);
                reportedFailure = true;
            }
            await Task.Delay(TimeSpan.FromSeconds(6), stoppingToken);
        }
    }
}
