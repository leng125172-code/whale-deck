using WhaleDeck.Application.Development;

namespace WhaleDeck.Worker;

public sealed partial class WorkspaceArchiveWorker(IServiceScopeFactory scopes, ILogger<WorkspaceArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkspaceArchiveDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow < nextWarning) continue;
                Deferred(logger); nextWarning = DateTimeOffset.UtcNow.AddMinutes(1);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Workspace archive deferred with code WORKSPACE_ARCHIVE_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
