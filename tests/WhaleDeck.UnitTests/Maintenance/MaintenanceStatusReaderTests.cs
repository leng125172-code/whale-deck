using System.Net;
using System.Text.Json;
using WhaleDeck.MaintenanceHost;

namespace WhaleDeck.UnitTests.Maintenance;

public sealed class MaintenanceStatusReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wd-maintenance-" + Guid.NewGuid().ToString("N"));
    private string PathToStatus => Path.Combine(_directory, "status.json");
    public MaintenanceStatusReaderTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(HttpStatusCode.OK, 200, "healthy")]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503, "Unavailable")]
    [InlineData(HttpStatusCode.Redirect, 503, "Unavailable")]
    public async Task MissingRuntimeFileUsesFixedBackendHealth(HttpStatusCode response, int code, string state)
    {
        var handler = new Probe(response); using var reader = Reader(handler);
        var result = await reader.ReadAsync(PathToStatus, default);
        Assert.Equal(code, result.StatusCode); Assert.Equal(state, result.Body.State);
        Assert.Equal(MaintenanceStatusReader.ProbeAddress, handler.Address);
        Assert.Empty(handler.Headers!);
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"state\":5}")]
    [InlineData("{\"state\":\"invented\"}")]
    public async Task InvalidStateFailsClosedWithoutThrowingOrForwardingFileContent(string content)
    {
        await File.WriteAllTextAsync(PathToStatus, content);
        var handler = new Probe(HttpStatusCode.OK); using var reader = Reader(handler);
        var result = await reader.ReadAsync(PathToStatus, default);
        Assert.Equal(503, result.StatusCode); Assert.Equal("InvalidMaintenanceState", result.Body.Phase);
        Assert.DoesNotContain(_directory, JsonSerializer.Serialize(result)); Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task OversizeStateIsBoundedAndNeverReturned()
    {
        await File.WriteAllTextAsync(PathToStatus, "{\"state\":\"updating\",\"extra\":\"" + new string('x', 9000) + "\"}");
        using var reader = Reader(new Probe(HttpStatusCode.OK));
        var result = await reader.ReadAsync(PathToStatus, default);
        Assert.Equal(503, result.StatusCode); Assert.True(JsonSerializer.Serialize(result).Length < 1024);
    }
    [Fact]
    public async Task ExplicitUpdateRemainsObservableWithoutDockerAndDropsUnknownFields()
    {
        await File.WriteAllTextAsync(PathToStatus, "{\"state\":\"updating\",\"message\":\"等待平台服务\",\"extra\":\"synthetic-private-value\"}");
        var handler = new Probe(HttpStatusCode.ServiceUnavailable); using var reader = Reader(handler);
        var result = await reader.ReadAsync(PathToStatus, default);
        Assert.Equal(200, result.StatusCode); Assert.Equal("updating", result.Body.State);
        Assert.Equal("等待平台服务", result.Body.Message); Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain("synthetic-private-value", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task OldHealthyFileDoesNotHideBackendFailure()
    {
        await File.WriteAllTextAsync(PathToStatus, "{\"state\":\"healthy\"}");
        using var reader = Reader(new Probe(HttpStatusCode.ServiceUnavailable));
        Assert.Equal("GatewayOffline", (await reader.ReadAsync(PathToStatus, default)).Body.Phase);
    }
    [Fact]
    public async Task ProbeFailuresAreSanitized()
    {
        using var reader = Reader(new Probe(HttpStatusCode.OK) { Failure = new HttpRequestException("synthetic-private-error") });
        var result = await reader.ReadAsync(PathToStatus, default);
        Assert.Equal(503, result.StatusCode); Assert.DoesNotContain("synthetic-private-error", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task ProbeTimeoutIsUnavailableInsteadOfAStatusEndpointFailure()
    {
        using var reader = Reader(new Probe(HttpStatusCode.OK) { Failure = new TaskCanceledException("synthetic timeout") });
        Assert.Equal(503, (await reader.ReadAsync(PathToStatus, default)).StatusCode);
    }
    [Fact]
    public async Task OneCanceledCallerCannotCancelAnotherCallersSharedProbe()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Probe(HttpStatusCode.OK) { Pending = completion.Task };
        using var reader = Reader(handler);
        using var cancellation = new CancellationTokenSource();
        var first = reader.ReadAsync(PathToStatus, cancellation.Token);
        var second = reader.ReadAsync(PathToStatus, default);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        completion.SetResult(new(HttpStatusCode.OK));
        Assert.Equal(200, (await second).StatusCode); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task ConcurrentMissingStateSharesProbeAndRefreshesAfterShortCache()
    {
        var clock = new Clock(); var handler = new Probe(HttpStatusCode.OK);
        using var reader = new MaintenanceStatusReader(new HttpClient(handler), clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => reader.ReadAsync(PathToStatus, default)));
        Assert.All(results, result => Assert.Equal(200, result.StatusCode)); Assert.Equal(1, handler.Calls);
        clock.Advance(); await reader.ReadAsync(PathToStatus, default); Assert.Equal(2, handler.Calls);
    }
    private static MaintenanceStatusReader Reader(Probe handler) => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) }, TimeProvider.System);
    public void Dispose() { Directory.Delete(_directory, true); GC.SuppressFinalize(this); }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(3);
    }
    private sealed class Probe(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls; public string? Address; public string[]? Headers; public Exception? Failure; public Task<HttpResponseMessage>? Pending;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls); Address = request.RequestUri!.AbsoluteUri;
            Headers = request.Headers.Select(header => header.Key).ToArray();
            return Pending ?? (Failure is null ? Task.FromResult(new HttpResponseMessage(status)) : Task.FromException<HttpResponseMessage>(Failure));
        }
    }
}
