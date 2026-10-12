using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostProcessStoreTests
{
    [Fact]
    public void FirstSampleHasUnknownCpuAndSecondSampleUsesMonotonicWindow()
    {
        var source = new FakeSource { Items = [Item(12, 10)] };
        var time = new ManualTime();
        var store = new HostProcessStore(source, time);
        store.Sample(default);
        Assert.False(Assert.Single(store.Query(Query()).Items).HasCpuPercent);
        time.Advance(6);
        source.Items = [Item(12, 34)];
        store.Sample(default);
        var result = store.Query(Query());
        Assert.Equal(6, result.CpuWindowSeconds);
        Assert.Equal(400, Assert.Single(result.Items).CpuPercent);
    }

    [Fact]
    public void PidReuseDoesNotInheritCpuOrAuthorizeOldDetails()
    {
        var source = new FakeSource { Items = [Item(12, 10)] };
        var time = new ManualTime();
        var store = new HostProcessStore(source, time);
        store.Sample(default);
        var original = Assert.Single(store.Query(Query()).Items);
        time.Advance(6);
        source.Items = [Item(12, 100) with { StartedAtUtc = Item(12, 0).StartedAtUtc.AddSeconds(1) }];
        store.Sample(default);
        Assert.False(Assert.Single(store.Query(Query()).Items).HasCpuPercent);
        Assert.Throws<KeyNotFoundException>(() => store.Get(new HostProcessRequest { Pid = 12, InstanceId = original.InstanceId }));
    }

    [Fact]
    public void CounterRegressionIsUnknownNotAnInventedZeroSample()
    {
        var source = new FakeSource { Items = [Item(12, 10)] };
        var time = new ManualTime();
        var store = new HostProcessStore(source, time);
        store.Sample(default);
        time.Advance(6);
        source.Items = [Item(12, 5)];
        store.Sample(default);
        Assert.False(Assert.Single(store.Query(Query()).Items).HasCpuPercent);
    }

    [Fact]
    public void MemoryOrderPaginationAndFilterUseStablePidTies()
    {
        var source = new FakeSource { Items = [Item(30, 1, 100), Item(10, 1, 100), Item(20, 1, 20)] };
        var store = new HostProcessStore(source, new ManualTime());
        store.Sample(default);
        var query = Query();
        query.Sort = "memory";
        query.PageSize = 1;
        Assert.Equal(10, Assert.Single(store.Query(query).Items).Pid);
        query.Page = 2;
        Assert.Equal(30, Assert.Single(store.Query(query).Items).Pid);
        query.Filter = "PROCESS";
        Assert.Equal(3, store.Query(query).Total);
        query.Filter = "20";
        query.Page = 1;
        Assert.Equal(20, Assert.Single(store.Query(query).Items).Pid);
    }

    [Fact]
    public void StalePartialSnapshotIsExplicitAfterCollectionFailure()
    {
        var source = new FakeSource { Items = [Item(10, 1)], SkippedCount = 3, IsTruncated = true };
        var time = new ManualTime();
        var store = new HostProcessStore(source, time);
        store.Sample(default);
        source.Fail = true;
        time.Advance(19);
        Assert.Throws<IOException>(() => store.Sample(default));
        var result = store.Query(Query());
        Assert.True(result.IsStale);
        Assert.True(result.IsTruncated);
        Assert.Equal(3, result.SkippedCount);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData(0, 50, "cpu", "")]
    [InlineData(1, 501, "cpu", "")]
    [InlineData(10001, 50, "cpu", "")]
    [InlineData(1, 50, "command", "")]
    [InlineData(1, 50, "cpu", "bad\nfilter")]
    public void AgentIndependentlyRejectsInvalidQueries(uint page, uint size, string sort, string filter)
    {
        var store = new HostProcessStore(new FakeSource(), new ManualTime());
        Assert.Throws<ArgumentException>(() => store.Query(new HostProcessesRequest { Page = page, PageSize = size, Sort = sort, Filter = filter }));
    }

    [Fact]
    public void MissingSnapshotIsUnavailableInsteadOfEmptySuccess()
    {
        var store = new HostProcessStore(new FakeSource(), new ManualTime());
        Assert.Equal(StatusCode.Unavailable, Assert.Throws<RpcException>(() => store.Query(Query())).StatusCode);
    }

    [Fact]
    public void DetailsUseLiveStatisticsAndNeverOfferProcessTermination()
    {
        var source = new FakeSource { Items = [Item(12, 1)] };
        var store = new HostProcessStore(source, new ManualTime());
        store.Sample(default);
        var instance = Assert.Single(store.Query(Query()).Items).InstanceId;
        source.Items = [Item(12, 8, 2048)];
        var detail = store.Get(new HostProcessRequest { Pid = 12, InstanceId = instance });
        Assert.Equal(2048, detail.MemoryBytes);
        Assert.Equal(8, detail.CpuTimeSeconds);
        Assert.DoesNotContain(HostProcessSnapshot.Descriptor.Fields.InDeclarationOrder(), field =>
            field.Name.Contains("command", StringComparison.OrdinalIgnoreCase) || field.Name.Contains("environment", StringComparison.OrdinalIgnoreCase));
    }

    private static HostProcessesRequest Query() => new() { Page = 1, PageSize = 50, Sort = "cpu", Descending = true };
    private static HostProcessSample Item(int pid, double cpu, long memory = 1024) =>
        new(pid, 1, 1000, $"process-{pid}", "S", 2, new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), cpu, memory, 4096);

    private sealed class FakeSource : IHostProcessSource
    {
        public IReadOnlyCollection<HostProcessSample> Items { get; set; } = [];
        public bool Fail { get; set; }
        public bool IsTruncated { get; set; }
        public int SkippedCount { get; set; }
        public HostProcessBatch ReadAll(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException();
            return new(Items, SkippedCount, IsTruncated);
        }
        public HostProcessSample ReadProcess(int pid) => Items.Single(item => item.Pid == pid);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) { _now = _now.AddSeconds(seconds); _timestamp += TimeSpan.FromSeconds(seconds).Ticks; }
    }
}
