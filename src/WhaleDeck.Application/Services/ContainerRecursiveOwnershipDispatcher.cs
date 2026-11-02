using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecursiveOwnershipDispatcher(IContainerRecursiveOwnershipRepository repository, IContainerRecursiveOwnershipGateway gateway,
    ContainerRecursiveOwnershipWorkflowService authorization, IContainerRecursiveOwnershipProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerRecursiveOwnershipWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_OWNERSHIP_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_OWNERSHIP_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_OWNERSHIP_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_OWNERSHIP_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerRecursiveOwnershipWorkflowAsync(cancellationToken);
                    var plan = await Guarded(row, true, token => gateway.PlanContainerRecursiveOwnershipAsync(Actor(row), ContainerRecursiveOwnershipWorkflowValidation.ReadCommand(row), token), cancellationToken);
                    ContainerRecursiveOwnershipValidation.Plan(plan);
                    if (plan.Command != ContainerRecursiveOwnershipWorkflowValidation.ReadCommand(row)) throw ContainerRecursiveOwnershipValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_RECURSIVE_OWNERSHIP_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerRecursiveOwnershipWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerRecursiveOwnershipAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerRecursiveOwnershipAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerRecursiveOwnershipWorkflowAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerRecursiveOwnershipAsync(operation, token), cancellationToken));

            async Task Finish(ContainerRecursiveOwnershipReceiptDto receipt)
            {
                ContainerRecursiveOwnershipWorkflowValidation.RequireCompatibleReceipt(row, receipt);
                // Recording an already verified Root outcome is observe-only.
                // Identity/cache outages cannot erase Partial/Unknown counts or
                // turn a known Aborted fence into a missing/unknown result.
                var canceled = false;
                if (receipt.State == "Aborted")
                {
                    canceled = await Canceled(row, cancellationToken);
                    if (!canceled)
                    {
                        try { canceled = !await Allowed(row, cancellationToken); }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
                    }
                }
                var state = receipt.State == "Completed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" : receipt.State == "Partial" ? "Partial" : receipt.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
                var code = state switch { "Succeeded" => "CONTAINER_RECURSIVE_OWNERSHIP_COMPLETED", "Superseded" => "CONTAINER_RECURSIVE_OWNERSHIP_TARGET_CHANGED",
                    "Partial" => "CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL_REQUIRES_ACKNOWLEDGEMENT", "Unknown" => "CONTAINER_RECURSIVE_OWNERSHIP_RESULT_UNKNOWN", "Canceled" => "CONTAINER_RECURSIVE_OWNERSHIP_CANCELED", _ => "CONTAINER_RECURSIVE_OWNERSHIP_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (ContainerRecursiveOwnershipPreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_RECURSIVE_OWNERSHIP_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_OWNERSHIP_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_OWNERSHIP_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no recursive ownership change happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_OWNERSHIP_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerRecursiveOwnershipIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private Task<bool> Canceled(ContainerRecursiveOwnershipIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerRecursiveOwnershipIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerRecursiveOwnershipIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(335));
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
