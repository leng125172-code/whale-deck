namespace WhaleDeck.Application.Development;

public sealed class WorkspaceRetirementDispatcher(IWorkspaceRetirementStore store, WorkspaceRetirements access, IWorkspaceRetirementGateway gateway)
{
    public async Task TickAsync(CancellationToken token)
    {
        var claim = await store.ClaimAsync(token);
        if (claim is null) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        var starting = false;
        try
        {
            WorkspaceRetirementTaskProof proof;
            if (claim.View.CancelRequested)
                proof = await gateway.CancelRetirementAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
            else
            {
                await access.RequireExecutionAsync(claim, lifetime.Token);
                // A committed dispatch can only be observed after lease takeover
                // or a lost reply. Destructive execution is never started twice.
                starting = await store.MarkDispatchAsync(claim, lifetime.Token);
                proof = starting
                    ? await gateway.StartRetirementAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token)
                    : await gateway.ObserveRetirementAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
                await access.RequireExecutionAsync(claim, lifetime.Token);
            }
            await store.RecordAsync(claim, proof, lifetime.Token);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException)
        {
            if (await store.AuthorizationLostAsync(claim, token))
            {
                // Revocation may stop only the persisted original task; it
                // does not authorize a new plan, deletion or recovery attempt.
                try
                {
                    var proof = await gateway.CancelRetirementAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
                    await store.RecordAsync(claim, proof, lifetime.Token);
                }
                catch (WorkspaceRetirementException)
                { await store.DeferAsync(claim, "WORKSPACE_RETIREMENT_STOP_UNCONFIRMED", token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { await store.DeferAsync(claim, "WORKSPACE_RETIREMENT_STOP_UNCONFIRMED", token); }
            }
        }
        catch (WorkspaceRetirementException error)
        {
            var code = "WORKSPACE_RETIREMENT_AGENT_" + error.Failure.ToString().ToUpperInvariant();
            if (starting && error.StartWasNotDispatched)
                await store.DeferUndispatchedAsync(claim, code, token);
            else await store.DeferAsync(claim, code, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { await store.DeferAsync(claim, "WORKSPACE_RETIREMENT_TIMEOUT", token); }
    }
}
