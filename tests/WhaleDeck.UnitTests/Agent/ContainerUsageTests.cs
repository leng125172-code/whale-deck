using Docker.DotNet.Models;
using WhaleDeck.Agent.Services;
using NativeStats = Docker.DotNet.Models.ContainerStatsResponse;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerUsageTests
{
    [Fact]
    public void NativeCountersUseDockerCpuAndCgroupCacheSemantics()
    {
        var previous = Frame(0, 100, 1000);
        var current = Frame(6, 300, 1800);
        current.MemoryStats.Stats["inactive_file"] = 200;
        current.Networks = new Dictionary<string, NetworkStats> { ["eth0"] = new() { RxBytes = 4, TxBytes = 8 }, ["eth1"] = new() { RxBytes = 6, TxBytes = 12 } };
        current.BlkioStats = new() { IoServiceBytesRecursive = [new() { Op = "Read", Value = 30 }, new() { Op = "Write", Value = 40 }, new() { Op = "Total", Value = 70 }] };
        var result = ContainerUsageStore.Calculate("id", "running", current, previous);
        Assert.Equal(100, result.CpuPercent); // 200 / 800 * 4 cores * 100
        Assert.Equal(6, result.CpuWindowSeconds);
        Assert.Equal(800ul, result.MemoryUsedBytes);
        Assert.Equal(40, result.MemoryPercent);
        Assert.Equal(10ul, result.NetworkReceiveBytes);
        Assert.Equal(20ul, result.NetworkSendBytes);
        Assert.Equal(30ul, result.BlockReadBytes);
        Assert.Equal(40ul, result.BlockWriteBytes);
        Assert.Equal("Good", result.Quality);
    }

    [Fact]
    public void WarmupResetAndLongGapsDoNotInventCpuUsage()
    {
        var first = ContainerUsageStore.Calculate("id", "running", Frame(0, 100, 1000), null);
        Assert.False(first.HasCpuPercent);
        Assert.True(first.HasMemoryUsedBytes);
        Assert.Equal("Warmup", first.Quality);
        var reset = ContainerUsageStore.Calculate("id", "running", Frame(6, 1, 1800), Frame(0, 100, 1000));
        Assert.False(reset.HasCpuPercent);
        Assert.False(ContainerUsageStore.Calculate("id", "running", Frame(30, 500, 3000), Frame(0, 100, 1000)).HasCpuPercent);
        var busy = ContainerUsageStore.Calculate("id", "running", Frame(6, 500, 1800), Frame(0, 100, 1000));
        Assert.Equal(200, busy.CpuPercent); // Never clamp multiple cores to 100%.
    }

    [Fact]
    public void LegacyCacheAndMissingCountersRemainExplicit()
    {
        var frame = Frame(0, 1, 1);
        frame.MemoryStats.Stats["total_inactive_file"] = 100;
        frame.MemoryStats.Stats["inactive_file"] = 500;
        frame.MemoryStats.Limit = 0;
        var result = ContainerUsageStore.Calculate("id", "running", frame, null);
        Assert.Equal(900ul, result.MemoryUsedBytes);
        Assert.False(result.HasMemoryPercent);
        Assert.False(result.HasNetworkReceiveBytes);
        Assert.False(result.HasBlockReadBytes);
        Assert.False(result.HasPids);
        frame.MemoryStats.Stats["total_inactive_file"] = 2000;
        Assert.Equal(1000ul, ContainerUsageStore.Calculate("id", "running", frame, null).MemoryUsedBytes);
    }

    [Fact]
    public async Task BoundedParallelSamplingAndReadonlyClonesDoNotRepeatDockerQueries()
    {
        var clock = new TestClock();
        var source = new FakeSource(270);
        using var store = new ContainerUsageStore(source, clock);
        await store.CollectAsync(CancellationToken.None);
        var result = store.Snapshot();
        Assert.Equal(256, result.Items.Count);
        Assert.Equal(270, result.TotalCount);
        Assert.True(result.IsTruncated);
        Assert.Equal(14, result.SkippedCount);
        Assert.InRange(source.Peak, 2, 8);
        Assert.Equal(256, source.Reads);
        result.Items.Clear();
        var clones = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(store.Snapshot)));
        Assert.All(clones, value => Assert.Equal(256, value.Items.Count));
        Assert.Equal(256, source.Reads);
        clock.Now = clock.Now.AddSeconds(19);
        Assert.True(store.Snapshot().IsStale);
    }

    [Fact]
    public async Task RestartEventDropsBaselineEvenWhenResetCountersAlreadyExceededTheirOldValue()
    {
        var source = new FakeSource(1);
        var events = new DockerEventBuffer();
        using var store = new ContainerUsageStore(source, new TestClock(), events);
        await store.CollectAsync(CancellationToken.None);
        events.Add(new Message { Type = "container", Action = "start", Actor = new Actor { ID = "c0000" }, Time = new DateTimeOffset(2026, 10, 12, 0, 0, 2, TimeSpan.Zero).ToUnixTimeSeconds() });
        source.SampleSeconds = 6;
        await store.CollectAsync(CancellationToken.None);
        Assert.False(store.Snapshot().Items[0].HasCpuPercent);
        Assert.Equal("Warmup", store.Snapshot().Items[0].Quality);
    }

    [Fact]
    public async Task StoppedAndFailedContainersNeverBecomeZeroMeasurements()
    {
        var source = new FakeSource(2) { FailId = "c0001" };
        source.Containers[0].State = "exited";
        using var store = new ContainerUsageStore(source, new TestClock());
        await store.CollectAsync(CancellationToken.None);
        var result = store.Snapshot();
        Assert.Equal("Stopped", result.Items[0].Quality);
        Assert.Equal("Unavailable", result.Items[1].Quality);
        Assert.All(result.Items, item => Assert.False(item.HasCpuPercent));
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, source.Reads);
        source.ThrowOnList = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CollectAsync(CancellationToken.None));
        Assert.Equal(2, store.Snapshot().Items.Count);
    }

    private static NativeStats Frame(int seconds, ulong cpu, ulong system) => new()
    {
        Read = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds),
        CPUStats = new() { CPUUsage = new() { TotalUsage = cpu }, SystemUsage = system, OnlineCPUs = 4 },
        MemoryStats = new() { Usage = 1000, Limit = 2000, Stats = new Dictionary<string, ulong>() }
    };

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSource(int count) : IContainerUsageSource
    {
        public List<ContainerListResponse> Containers { get; } = Enumerable.Range(0, count).Select(index => new ContainerListResponse { ID = $"c{index:D4}", State = "running" }).ToList();
        public string? FailId { get; init; }
        public bool ThrowOnList { get; set; }
        public int SampleSeconds { get; set; }
        public int Reads;
        public int Peak;
        private int _active;
        public Task<IList<ContainerListResponse>> ListContainersAsync(bool all, CancellationToken cancellationToken) =>
            ThrowOnList ? throw new InvalidOperationException("unavailable") : Task.FromResult<IList<ContainerListResponse>>(Containers);
        public async Task<NativeStats> ReadContainerUsageAsync(string id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Reads);
            var active = Interlocked.Increment(ref _active);
            int seen;
            do { seen = Peak; } while (active > seen && Interlocked.CompareExchange(ref Peak, active, seen) != seen);
            try
            {
                await Task.Delay(5, cancellationToken);
                if (id == FailId) throw new InvalidOperationException("disappeared");
                return Frame(SampleSeconds, checked((ulong)(SampleSeconds + 1)), checked((ulong)(SampleSeconds * 10 + 1)));
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
