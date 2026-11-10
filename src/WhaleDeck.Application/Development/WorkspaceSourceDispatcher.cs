namespace WhaleDeck.Application.Development;

public sealed class WorkspaceSourceDispatcher(IWorkspaceSourceStore store, WorkspaceSources access, IWorkspaceSourceGateway gateway)
{
    public async Task TickAsync(CancellationToken token)
    {
        var claim = await store.ClaimAsync(token);
        if (claim is null) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(claim.Import.PreviousImportId is not null &&
            claim.Import.State == WorkspaceSources.Publishing ? 145 : 118));
        var work = lifetime.Token;
        try
        {
            switch (claim.Import.State)
            {
                case WorkspaceSources.Acquiring:
                    await AcquireAsync(claim, work);
                    break;
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
        catch (UnauthorizedAccessException) when (claim.Import.State == WorkspaceSources.Acquiring)
        {
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);
        }
        catch (WorkspaceSourceException error) when (claim.Import.State == WorkspaceSources.Acquiring &&
            error.Failure is WorkspaceSourceFailure.Rejected or WorkspaceSourceFailure.Conflict or WorkspaceSourceFailure.Integrity)
        {
            var code = error.Failure switch
            {
                WorkspaceSourceFailure.Conflict => "WORKSPACE_SOURCE_GIT_SNAPSHOT_CHANGED",
                WorkspaceSourceFailure.Integrity => "WORKSPACE_SOURCE_GIT_INTEGRITY",
                _ => "WORKSPACE_SOURCE_GIT_REJECTED"
            };
            await store.AdvanceAsync(claim, WorkspaceSources.Failed, null, code, token);
        }
        catch (WorkspaceSourceException error) when (claim.Import.State != WorkspaceSources.Canceling &&
            error.Failure is WorkspaceSourceFailure.Rejected or WorkspaceSourceFailure.Conflict)
        {
            var code = error.Failure != WorkspaceSourceFailure.Conflict ? "WORKSPACE_SOURCE_ARCHIVE_REJECTED" :
                error.HostCode is "WORKSPACE_SOURCE_WORK_BASE_CONFLICT" or "WORKSPACE_SOURCE_WORK_CHANGED" or "WORKSPACE_SOURCE_WORK_DIRTY"
                    ? error.HostCode : "WORKSPACE_SOURCE_HOST_CONFLICT";
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, code, token);
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

    private async Task AcquireAsync(WorkspaceSourceClaim claim, CancellationToken token)
    {
        if (!await AuthorizedAsync(claim, token))
        {
            await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);
            return;
        }
        using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var database = new SemaphoreSlim(1);
        var watch = WatchAsync();
        try
        {
            await gateway.AcquireGitAsync(claim,
                (proof, lifetime) => WithDatabaseAsync(async () =>
                {
                    if (!await AuthorizedAsync(claim, lifetime))
                        throw new UnauthorizedAccessException("WORKSPACE_SOURCE_AUTHORIZATION_ENDED");
                    await store.BindGitAsync(claim, proof, lifetime);
                }, lifetime),
                (offset, bytes, lifetime) => WithDatabaseAsync(() => store.AppendGitAsync(claim, offset, bytes, lifetime), lifetime),
                acquisition.Token);
        }
        finally
        {
            await acquisition.CancelAsync();
            try { await watch; }
            catch (OperationCanceledException) when (acquisition.IsCancellationRequested) { }
        }
        if (await AuthorizedAsync(claim, token)) await store.CompleteGitAsync(claim, token);
        else await store.AdvanceAsync(claim, WorkspaceSources.Canceling, null, "WORKSPACE_SOURCE_AUTHORIZATION_ENDED", token);

        async Task WatchAsync()
        {
            using var poll = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await poll.WaitForNextTickAsync(acquisition.Token))
                    await WithDatabaseAsync(async () =>
                    {
                        if (!await store.IsCurrentAsync(claim, acquisition.Token)) await acquisition.CancelAsync();
                    }, acquisition.Token);
            }
            finally { await acquisition.CancelAsync(); }
        }

        // The lease check shares the scoped DbContext with archive writes.
        async Task WithDatabaseAsync(Func<Task> action, CancellationToken lifetime)
        {
            await database.WaitAsync(lifetime);
            try { await action(); }
            finally { database.Release(); }
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
        if (observed.Plan.Update?.PreviousContentSha256 != claim.BaseContentSha256) throw Integrity();
        if (claim.Import.Git?.Snapshot is { } git &&
            (observed.Plan.FileCount != git.FileCount || observed.Plan.TotalBytes != git.TotalBytes)) throw Integrity();
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
        var failed = claim.Import.Code is "WORKSPACE_SOURCE_HOST_CONFLICT" or "WORKSPACE_SOURCE_ARCHIVE_REJECTED" or
            "WORKSPACE_SOURCE_WORK_BASE_CONFLICT" or "WORKSPACE_SOURCE_WORK_CHANGED" or "WORKSPACE_SOURCE_WORK_DIRTY";
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
            return (claim.Import.Git is null || access.RequireGitGrant(claim.Actor, claim.Import.Git.RepositoryId).CredentialRef == claim.GitCredentialRef) &&
                live.Version == claim.Workspace.Version && live.StorageRootId == claim.Workspace.StorageRootId &&
                live.EnvironmentSha256 == claim.Workspace.EnvironmentSha256 && live.Budget == claim.Workspace.Budget;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or KeyNotFoundException ||
            error is InvalidOperationException { Message: "WORKSPACE_SOURCE_STORAGE_REQUIRED" }) { return false; }
    }

    private static WorkspaceSourceException Integrity() => new(WorkspaceSourceFailure.Integrity);
}
