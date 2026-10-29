using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerMoveDispatcher(IContainerMoveRepository repository, IContainerMoveGateway gateway,
    ContainerMoveWorkflowService authorization, IContainerMoveProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerMoveWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_MOVE_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_MOVE_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_MOVE_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_MOVE_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerMoveWorkflowAsync(cancellationToken);
                    var plan = await Guarded(row, true, token => gateway.PlanContainerMoveAsync(Actor(row), ContainerMoveWorkflowValidation.ReadCommand(row), token), cancellationToken);
                    ContainerMoveValidation.Plan(plan);
                    if (plan.Command != ContainerMoveWorkflowValidation.ReadCommand(row)) throw ContainerMoveValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_MOVE_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerMoveWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerMoveAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerMoveAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerMoveWorkflowAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerMoveAsync(operation, token), cancellationToken));

            async Task Finish(ContainerMoveReceiptDto receipt)
            {
                ContainerMoveValidation.Receipt(operation, receipt);
                var canceled = receipt.State != "Completed" && (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken));
                var state = receipt.State == "Completed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" : receipt.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
                var code = state switch { "Succeeded" => "CONTAINER_MOVE_COMPLETED", "Superseded" => "CONTAINER_MOVE_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_MOVE_RESULT_UNKNOWN", "Canceled" => "CONTAINER_MOVE_CANCELED", _ => "CONTAINER_MOVE_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_MOVE_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_MOVE_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_MOVE_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no publication happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_MOVE_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerMoveIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private Task<bool> Canceled(ContainerMoveIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerMoveIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerMoveIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        await Guard(); var call = action(lifetime.Token);
        try
        {
            while (!call.IsCompleted)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var delay = Task.Delay(TimeSpan.FromSeconds(6), clock, wait.Token);
                if (await Task.WhenAny(call, delay) == call) { await wait.CancelAsync(); break; }
                await delay; await Guard();
            }
            var result = await call; await Guard(); return result;
        }
        finally { await lifetime.CancelAsync(); try { await call; } catch (Exception) { } }
        async Task Guard()
        {
            using var check = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); check.CancelAfter(TimeSpan.FromSeconds(10));
            var expiry = row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc;
            if (active && (expiry <= clock.GetUtcNow() || await Canceled(row, check.Token) || !await Allowed(row, check.Token)) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), check.Token)) throw new FileTransferRejectedException();
        }
    }
}
