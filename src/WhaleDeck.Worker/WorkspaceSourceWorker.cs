using WhaleDeck.Application.Development;

namespace WhaleDeck.Worker;

public sealed partial class WorkspaceSourceWorker(IServiceScopeFactory scopes, ILogger<WorkspaceSourceWorker> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<WorkspaceSourceDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow < nextWarning) continue;
                Deferred(logger);
                nextWarning = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workspace source import deferred with code WORKSPACE_SOURCE_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
