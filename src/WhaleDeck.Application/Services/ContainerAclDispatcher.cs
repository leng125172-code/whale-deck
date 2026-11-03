using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class ContainerAclDispatcher(IContainerAclRepository repository, IContainerAclGateway gateway,
    ContainerAclWorkflowService authorization, IContainerAclProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!ContainerAclWorkflowValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_ACL_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "CONTAINER_ACL_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_ACL_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "CONTAINER_ACL_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    if (row.RestoreSourceJson is not null)
                        await Guarded(row, true, async token => { await authorization.ValidateRestoreSourceAsync(Actor(row), row, token); return true; }, cancellationToken);
                    await gateway.RequireContainerAclWorkflowAsync(cancellationToken);
                    var observedPlan = await Guarded(row, true, token => gateway.PlanContainerAclAsync(Actor(row), ContainerAclWorkflowValidation.ReadCommand(row), token), cancellationToken);
                    ContainerAclPlanDto plan;
                    try { plan = ContainerAclWorkflowValidation.SnapshotPlan(observedPlan); }
                    catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
                    { throw ContainerAclValidation.Invalid(); }
                    if (!ContainerAclValidation.SameCommand(plan.Command, ContainerAclWorkflowValidation.ReadCommand(row))) throw ContainerAclValidation.Invalid();
                    if (ContainerAclRestoreValidation.Read(row) is {} source && source.Entry.Kind != plan.Kind) throw new ContainerAclRestoreSourceChangedException();
                    await repository.FinishPlanningAsync(row, plan, "CONTAINER_ACL_READY", cancellationToken);
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var operation = ContainerAclWorkflowValidation.Reference(row);
            var observed = await Guarded(row, false, token => gateway.InspectContainerAclAsync(operation, token), cancellationToken);
            if (observed is not null) { await Finish(observed); return true; }
            if (await Canceled(row, cancellationToken) || !await Allowed(row, cancellationToken) || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardContainerAclAsync(operation, token), cancellationToken)); return true; }
            try { if (row.RestoreSourceJson is not null) await Guarded(row, true, async token => { await authorization.ValidateRestoreSourceAsync(Actor(row), row, token); return true; }, cancellationToken); }
            catch (ContainerAclRestoreSourceChangedException)
            {
                await Finish(await Guarded(row, false, token => gateway.DiscardContainerAclAsync(operation, token), cancellationToken), "CONTAINER_ACL_RESTORE_SOURCE_CHANGED");
                return true;
            }
            await gateway.RequireContainerAclWorkflowAsync(cancellationToken);
            if (await repository.StartedAsync(row, clock.GetUtcNow(), cancellationToken) != ContainerFileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyContainerAclAsync(operation, token), cancellationToken));

            async Task Finish(ContainerAclReceiptDto receipt, string? abortCode = null)
            {
                ContainerAclWorkflowValidation.RequireCompatibleReceipt(row, receipt);
                // Observe-only reconciliation must retain trusted Unknown/Aborted facts during identity outages.
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
                var code = state switch { "Succeeded" => "CONTAINER_ACL_COMPLETED", "Superseded" => "CONTAINER_ACL_TARGET_CHANGED",
                    "Unknown" => "CONTAINER_ACL_RESULT_UNKNOWN", "Canceled" => "CONTAINER_ACL_CANCELED", _ => "CONTAINER_ACL_INTERRUPTED" };
                await repository.FinishAsync(row, state, receipt.State == "Aborted" && abortCode is not null ? abortCode : code, receipt, cancellationToken);
            }
        }
        catch (ContainerAclRestoreSourceChangedException) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, "CONTAINER_ACL_RESTORE_SOURCE_CHANGED", cancellationToken); }
        catch (ContainerAclPreflightRejectedException error) when (row.ExecutionJobId is null)
        { await repository.FinishPlanningAsync(row, null, error.Code, cancellationToken); }
        catch (InvalidOperationException error) when (error.Message is "CONTAINER_ACL_OUTPUT_INVALID" or "CONTAINER_ACL_FACT_CHANGED")
        {
            if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "CONTAINER_ACL_OUTPUT_INVALID", cancellationToken);
            else await repository.FinishAsync(row, "Unknown", "CONTAINER_ACL_OUTPUT_INVALID", null, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A missing/invalid reply never proves that no ACL change happened.
            if (row.ExecutionJobId is not null && row.ExecutionExpiresAtUtc <= clock.GetUtcNow())
                await repository.FinishAsync(row, "Unknown", "CONTAINER_ACL_RESULT_UNKNOWN", null, cancellationToken);
            else await repository.DeferAsync(row, cancellationToken);
        }
        return true;
    }
    private static TerminalActor Actor(ContainerAclIntent row) => new(row.ActorSubject, null, [], row.LoginBinding, row.TraceId, null);
    private Task<bool> Canceled(ContainerAclIntent row, CancellationToken token) => repository.CancellationRequestedAsync(row.Id, token);
    private async Task<bool> Allowed(ContainerAclIntent row, CancellationToken token)
    { try { await authorization.AuthorizeAsync(Actor(row), token); return true; } catch (UnauthorizedAccessException) { return false; } }
    private async Task<T> Guarded<T>(ContainerAclIntent row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
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
