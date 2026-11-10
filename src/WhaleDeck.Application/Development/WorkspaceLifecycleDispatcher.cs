namespace WhaleDeck.Application.Development;

public sealed class WorkspaceLifecycleDispatcher(IWorkspaceLifecycleStore store, WorkspaceLifecycle access, IWorkspaceLifecycleGateway gateway)
{
    public async Task TickAsync(CancellationToken token)
    {
        var claim = await store.ClaimAsync(token);
        if (claim is null) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await access.RequireExecutionAsync(claim, lifetime.Token);
            var proof = await gateway.ApplyAsync(claim.Actor, claim.Workspace, claim.Plan, claim.View.OperationId, lifetime.Token);
            await access.RequireExecutionAsync(claim, lifetime.Token);
            await store.CompleteAsync(claim, proof, lifetime.Token);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException)
        { await store.DeferAsync(claim, "WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED", token); }
        catch (WorkspaceLifecycleException error)
        { await store.DeferAsync(claim, "WORKSPACE_LIFECYCLE_AGENT_" + error.Failure.ToString().ToUpperInvariant(), token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { await store.DeferAsync(claim, "WORKSPACE_LIFECYCLE_TIMEOUT", token); }
    }
}
