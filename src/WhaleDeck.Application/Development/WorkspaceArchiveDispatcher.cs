namespace WhaleDeck.Application.Development;

public sealed class WorkspaceArchiveDispatcher(IWorkspaceArchiveStore store, WorkspaceArchives access, IWorkspaceArchiveGateway gateway)
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
            WorkspaceArchiveTaskProof proof;
            if (claim.View.CancelRequested)
                proof = await gateway.CancelArchiveAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
            else
            {
                await access.RequireExecutionAsync(claim, lifetime.Token);
                // This committed fence is consumed once. Lease takeover and a
                // lost reply only observe this exact task, never start it again.
                var dispatch = await store.MarkDispatchAsync(claim, lifetime.Token);
                starting = dispatch;
                proof = dispatch
                    ? await gateway.StartArchiveAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token)
                    : await gateway.ObserveArchiveAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
                await access.RequireExecutionAsync(claim, lifetime.Token);
            }
            await store.RecordAsync(claim, proof, lifetime.Token);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException)
        {
            if (await store.AuthorizationLostAsync(claim, token))
            {
                // Revocation cleanup uses only the persisted original task.
                // It never authorizes a new capture, removal or retry.
                try
                {
                    var proof = await gateway.CancelArchiveAsync(claim.Actor, claim.Workspace, claim.Task, lifetime.Token);
                    await store.RecordAsync(claim, proof, lifetime.Token);
                }
                catch (WorkspaceArchiveException) { await store.DeferAsync(claim, "WORKSPACE_ARCHIVE_STOP_UNCONFIRMED", token); }
            }
        }
        catch (WorkspaceArchiveException error)
        {
            var code = "WORKSPACE_ARCHIVE_AGENT_" + error.Failure.ToString().ToUpperInvariant();
            if (starting && error.StartWasNotDispatched)
                await store.DeferUndispatchedAsync(claim, code, token);
            else await store.DeferAsync(claim, code, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { await store.DeferAsync(claim, "WORKSPACE_ARCHIVE_TIMEOUT", token); }
    }
}
