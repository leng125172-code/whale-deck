using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerRecursiveAclRestoreDispatcher(IContainerRecursiveAclRestoreRepository repository, IContainerRecursiveAclRestoreGateway gateway,
    ContainerRecursiveAclRestoreWorkflowService authorization, IContainerRecursiveAclRestoreProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_ACL_RESTORE_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_ACL_RESTORE_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    await gateway.RequireContainerRecursiveAclRestoreWorkflowAsync(cancellationToken);
                    var plan = await Guarded(row, true, async token => ContainerRecursiveAclRestoreWorkflowValidation.SnapshotPlan(await gateway.PlanContainerRecursiveAclRestoreAsync(Actor(row), ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row), token)), cancellationToken);
                    ContainerRecursiveAclRestoreValidation.Plan(plan);
                    if (!ContainerRecursiveAclRestoreValidation.SameCommand(plan.Command, ContainerRecursiveAclRestoreWorkflowValidation.ReadCommand(row))) throw ContainerRecursiveAclRestoreValidation.Invalid();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_RECURSIVE_ACL_RESTORE_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerRecursiveAclRestoreWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerRecursiveAclRestoreAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerRecursiveAclRestoreAsync(operation, token), cancellationToken)); return true; }
            await gateway.RequireContainerRecursiveAclRestoreWorkflowAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            try { await Finish(await Guarded(row, true, token => gateway.ApplyContainerRecursiveAclRestoreAsync(operation, token), cancellationToken)); }
            catch (ContainerRecursiveAclRestorePreflightRejectedException error) when (error.IsSourceRejection)
            {
                // A source fence can fail after a child changed. Inspect the
                // ORIGINAL execution before choosing an Aborted fence; never
                // infer zero writes from the exception and never Apply again.
                var receipt = await Guarded(row, false, token => gateway.InspectContainerRecursiveAclRestoreAsync(operation, token), cancellationToken)
                    ?? await Guarded(row, false, token => gateway.DiscardContainerRecursiveAclRestoreAsync(operation, token), cancellationToken);
                await Finish(receipt, error.Code);
            }

            async Task Finish(ContainerRecursiveAclRestoreReceiptDto receipt, string? refusalCode = null)
            {
                ContainerRecursiveAclRestoreWorkflowValidation.RequireCompatibleReceipt(row, receipt);
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
                var code = state switch { "Succeeded" => "CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED", "Superseded" => "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED",
                    "Partial" => "CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_REQUIRES_ACKNOWLEDGEMENT", "Unknown" => "CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN", "Canceled" => "CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED", _ => refusalCode ?? "CONTAINER_RECURSIVE_ACL_RESTORE_INTERRUPTED" };
                await repository.FinishAsync(row, state, code, receipt, cancellationToken);
            }
        }
        catch (ContainerRecursiveAclRestorePreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message == "CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_ACL_RESTORE_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no recursive ACL change happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerRecursiveAclRestoreIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private Task<bool> Canceled(ContainerRecursiveAclRestoreIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerRecursiveAclRestoreIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerRecursiveAclRestoreIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
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
