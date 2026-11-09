using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

public sealed partial class ContainerRecycleArchiveRetirementWorker(IServiceScopeFactory scopes, ILogger<ContainerRecycleArchiveRetirementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ContainerRecycleArchiveRetirementDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow < nextWarning) continue;
                Deferred(logger);
                nextWarning = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archive retirement deferred with code CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
