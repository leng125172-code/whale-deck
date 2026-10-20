using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

public sealed partial class FileTreeOverwriteRestoringWorker(IServiceScopeFactory scopes, ILogger<FileTreeOverwriteRestoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FileTreeOverwriteRestoringDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { if (DateTimeOffset.UtcNow >= nextWarning) { Deferred(logger); nextWarning = DateTimeOffset.UtcNow.AddMinutes(1); } }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Directory overwrite restoring workflow deferred with code FILES_TREE_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
