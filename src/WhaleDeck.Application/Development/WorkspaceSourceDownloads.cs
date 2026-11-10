using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public interface IWorkspaceSourceDownloadGateway
{
    Task<ReadOnlyMemory<byte>> ExportPreviousAsync(WorkspaceDefinition workspace, WorkspaceSourceView import,
        TerminalActor actor, Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken token);
}

public sealed class WorkspaceSourceDownloads(WorkspaceSources sources, IWorkspaceSourceStore store,
    IWorkspaceSourceDownloadGateway gateway, TimeProvider clock)
{
    public async Task ExportPreviousAsync(TerminalActor actor, Guid workspaceId, Guid importId,
        Func<Task<TerminalActor>> refreshLogin, Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken token)
    {
        var workspace = await sources.RequireAsync(actor, workspaceId, importId, token);
        var import = await store.FindAsync(workspaceId, importId, actor.Subject, token);
        if (import is not { State: WorkspaceSources.Published, PreviousImportId: not null, Plan.Update: not null,
            ConflictResolution: WorkspaceSources.CleanOnly or WorkspaceSources.RetainAndReplace })
            throw new KeyNotFoundException("WORKSPACE_SOURCE_PREVIOUS_NOT_FOUND");

        await store.RecordPreviousDownloadAsync(workspaceId, importId, actor, token);
        var verifiedAt = clock.GetTimestamp();
        var sent = false;
        var tail = await gateway.ExportPreviousAsync(workspace, import, actor, async (chunk, lifetime) =>
        {
            if (!sent || clock.GetElapsedTime(verifiedAt) >= TimeSpan.FromSeconds(6))
                await RequireLiveAsync(lifetime);
            await write(chunk, lifetime);
            sent = true;
        }, token);
        await RequireLiveAsync(token);
        await write(tail, token);

        async Task RequireLiveAsync(CancellationToken lifetime)
        {
            var current = await refreshLogin();
            if (current.Subject != actor.Subject || current.LoginBinding != actor.LoginBinding)
                throw new UnauthorizedAccessException("WORKSPACE_SOURCE_LOGIN_CHANGED");
            _ = await sources.RequireAsync(current, workspaceId, importId, lifetime);
            verifiedAt = clock.GetTimestamp();
        }
    }
}
