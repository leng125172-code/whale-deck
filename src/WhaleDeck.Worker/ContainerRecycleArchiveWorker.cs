using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

public sealed partial class ContainerRecycleArchiveWorker(IServiceScopeFactory scopes, ILogger<ContainerRecycleArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ContainerRecycleArchiveDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { if (DateTimeOffset.UtcNow >= nextWarning) { Deferred(logger); nextWarning = DateTimeOffset.UtcNow.AddMinutes(1); } }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Container recycle archive deferred with code CONTAINER_RECYCLE_ARCHIVE_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
