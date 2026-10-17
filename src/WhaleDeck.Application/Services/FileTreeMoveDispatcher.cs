using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public sealed class FileTreeMoveDispatcher(IFileTreeMoveRepository repository, IFileTreeMoveGateway gateway,
    IIdentityDirectory identity, IFileTreeMoveProtector protector, TimeProvider clock)
{
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        var row = await repository.ClaimAsync(clock.GetUtcNow(), cancellationToken); if (row is null) return false;
        try
        {
            if (!FileTreeMoveValidation.HasValidBinding(row, protector))
            {
                if (row.ExecutionJobId is null) await repository.FinishPlanningAsync(row, null, "FILES_INTENT_BINDING_CHANGED", cancellationToken);
                else await repository.FinishAsync(row, "Unknown", "FILES_INTENT_BINDING_CHANGED", null, cancellationToken);
                return true;
            }
            if (row.ExecutionJobId is null)
            {
                if (await repository.CancellationRequestedAsync(row, cancellationToken) || !await Administrator(row, cancellationToken))
                    await repository.FinishPlanningAsync(row, null, "FILES_CANCELED", cancellationToken);
                else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= clock.GetUtcNow())
                    await repository.FinishPlanningAsync(row, null, "FILES_PLAN_EXPIRED", cancellationToken);
                else if (await repository.MarkPlanningAsync(row, cancellationToken))
                {
                    try
                    {
                        var plan = await Guarded(row, true, token => gateway.PlanTreeMoveAsync(new(row.ActorSubject, null, [], row.LoginBinding, row.Id.ToString("D"), null),
                            FileTreeMoveValidation.ReadCommand(row), token), cancellationToken);
                        FileTreeMoveValidation.Plan(plan);
                        await repository.FinishPlanningAsync(row, plan, "FILES_TREE_READY_FOR_CONFIRMATION", cancellationToken);
                    }
                    catch (FileTreeMoveRejectedException error)
                    { await repository.FinishPlanningAsync(row, null, error.Message, cancellationToken); }
                }
                else await repository.DeferAsync(row, cancellationToken);
                return true;
            }
            var receipt = await Guarded(row, false, token => gateway.InspectTreeMoveAsync(row, token), cancellationToken);
            if (receipt is not null) { await Finish(receipt); return true; }
            var canceled = await repository.CancellationRequestedAsync(row, cancellationToken) || !await Administrator(row, cancellationToken);
            if (canceled || row.ExecutionExpiresAtUtc <= clock.GetUtcNow() || row.RootStartedAtUtc is not null)
            { await Finish(await Guarded(row, false, token => gateway.DiscardTreeMoveAsync(row, token), cancellationToken)); return true; }
            if (await repository.StartedAsync(row, cancellationToken) != FileUploadStartResult.Started)
            { await repository.DeferAsync(row, cancellationToken); return true; }
            await Finish(await Guarded(row, true, token => gateway.ApplyTreeMoveAsync(row, token), cancellationToken));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { await repository.DeferAsync(row, cancellationToken); }
        return true;

        async Task Finish(FileTreeMoveReceipt result)
        {
            FileTreeMoveValidation.Receipt(row.ExecutionJobId!.Value, FileTreeMoveValidation.ReadPlan(row), result);
            var canceled = result.State != "Completed" && (await repository.CancellationRequestedAsync(row, cancellationToken) || !await Administrator(row, cancellationToken));
            var state = result.State == "Completed" ? "Succeeded" : result.State == "Unknown" ? "Unknown" : canceled ? "Canceled" : "Failed";
            await repository.FinishAsync(row, state, state switch { "Succeeded" => "FILES_TREE_MOVE_COMPLETED", "Unknown" => "FILES_RESULT_UNKNOWN",
                "Canceled" => "FILES_CANCELED", _ => "FILES_OPERATION_ABORTED" }, result, cancellationToken);
        }
    }
    private async Task<bool> Administrator(FileTreeMoveOperation row, CancellationToken cancellationToken)
    {
        try
        {
            var user = await identity.ResolveCurrentAsync(row.ActorSubject, null, [], cancellationToken);
            return user.Subject == row.ActorSubject && user.IsAdministrator && user.PermissionExpiresAtUtc > clock.GetUtcNow();
        }
        catch (UnauthorizedAccessException) { return false; }
    }
    private async Task<T> Guarded<T>(FileTreeMoveOperation row, bool active, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        await Guard(); var call = action(lifetime.Token);
        try
        {
            while (!call.IsCompleted)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var interval = Task.Delay(TimeSpan.FromSeconds(6), clock, waiting.Token);
                if (await Task.WhenAny(call, interval) == call) { await waiting.CancelAsync(); break; }
                await interval; await Guard();
            }
            var result = await call;
            // Recheck after the final awaited scan/write, not only before it.
            // A completed write remains a historical receipt even if the next
            // guard cancels; reconciliation will find it, never reapply it.
            await Guard(); return result;
        }
        finally
        {
            await lifetime.CancelAsync();
            try { await call; } catch (Exception) { /* Observe; never expose helper text or assume success. */ }
        }
        async Task Guard()
        {
            using var checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); checking.CancelAfter(TimeSpan.FromSeconds(10));
            var deadline = row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc;
            if (active && (deadline <= clock.GetUtcNow() || await repository.CancellationRequestedAsync(row, checking.Token) || !await Administrator(row, checking.Token)) ||
                !await repository.RenewAsync(row, clock.GetUtcNow(), checking.Token)) throw new FileTransferRejectedException();
        }
    }
}
