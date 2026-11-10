using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceSourceDiffNodeView(string Kind, long? SizeBytes, bool? Executable);
public sealed record WorkspaceSourceDiffEntryView(string Path, string Change, WorkspaceSourceDiffNodeView? Before,
    WorkspaceSourceDiffNodeView? After);
public sealed record WorkspaceSourceDiffPageView(Guid WorkspaceId, Guid ImportId, Guid PreviousImportId, string PlanSha256,
    int Offset, int? NextOffset, int VisibleEntries, int RedactedEntries, WorkspaceSourceDiffEntryView[] Entries);
public sealed record WorkspaceSourceTextDiffView(Guid WorkspaceId, Guid ImportId, Guid PreviousImportId, string PlanSha256,
    string Path, string Status, string? Patch);

public interface IWorkspaceSourceDiffGateway
{
    Task<WorkspaceSourceDiffPageView> ReadDiffAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, int offset, int limit, CancellationToken token);
    Task<WorkspaceSourceTextDiffView> ReadTextDiffAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, string path, CancellationToken token);
}

public sealed class WorkspaceSourceDiffs(WorkspaceSources sources, IWorkspaceSourceStore store, IWorkspaceSourceDiffGateway gateway)
{
    public Task<WorkspaceSourceDiffPageView> ReadAsync(TerminalActor actor, Guid workspaceId, Guid importId,
        string planSha256, int offset, int limit, Func<Task<TerminalActor>> refreshLogin, CancellationToken token)
    {
        if (!WorkspaceSources.Digest(planSha256) || offset is < 0 or >= 20000 || limit is < 1 or > 50)
            throw new ArgumentException("WORKSPACE_SOURCE_DIFF_QUERY_INVALID");
        return ReadScopedAsync(actor, workspaceId, importId, planSha256, refreshLogin,
            (workspace, import) => gateway.ReadDiffAsync(workspace, import, actor, offset, limit, token), token);
    }

    public Task<WorkspaceSourceTextDiffView> ReadTextAsync(TerminalActor actor, Guid workspaceId, Guid importId,
        string planSha256, string path, Func<Task<TerminalActor>> refreshLogin, CancellationToken token)
    {
        if (!WorkspaceSources.Digest(planSha256) || path is not { Length: > 0 and <= 4096 })
            throw new ArgumentException("WORKSPACE_SOURCE_DIFF_QUERY_INVALID");
        return ReadScopedAsync(actor, workspaceId, importId, planSha256, refreshLogin,
            (workspace, import) => gateway.ReadTextDiffAsync(workspace, import, actor, path, token), token);
    }

    private async Task<T> ReadScopedAsync<T>(TerminalActor actor, Guid workspaceId, Guid importId, string planSha256,
        Func<Task<TerminalActor>> refreshLogin, Func<WorkspaceDefinition, WorkspaceSourceView, Task<T>> read, CancellationToken token)
    {
        var workspace = await sources.RequireAsync(actor, workspaceId, importId, token);
        var import = RequirePlan(await store.FindAsync(workspaceId, importId, actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND"));
        var result = await read(workspace, import);
        var live = await refreshLogin();
        if (live.Subject != actor.Subject || live.LoginBinding != actor.LoginBinding)
            throw new UnauthorizedAccessException("WORKSPACE_SOURCE_LOGIN_CHANGED");
        RequirePlan(await sources.GetAsync(live, workspaceId, importId, token));
        return result;

        WorkspaceSourceView RequirePlan(WorkspaceSourceView item)
        {
            if (item is not { State: WorkspaceSources.Staged, PreviousImportId: not null, Plan.Update: not null } ||
                item.Plan.PlanSha256 != planSha256)
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Conflict);
            return item;
        }
    }
}
