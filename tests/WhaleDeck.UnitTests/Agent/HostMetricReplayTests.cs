using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostMetricReplayTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-21T00:00:02.1234567Z", System.Globalization.CultureInfo.InvariantCulture);
    private static HostMetricBatch Batch(int index, int points = 1) => new(Start.AddSeconds(index * 6),
        Enumerable.Range(0, points).Select(device => new HostMetric("disk.write", "disk" + device, index, "bytesPerSecond", index == 0 ? "Warmup" : "Good")).ToArray());

    [Fact]
    public void LateReaderReceivesEveryOriginalBatchAcrossMinuteBoundaries()
    {
        var history = new HostMetricHistory();
        for (var i = 0; i < 31; i++) history.Append(Batch(i));
        var replay = history.ReadAfter(Start.AddSeconds(48)).ToArray();
        Assert.Equal(22, replay.Length);
        Assert.Equal(Enumerable.Range(9, 22).Select(i => Start.AddSeconds(i * 6)), replay.Select(item => item.SampledAtUtc));
        Assert.All(replay, item => Assert.Equal("Good", Assert.Single(item.Metrics).Quality));
        Assert.Empty(history.ReadAfter(replay[^1].SampledAtUtc));
        Assert.Equal(31, history.ReadAfter(null).Count); // Reading is not consuming or collecting.
    }

    [Fact]
    public void RetentionIsBoundedAndEvictionDoesNotInventOldSamples()
    {
        var history = new HostMetricHistory();
        for (var i = 0; i < 200; i++) history.Append(Batch(i));
        var retained = history.ReadAfter(null).ToArray();
        Assert.Equal(120, retained.Length);
        Assert.Equal(Batch(80).SampledAtUtc, retained[0].SampledAtUtc);
        Assert.Equal(Batch(199).SampledAtUtc, retained[^1].SampledAtUtc);
    }

    [Fact]
    public void PointBudgetEvictsWholeBatchesAndRejectsOversizedBatch()
    {
        var history = new HostMetricHistory();
        history.Append(Batch(0, 40_000));
        history.Append(Batch(1, 40_000));
        Assert.Equal(Batch(1).SampledAtUtc, Assert.Single(history.ReadAfter(null)).SampledAtUtc);
        Assert.Throws<InvalidOperationException>(() => history.Append(Batch(2, HostMetricHistory.MaximumPoints + 1)));
        Assert.Single(history.ReadAfter(null));
    }

    [Fact]
    public void AppendFreezesCallerOwnedCollection()
    {
        var source = Batch(1).Metrics.ToList();
        var history = new HostMetricHistory();
        history.Append(new(Start, source));
        source.Clear();
        Assert.Single(Assert.Single(history.ReadAfter(null)).Metrics);
    }

    [Fact]
    public async Task RpcLegacySnapshotStillReturnsOnlyLatestBatchAndCloses()
    {
        var reader = new HostReader();
        reader.CollectMetrics();
        var batch = reader.CollectMetrics();
        var writer = new Writer();
        await new HostGrpcService(reader, null!, null!, null!).StreamMetrics(new(), writer, new Context());
        Assert.Single(writer.Points, item => item.MetricKind == "batch.complete");
        Assert.All(writer.Points, point => Assert.Equal(batch.SampledAtUtc, point.SampledAtUtc.ToDateTimeOffset()));
    }

    [Fact]
    public async Task RpcReplayHonorsExclusiveCursorAndPreservesBatchMarkers()
    {
        var reader = new HostReader();
        var first = reader.CollectMetrics();
        var second = reader.CollectMetrics();
        var writer = new Writer();
        await new HostGrpcService(reader, null!, null!, null!).StreamMetrics(new()
            { ReplayRecent = true, AfterSampledAtUtc = Timestamp.FromDateTimeOffset(first.SampledAtUtc) }, writer, new Context());
        Assert.Single(writer.Points, item => item.MetricKind == "batch.complete");
        Assert.All(writer.Points, point => Assert.Equal(second.SampledAtUtc, point.SampledAtUtc.ToDateTimeOffset()));
        Assert.Same(second, reader.SampleMetrics());
    }

    [Theory]
    [InlineData(true, 6, false)]
    [InlineData(false, 0, true)]
    public async Task RpcRejectsAmbiguousReplayRequests(bool replay, uint interval, bool after)
    {
        var request = new MetricsRequest { ReplayRecent = replay, IntervalSeconds = interval };
        if (after) request.AfterSampledAtUtc = Timestamp.FromDateTimeOffset(Start);
        var error = await Assert.ThrowsAsync<RpcException>(() => new HostGrpcService(new(), null!, null!, null!).StreamMetrics(request, new Writer(), new Context()));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Fact]
    public async Task DecoderReturnsAllCanonicalValuesNotOnlyLastBatch()
    {
        var points = Frames(Enumerable.Range(0, 20).Select(i => Batch(i, 2))).ToArray();
        var values = await HostMetricReplayReader.ReadAsync(new Reader(points), null, default);
        Assert.Equal(40, values.Count);
        Assert.Equal(20, values.Select(item => item.SampledAtUtc).Distinct().Count());
        Assert.Equal("Warmup", values.First().Quality);
        Assert.Equal(0, values.First().Value);
        Assert.Equal(19, values.Last().Value);
    }

    [Theory]
    [InlineData("missing-marker")]
    [InlineData("empty-marker")]
    [InlineData("wrong-marker-time")]
    [InlineData("duplicate-metric")]
    [InlineData("duplicate-batch")]
    [InlineData("mixed-timestamps")]
    [InlineData("missing-timestamp")]
    [InlineData("invalid-timestamp")]
    [InlineData("extra-batch")]
    [InlineData("point-budget")]
    [InlineData("transport-failure")]
    public async Task DecoderRejectsPartialConflictingOrUnboundedResponses(string variant)
    {
        var points = Frames([Batch(1)]).ToList();
        switch (variant)
        {
            case "missing-marker": points.RemoveAt(1); break;
            case "empty-marker": points.RemoveAt(0); break;
            case "wrong-marker-time": points[1].SampledAtUtc = Timestamp.FromDateTimeOffset(Start); break;
            case "duplicate-metric": points.Insert(1, points[0].Clone()); break;
            case "duplicate-batch": points.AddRange(Frames([Batch(1)])); break;
            case "mixed-timestamps": var other = points[0].Clone(); other.DeviceId = "disk2"; other.SampledAtUtc = Timestamp.FromDateTimeOffset(Start); points.Insert(1, other); break;
            case "missing-timestamp": points[0].SampledAtUtc = null; break;
            case "invalid-timestamp": points[0].SampledAtUtc = new Timestamp { Seconds = long.MaxValue }; break;
            case "extra-batch": points = Frames(Enumerable.Range(0, 121).Select(i => Batch(i))).ToList(); break;
            case "point-budget": points = Frames([Batch(1, 65_537)]).ToList(); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => HostMetricReplayReader.ReadAsync(new Reader(points, variant == "transport-failure"), null, default));
    }

    [Fact]
    public async Task DecoderRejectsSamplesAtOrBeforeAcknowledgedCursor()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => HostMetricReplayReader.ReadAsync(new Reader(Frames([Batch(1)]).ToArray()), Batch(1).SampledAtUtc, default));
        Assert.Empty(await HostMetricReplayReader.ReadAsync(new Reader([]), Start, default));
    }

    private static IEnumerable<MetricPoint> Frames(IEnumerable<HostMetricBatch> batches)
    {
        foreach (var batch in batches)
        {
            foreach (var metric in batch.Metrics)
                yield return new() { MetricKind = metric.Kind, DeviceId = metric.DeviceId, Unit = metric.Unit, Quality = metric.Quality,
                    Value = metric.Value, SampledAtUtc = Timestamp.FromDateTimeOffset(batch.SampledAtUtc) };
            yield return new() { MetricKind = "batch.complete", SampledAtUtc = Timestamp.FromDateTimeOffset(batch.SampledAtUtc) };
        }
    }

    private sealed class Reader(IReadOnlyList<MetricPoint> points, bool failAtEnd = false) : IAsyncStreamReader<MetricPoint>
    {
        private int _index = -1;
        public MetricPoint Current => points[_index];
        public Task<bool> MoveNext(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (++_index == points.Count && failAtEnd) throw new InvalidOperationException("SYNTHETIC_TRANSPORT_FAILURE");
            return Task.FromResult(_index < points.Count);
        }
    }

    private sealed class Writer : IServerStreamWriter<MetricPoint>
    {
        public List<MetricPoint> Points { get; } = [];
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(MetricPoint message) { Points.Add(message); return Task.CompletedTask; }
    }

    private sealed class Context : ServerCallContext
    {
        protected override string MethodCore => "/whaledeck.agent.v1.HostService/StreamMetrics";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "unix";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(15);
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
