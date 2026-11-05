using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecycleDispatcher(IContainerRecycleRepository repository, IContainerRecycleGateway gateway,
    ContainerRecycleWorkflowService authorization, IContainerRecycleProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerRecycleWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerRecycleAsync(cancellationToken);
                    var plan = await Guarded(row, true, token => ReadPlan(row, token), cancellationToken);
                    ContainerRecycleValidation.Plan(plan);
                    if (plan.Command != ContainerRecycleWorkflowValidation.ReadCommand(row)) throw ContainerRecycleValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_RECYCLE_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerRecycleWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerRecycleAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerRecycleAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerRecycleAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerRecycleAsync(operation, token), cancellationToken));

            async Task Finish(ContainerRecycleReceiptDto receipt)
            {
                ContainerRecycleWorkflowValidation.RequireCompatibleReceipt(row, receipt);
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
                var state = receipt.State == "Trashed" ? receipt.BindingCurrent ? "Succeeded" : "Superseded" :
                    receipt.State == "Restored" ? "Superseded" : receipt.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
                var code = receipt.State == "Restored" ? "CONTAINER_RECYCLE_ALREADY_RESTORED" : state switch {
                    "Succeeded" => "CONTAINER_RECYCLE_TRASHED", "Superseded" => "CONTAINER_RECYCLE_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_RECYCLE_RESULT_UNKNOWN", "Canceled" => "CONTAINER_RECYCLE_CANCELED", _ => "CONTAINER_RECYCLE_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (ContainerRecyclePreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_RECYCLE_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECYCLE_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no removal happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_RECYCLE_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerRecycleIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private async Task<ContainerRecyclePlanDto> ReadPlan(ContainerRecycleIntent row, CancellationToken token)
    {
        var untrusted = await gateway.PlanContainerRecycleAsync(Actor(row), ContainerRecycleWorkflowValidation.ReadCommand(row), token);
        try { return ContainerRecycleWorkflowValidation.SnapshotPlan(untrusted); }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException)
        { throw ContainerRecycleValidation.Invalid(); }
    }
    private Task<bool> Canceled(ContainerRecycleIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerRecycleIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerRecycleIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
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
            if (active && (expiry <= clock.GetUtcNow() || await Canceled(row, check.Token) || !await Allowed(row, check.Token)) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), check.Token)) throw new FileTransferRejectedException();
        }
    }
}
