using System.Collections.Concurrent;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class OperationCoordinator(OperationStore store, ILogger<OperationCoordinator> logger)
{
    private static readonly Action<ILogger, string, string, Exception?> LogFailure = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(2101, "AgentOperationFailed"),
        "Agent operation {OperationId} failed with code {ErrorCode}");

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    public void Start(
        OperationHandle operation,
        string errorCode,
        Func<CancellationToken, Task<string?>> action,
        string? maintenanceMessage = null,
        bool completionWinsCancellation = false)
    {
        var cancellation = new CancellationTokenSource();
        if (!_running.TryAdd(operation.OperationId, cancellation))
            throw new InvalidOperationException("The operation is already running.");

        operation.State = OperationState.Queued;
        operation.Phase = "Queued";
        operation.ProgressPercent = 0;
        store.Save(operation, maintenanceMessage);

        _ = Task.Run(async () =>
        {
            try
            {
                operation.State = OperationState.Running;
                operation.Phase = "Executing";
                operation.ProgressPercent = 20;
                store.Save(operation, maintenanceMessage);
                operation.ResultJson = await action(cancellation.Token) ?? string.Empty;
                if (!completionWinsCancellation) cancellation.Token.ThrowIfCancellationRequested();
                operation.State = OperationState.Succeeded;
                operation.Phase = "Completed";
                operation.ProgressPercent = 100;
                operation.ErrorCode = string.Empty;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                operation.State = OperationState.Canceled;
                operation.Phase = "Canceled";
                operation.ErrorCode = string.Empty;
            }
            catch (ApplicationOperationException exception)
            {
                operation.ResultJson = exception.ResultJson;
                operation.State = OperationState.Failed;
                operation.Phase = "RolledBack";
                operation.ErrorCode = errorCode;
                LogFailure(logger, operation.OperationId, errorCode, exception);
            }
            catch (Exception exception)
            {
                operation.State = OperationState.Failed;
                operation.Phase = "Failed";
                operation.ErrorCode = errorCode;
                LogFailure(logger, operation.OperationId, errorCode, exception);
            }
            finally
            {
                store.Save(operation, maintenanceMessage);
                if (_running.TryRemove(operation.OperationId, out var source)) source.Dispose();
            }
        });
    }

    public bool RequestCancellation(string operationId)
    {
        if (!_running.TryGetValue(operationId, out var cancellation)) return false;
        try { cancellation.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }
}
