using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerOwnershipDispatcher(IContainerOwnershipRepository repository, IContainerOwnershipGateway gateway,
    ContainerOwnershipWorkflowService authorization, IContainerOwnershipProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerOwnershipWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_OWNERSHIP_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_OWNERSHIP_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_OWNERSHIP_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_OWNERSHIP_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerOwnershipWorkflowAsync(cancellationToken);
                    var plan = await Guarded(row, true, token => gateway.PlanContainerOwnershipAsync(Actor(row), ContainerOwnershipWorkflowValidation.ReadCommand(row), token), cancellationToken);
                    ContainerOwnershipValidation.Plan(plan);
                    if (plan.Command != ContainerOwnershipWorkflowValidation.ReadCommand(row)) throw ContainerOwnershipValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_OWNERSHIP_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerOwnershipWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerOwnershipAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerOwnershipAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerOwnershipWorkflowAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerOwnershipAsync(operation, token), cancellationToken));

            async Task Finish(ContainerOwnershipReceiptDto receipt)
            {
                ContainerOwnershipValidation.Receipt(operation, receipt);
                var canceled = receipt.State != "Completed" && (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken));
                var state = receipt.State == "Completed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" : receipt.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
                var code = state switch { "Succeeded" => "CONTAINER_OWNERSHIP_COMPLETED", "Superseded" => "CONTAINER_OWNERSHIP_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_OWNERSHIP_RESULT_UNKNOWN", "Canceled" => "CONTAINER_OWNERSHIP_CANCELED", _ => "CONTAINER_OWNERSHIP_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (ContainerOwnershipPreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_OWNERSHIP_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_OWNERSHIP_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_OWNERSHIP_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no ownership change happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_OWNERSHIP_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerOwnershipIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private Task<bool> Canceled(ContainerOwnershipIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerOwnershipIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerOwnershipIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(65));
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
