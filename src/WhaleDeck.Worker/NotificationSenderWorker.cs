using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

public sealed partial class NotificationSenderWorker(IServiceScopeFactory scopes, ILogger<NotificationSenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var warnAfter = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationSender>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= warnAfter)
                { LogFailure(logger); warnAfter = DateTimeOffset.UtcNow.AddMinutes(1); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "Notification sender unavailable; durable state will recover, private error details withheld")]
    private static partial void LogFailure(ILogger logger);
}
