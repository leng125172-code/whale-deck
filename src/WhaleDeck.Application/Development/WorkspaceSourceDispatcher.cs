namespace WhaleDeck.Application.Development;

public sealed class WorkspaceSourceDispatcher(IWorkspaceSourceStore store, WorkspaceSources access, IWorkspaceSourceGateway gateway)
{
    public async Task TickAsync(CancellationToken token)
    {
        var claim = await store.ClaimAsync(token);
        if (claim is null) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(118));
        var work = lifetime.Token;
        try
        {
            switch (claim.Import.State)
            {
                case WorkspaceSources.Ready:
                    await StageAsync(claim, work);
                    break;
                case WorkspaceSources.Publishing:
                    await PublishAsync(claim, work);
                    break;
                case WorkspaceSources.Canceling:
                    await CancelAsync(claim, work);
                    break;
                default:
                    throw new InvalidDataException("WORKSPACE_SOURCE_CLAIM_INVALID");
            }
        }
        catch (WorkspaceSourceException error) when (claim.Import.State != WorkspaceSources.Canceling &&
            error.Failure is WorkspaceSourceFailure.Rejected or WorkspaceSourceFailure.Conflict)
        {
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null,
                error.Failure == WorkspaceSourceFailure.Conflict ? "WORKSPACE_SOURCE_HOST_CONFLICT" : "WORKSPACE_SOURCE_ARCHIVE_REJECTED", token);
        }
        catch (WorkspaceSourceException error)
        {
            await store.DeferAsync(claim, error.Message, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await store.DeferAsync(claim, "WORKSPACE_SOURCE_TIMEOUT", token);
        }
    }

    private async Task StageAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        if (!await AuthorizedAsync(claim, token))
        {
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);
            return;
        }
        var observed = await InspectAsync(claim, token);
        if (observed is null)
        {
            if (!await AuthorizedAsync(claim, token))
            {
                await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);
                return;
            }
            if (!await store.IsCurrentAsync(claim, token)) { await store.DeferAsync(claim, null, token); return; }
            observed = await gateway.StageSourceAsync(claim, store.ReadChunksAsync(claim, token), token);
        }
        if (observed.State != WorkspaceSources.Staged) throw Integrity();
        await store.AdvanceAsync(claim, WorkspaceSources.Staged, observed.Plan, null, token);
    }

    private async Task PublishAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        var expected = claim.Import.Plan ?? throw Integrity();
        var observed = await InspectAsync(claim, token) ?? throw Integrity();
        if (observed.Plan != expected) throw Integrity();
        if (observed.State == WorkspaceSources.Published)
        {
            await store.AdvanceAsync(claim, WorkspaceSources.Published, expected, null, token);
            return;
        }
        if (observed.State is not (WorkspaceSources.Staged or "PublishUnconfirmed")) throw Integrity();
        if (!await AuthorizedAsync(claim, token))
        {
            if (observed.State == "PublishUnconfirmed") throw Integrity();
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, expected, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);
            return;
        }
        if (!await store.IsCurrentAsync(claim, token)) { await store.DeferAsync(claim, null, token); return; }
        var result = await gateway.PublishSourceAsync(claim, expected, token);
        if (result.State != WorkspaceSources.Published || result.Plan != expected) throw Integrity();
        await store.AdvanceAsync(claim, WorkspaceSources.Published, expected, null, token);
    }

    private async Task CancelAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        var observed = await InspectAsync(claim, token);
        var expected = claim.Import.Plan;
        if (observed is null)
        {
            if (expected is not null) throw Integrity();
            await gateway.AbortSourceAsync(claim, token);
        }
        else
        {
            if (expected is not null && observed.Plan != expected) throw Integrity();
            if (observed.State is WorkspaceSources.Staged or "DiscardUnconfirmed")
                observed = await gateway.DiscardSourceAsync(claim, observed.Plan, token);
            if (observed.State != "Discarded") throw Integrity();
        }
        var failed = claim.Import.Code is "WORKSPACE_SOURCE_HOST_CONFLICT" or "WORKSPACE_SOURCE_ARCHIVE_REJECTED";
        await store.AdvanceAsync(claim, failed ? WorkspaceSources.Failed : WorkspaceSources.Canceled, expected, claim.Import.Code, token);
    }

    private async Task<WorkspaceSourceObservation?> InspectAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        try { return await gateway.InspectSourceAsync(claim, token); }
        catch (WorkspaceSourceException error) when (error.Failure == WorkspaceSourceFailure.Missing) { return null; }
    }

    private async Task<bool> AuthorizedAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        try
        {
            var live = await access.RequireAsync(claim.Actor, claim.Workspace.Id, claim.Import.ImportId, token);
            return live.Version == claim.Workspace.Version && live.StorageRootId == claim.Workspace.StorageRootId &&
                live.EnvironmentSha256 == claim.Workspace.EnvironmentSha256 && live.Budget == claim.Workspace.Budget;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException ||
            error is InvalidOperationException { Message: "WORKSPACE_SOURCE_STORAGE_REQUIRED" }) { return false; }
    }

    private static WorkspaceSourceException Integrity() => new(WorkspaceSourceFailure.Integrity);
}
