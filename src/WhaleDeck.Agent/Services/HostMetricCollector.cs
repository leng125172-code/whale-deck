namespace WhaleDeck.Agent.Services;

/// <summary>Owns the canonical six-second host sampling cadence; RPC readers cannot change baselines.</summary>
public sealed partial class HostMetricCollector(HostReader host, ILogger<HostMetricCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextLog = DateTimeOffset.MinValue;
        var initial = true;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        do
        {
            try
            {
                if (initial) host.SampleMetrics(); else host.CollectMetrics();
                initial = false;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextLog)
                {
                    LogSamplingFailure(logger, exception);
                    nextLog = DateTimeOffset.UtcNow.AddMinutes(1);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 4012, Level = LogLevel.Warning, Message = "Host metric sampling failed; the next scheduled batch will retry")]
    private static partial void LogSamplingFailure(ILogger logger, Exception exception);
}
