using WhaleDeck.Application.Development;

namespace WhaleDeck.Worker;

public sealed partial class WorkspaceRunReportRetentionWorker(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<WorkspaceRunReportRetentionWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<IWorkspaceRunReportStore>();
                    await store.ExpireAsync(BatchSize, stoppingToken);
                }
                catch (Exception)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    LogCleanupFailure(logger);
                }

                // Delay after each pass so slow or failed batches cannot cause a burst of queries.
                await Task.Delay(Interval, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancels either the database operation or the delay.
        }
    }

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning,
        Message = "Workspace report retention failed; the next scheduled batch will retry")]
    private static partial void LogCleanupFailure(ILogger logger);
}
