using WhaleDeck.Application.Development;

namespace WhaleDeck.Worker;

public sealed partial class WorkspaceRunWorker(IServiceScopeFactory scopes, ILogger<WorkspaceRunWorker> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<WorkspaceRunDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow < nextWarning) continue;
                Deferred(logger);
                nextWarning = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workspace runner deferred with code WORKSPACE_RUN_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
