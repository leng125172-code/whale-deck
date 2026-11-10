using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceSourceDiffNodeView(string Kind, long? SizeBytes, bool? Executable);
public sealed record WorkspaceSourceDiffEntryView(string Path, string Change, WorkspaceSourceDiffNodeView? Before,
    WorkspaceSourceDiffNodeView? After);
public sealed record WorkspaceSourceDiffPageView(Guid WorkspaceId, Guid ImportId, Guid PreviousImportId, string PlanSha256,
    int Offset, int? NextOffset, int VisibleEntries, int RedactedEntries, WorkspaceSourceDiffEntryView[] Entries);

public interface IWorkspaceSourceDiffGateway
{
    Task<WorkspaceSourceDiffPageView> ReadDiffAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, int offset, int limit, CancellationToken token);
}

public sealed class WorkspaceSourceDiffs(WorkspaceSources sources, IWorkspaceSourceStore store, IWorkspaceSourceDiffGateway gateway)
{
    public async Task<WorkspaceSourceDiffPageView> ReadAsync(TerminalActor actor, Guid workspaceId, Guid importId,
        string planSha256, int offset, int limit, Func<Task<TerminalActor>> refreshLogin, CancellationToken token)
    {
        if (!WorkspaceSources.Digest(planSha256) || offset is < 0 or >= 20000 || limit is < 1 or > 50)
            throw new ArgumentException("WORKSPACE_SOURCE_DIFF_QUERY_INVALID");
        var workspace = await sources.RequireAsync(actor, workspaceId, importId, token);
        var import = RequirePlan(await store.FindAsync(workspaceId, importId, actor.Subject, token)
            ?? throw new KeyNotFoundException("WORKSPACE_SOURCE_NOT_FOUND"));
        var result = await gateway.ReadDiffAsync(workspace, import, actor, offset, limit, token);
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
