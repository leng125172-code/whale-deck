using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecycleRestoreDispatcher(IContainerRecycleRestoreRepository repository, IContainerRecycleRestoreGateway gateway,
    ContainerRecycleRestoreWorkflowService authorization, IContainerRecycleRestoreProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerRecycleRestoreWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_RESTORE_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_RESTORE_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_RESTORE_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerRecycleRestoreAsync(cancellationToken);
                    var plan = await Guarded(row, true, token => ReadPlan(row, token), cancellationToken);
                    ContainerRecycleRestoreValidation.Plan(plan);
                    if (!ContainerRecycleRestoreWorkflowValidation.SameCommand(plan.Command, ContainerRecycleRestoreWorkflowValidation.ReadCommand(row))) throw ContainerRecycleRestoreValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_RECYCLE_RESTORE_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerRecycleRestoreWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerRecycleRestoreAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerRecycleRestoreAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerRecycleRestoreAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerRecycleRestoreAsync(operation, token), cancellationToken));

            async Task Finish(ContainerRecycleRestoreReceiptDto receipt)
            {
                ContainerRecycleRestoreWorkflowValidation.RequireCompatibleReceipt(row, receipt);
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
                var state = receipt.State == "Completed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" : receipt.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
                var code = state switch { "Succeeded" => "CONTAINER_RECYCLE_RESTORE_COMPLETED", "Superseded" => "CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN", "Canceled" => "CONTAINER_RECYCLE_RESTORE_CANCELED", _ => "CONTAINER_RECYCLE_RESTORE_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (InvalidOperationException error) when (row.ExecutionJobId is null && error.Message == "CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED")
        { await repository.FinishPlanningAsync(row, null, error.Message, cancellationToken); }
        catch (ContainerRecycleRestorePreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no removal happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerRecycleRestoreIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private async Task<ContainerRecycleRestorePlanDto> ReadPlan(ContainerRecycleRestoreIntent row, CancellationToken token)
    {
        var untrusted = await gateway.PlanContainerRecycleRestoreAsync(Actor(row), ContainerRecycleRestoreWorkflowValidation.ReadCommand(row), token);
        try { return ContainerRecycleRestoreWorkflowValidation.SnapshotPlan(untrusted); }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException)
        { throw ContainerRecycleRestoreValidation.Invalid(); }
    }
    private Task<bool> Canceled(ContainerRecycleRestoreIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerRecycleRestoreIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerRecycleRestoreIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(135));
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
            if (active) await authorization.RequireCurrentSourceAsync(row, Actor(row), check.Token);
            if (active && (expiry <= clock.GetUtcNow() || await Canceled(row, check.Token) || !await Allowed(row, check.Token)) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), check.Token)) throw new FileTransferRejectedException();
        }
    }
}
