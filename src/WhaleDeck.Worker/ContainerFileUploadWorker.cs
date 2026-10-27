using WhaleDeck.Application.Services;

namespace WhaleDeck.Worker;

/// <summary>Independent of metrics ingestion and generic Outbox dispatch; one publication at a time.</summary>
public sealed partial class ContainerFileUploadWorker(IServiceScopeFactory scopes, ILogger<ContainerFileUploadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); var nextWarning = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ContainerFileUploadDispatcher>().TickAsync(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { if (DateTimeOffset.UtcNow >= nextWarning) { Deferred(logger); nextWarning = DateTimeOffset.UtcNow.AddMinutes(1); } }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Container upload deferred with code CONTAINER_UPLOAD_WORKER_UNAVAILABLE")]
    private static partial void Deferred(ILogger logger);
}
