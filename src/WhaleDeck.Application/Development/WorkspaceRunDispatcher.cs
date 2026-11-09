namespace WhaleDeck.Application.Development;

public sealed class WorkspaceRunDispatcher(IWorkspaceRunStore queue, WorkspaceRuns access, IWorkspaceRunnerGateway gateway,
    TimeProvider clock)
{
    public async Task TickAsync(CancellationToken token)
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(token);
        tick.CancelAfter(TimeSpan.FromSeconds(30));
        var claim = await queue.ClaimAsync(tick.Token);
        if (claim is null) return;
        try
        {
            var current = await queue.GetAsync(claim.Run.WorkspaceId, claim.Run.RunId, claim.Actor.Subject, tick.Token)
                ?? throw new InvalidDataException("WORKSPACE_RUN_SCOPE_INVALID");
            var stop = current.CancellationRequested || !await AuthorizedAsync(claim, tick.Token);
            var command = claim.Command;
            WorkspaceRunnerReceipt receipt;
            if (claim.DispatchedAtUtc is null)
            {
                if (stop || clock.GetUtcNow() - claim.Run.CreatedAtUtc >= TimeSpan.FromMinutes(10))
                {
                    await queue.FinishAsync(claim, "Canceled", "CanceledBeforeDispatch", "WORKSPACE_RUN_ADMISSION_ENDED", null, tick.Token);
                    return;
                }
                if (!await queue.EnterDispatchAsync(claim, tick.Token)) return;
                if (!await AuthorizedAsync(claim, tick.Token) ||
                    (await queue.GetAsync(claim.Run.WorkspaceId, claim.Run.RunId, claim.Actor.Subject, tick.Token))?.CancellationRequested != false)
                {
                    await queue.FinishAsync(claim, "Canceled", "CanceledBeforeDispatch", "WORKSPACE_RUN_ADMISSION_ENDED", null, tick.Token);
                    return;
                }

                // The committed fence is never entered again. The fixed Agent
                // dispatch outlives its caller after admission, so the Worker
                // can return to its queue and observe or cancel the same run.
                using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(tick.Token);
                dispatch.CancelAfter(TimeSpan.FromSeconds(6));
                receipt = await gateway.StartAsync(claim.Actor, claim.Workspace, command, dispatch.Token);
            }
            else
            {
                receipt = await RecoverAsync(claim, command, stop, tick.Token);
            }
            await FinishAsync(claim, receipt, tick.Token);
        }
        catch (WorkspaceRunnerException error) when (claim.DispatchedAtUtc is { } dispatched && error.Failure == WorkspaceRunnerFailure.NotFound &&
            clock.GetUtcNow() - dispatched >= TimeSpan.FromSeconds(330 + claim.Command.DeadlineSeconds))
        {
            await queue.FinishAsync(claim, "Failed", "ResultUnknown", "WORKSPACE_RUN_RESULT_UNKNOWN", null, token);
        }
        catch (WorkspaceRunnerException error) when (claim.DispatchedAtUtc is null && error.Failure == WorkspaceRunnerFailure.Unsupported)
        {
            await queue.FinishAsync(claim, "Failed", "AgentUnavailable", "WORKSPACE_RUN_AGENT_CONTROL_REQUIRED", null, token);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is WorkspaceRunnerException or OperationCanceledException)
        {
            await queue.DeferAsync(claim, token);
        }
    }

    private async Task<WorkspaceRunnerReceipt> RecoverAsync(WorkspaceRunClaim claim, WorkspaceRunCommand command, bool stop,
        CancellationToken token)
    {
        if (stop) return await gateway.CancelAsync(claim.Actor, claim.Workspace, command, token);
        try { return await gateway.ObserveAsync(claim.Actor, claim.Workspace, command, token); }
        catch (WorkspaceRunnerException error) when (error.Failure == WorkspaceRunnerFailure.Interrupted)
        {
            return await gateway.ReclaimAsync(claim.Actor, claim.Workspace, command, token);
        }
    }

    private async Task<bool> AuthorizedAsync(WorkspaceRunClaim claim, CancellationToken token)
    {
        try
        {
            var current = await access.RequireAsync(claim.Actor, claim.Workspace.Id, token);
            return current.State == WorkspaceStorageAllocation.Allocated && current.Version == claim.Workspace.Version &&
                current.StorageRootId == claim.Workspace.StorageRootId && current.EnvironmentSha256 == claim.Workspace.EnvironmentSha256 &&
                current.Budget == claim.Workspace.Budget;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException) { return false; }
    }

    private Task FinishAsync(WorkspaceRunClaim claim, WorkspaceRunnerReceipt receipt, CancellationToken token)
    {
        if (receipt.RunId != claim.Run.RunId || receipt.RequestSha256.Length != 64 ||
            !receipt.RequestSha256.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'))
            throw new InvalidDataException("WORKSPACE_RUN_RECEIPT_INVALID");
        return receipt.Status switch
        {
            "Succeeded" when receipt.RuntimeExitCode == 0 =>
                queue.FinishAsync(claim, "Succeeded", "Completed", null, receipt, token),
            "Cancelled" when receipt.RuntimeExitCode is null =>
                queue.FinishAsync(claim, "Canceled", "CanceledAtHost", null, receipt, token),
            "Failed" when receipt.RuntimeExitCode is >= 1 and <= 255 =>
                queue.FinishAsync(claim, "Failed", "HostFailed", "WORKSPACE_RUN_HOST_FAILED", receipt, token),
            "TimedOut" when receipt.RuntimeExitCode is null =>
                queue.FinishAsync(claim, "Failed", "TimedOut", "WORKSPACE_RUN_TIMED_OUT", receipt, token),
            "Interrupted" when receipt.RuntimeExitCode is null =>
                queue.FinishAsync(claim, "Failed", "Interrupted", "WORKSPACE_RUN_INTERRUPTED", receipt, token),
            _ => throw new InvalidDataException("WORKSPACE_RUN_RECEIPT_INVALID")
        };
    }
}
