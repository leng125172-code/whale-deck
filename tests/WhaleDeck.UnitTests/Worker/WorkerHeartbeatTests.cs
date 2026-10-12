using System.Globalization;
using WhaleDeck.Worker;

namespace WhaleDeck.UnitTests.Worker;

public sealed class WorkerHeartbeatTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "whaledeck-worker-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportCreatesAFreshAtomicHeartbeat()
    {
        var path = Path.Combine(_directory, "worker.heartbeat");

        WorkerHeartbeat.Report(path);

        Assert.True(WorkerHeartbeat.IsFresh(path, TimeSpan.FromSeconds(45), DateTimeOffset.UtcNow));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void RejectsMissingMalformedStaleAndFutureHeartbeats()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "worker.heartbeat");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        Assert.False(WorkerHeartbeat.IsFresh(path, TimeSpan.FromSeconds(45), now));

        File.WriteAllText(path, "not-a-timestamp");
        Assert.False(WorkerHeartbeat.IsFresh(path, TimeSpan.FromSeconds(45), now));

        File.WriteAllText(path, now.AddSeconds(-46).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        Assert.False(WorkerHeartbeat.IsFresh(path, TimeSpan.FromSeconds(45), now));

        File.WriteAllText(path, now.AddSeconds(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        Assert.False(WorkerHeartbeat.IsFresh(path, TimeSpan.FromSeconds(45), now));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
