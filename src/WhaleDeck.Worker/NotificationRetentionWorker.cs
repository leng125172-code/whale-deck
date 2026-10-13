using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.Worker;

public sealed partial class NotificationRetentionWorker(IServiceScopeFactory scopes, ILogger<NotificationRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await NotificationRetention.PruneAsync(scope.ServiceProvider.GetRequiredService<PlatformDbContext>(), DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested) { LogRetentionFailure(logger, error); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Notification retention failed; bounded cleanup will retry")]
    private static partial void LogRetentionFailure(ILogger logger, Exception error);
}
