using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class OperationGrpcService(OperationStore store, OperationCoordinator coordinator) : OperationService.OperationServiceBase
{
    public override Task<OperationHandle> GetOperation(OperationRequest request, ServerCallContext context) =>
        Task.FromResult(store.Get(request.OperationId) ?? throw new RpcException(new Status(StatusCode.NotFound, "Operation was not found.")));

    public override Task<OperationHandle> GetMaintenanceStatus(Empty request, ServerCallContext context) =>
        Task.FromResult(store.GetMaintenance());

    public override Task<OperationHandle> CancelOperation(OperationRequest request, ServerCallContext context)
    {
        var operation = store.Get(request.OperationId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Operation was not found."));
        if (operation.State == OperationState.Canceled) return Task.FromResult(operation);
        if (operation.State is OperationState.Succeeded or OperationState.Failed or OperationState.RolledBack)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Completed operations cannot be canceled."));
        }
        if (!coordinator.RequestCancellation(operation.OperationId))
        {
            if (operation.State is OperationState.Succeeded or OperationState.Failed or OperationState.RolledBack or OperationState.Canceled)
                return Task.FromResult(operation);
            operation.State = OperationState.Failed;
            operation.Phase = "ExecutorUnavailable";
            operation.ErrorCode = "AGENT_OPERATION_INTERRUPTED";
            store.Save(operation);
        }
        else if (operation.State is OperationState.Queued or OperationState.Running or OperationState.WaitingForHealth or OperationState.RollingBack)
        {
            operation.Phase = "CancellationRequested";
            store.Save(operation);
        }
        return Task.FromResult(operation);
    }

    public override async Task WatchOperation(OperationRequest request, IServerStreamWriter<OperationHandle> responseStream, ServerCallContext context)
    {
        OperationState? previous = null;
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var operation = store.Get(request.OperationId)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "Operation was not found."));
            if (operation.State != previous)
            {
                await responseStream.WriteAsync(operation);
                previous = operation.State;
            }
            if (operation.State is OperationState.Succeeded or OperationState.Failed or OperationState.RolledBack or OperationState.Canceled)
            {
                return;
            }
            await Task.Delay(500, context.CancellationToken);
        }
    }
}
