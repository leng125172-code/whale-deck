using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.Worker;

/// <summary>Evaluates persisted observations even when Agent collection/dispatch is offline.</summary>
public sealed partial class MonitoringAlertWorker(IServiceScopeFactory scopes, ILogger<MonitoringAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextLog = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await MonitoringAlerts.EvaluateAsync(scope.ServiceProvider.GetRequiredService<PlatformDbContext>(), DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextLog)
                {
                    LogEvaluationFailure(logger, error);
                    nextLog = DateTimeOffset.UtcNow.AddMinutes(1);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Monitoring alert evaluation failed; the independent loop will retry")]
    private static partial void LogEvaluationFailure(ILogger logger, Exception error);
}
