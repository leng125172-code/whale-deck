using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

public sealed partial class NotificationPrivateWorker(IServiceScopeFactory scopes, ILogger<NotificationPrivateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<NotificationPrivateDispatcher>().TickAsync(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { if (DateTimeOffset.UtcNow >= nextWarning) { Failed(logger); nextWarning = DateTimeOffset.UtcNow.AddMinutes(1); } }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification private workflow deferred with code PRIVATE_WORKER_UNAVAILABLE")]
    private static partial void Failed(ILogger logger);
}
