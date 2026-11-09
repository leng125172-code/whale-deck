using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceReportGrant(Guid GroupId, bool Enabled, long Version);

public interface IWorkspaceReportAccess
{
    Task<bool> AllowsAsync(Guid workspaceId, Guid[] groupIds, CancellationToken token);
    Task<WorkspaceReportGrant> GetAsync(Guid workspaceId, Guid groupId, CancellationToken token);
    Task<WorkspaceReportGrant> SetAsync(TerminalActor actor, Guid workspaceId, Guid groupId, bool enabled,
        long expectedVersion, CancellationToken token);
}

public sealed class WorkspaceReportAuthorization(
    IPrivilegedIdentityDirectory identity, ILoginSessionRegistry sessions, IWorkspaceReportAccess access, TimeProvider clock,
    IDevelopmentWorkspaceRegistry? registry = null, WorkspaceRegistrationPolicy? policy = null)
{
    public async Task<CurrentUserDto> ResolveAsync(TerminalActor actor, CancellationToken token)
    {
        FileManagementService.ValidateActor(actor);
        await RequireLoginAsync(actor, token);
        var current = await identity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, token);
        if (current.Subject != actor.Subject || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_CURRENT_IDENTITY_REQUIRED");
        await RequireLoginAsync(actor, token);
        return current;
    }

    public async Task RequireReaderAsync(TerminalActor actor, Guid workspaceId, CancellationToken token)
    {
        var current = await ResolveAsync(actor, token);
        if (current.IsAdministrator) return;
        var ids = new HashSet<Guid>();
        foreach (var group in current.Groups)
            if (Guid.TryParseExact(group, "D", out var id) && id != Guid.Empty)
                ids.Add(id);
        var owner = registry is not null && policy is not null && ids.Overlaps(policy.CreateGroupIds) &&
            await registry.FindAsync(workspaceId, actor.Subject, token) is { } workspace &&
            policy.StorageRootIds.Contains(workspace.StorageRootId, StringComparer.Ordinal);
        if (!owner && (ids.Count == 0 || !await access.AllowsAsync(workspaceId, ids.ToArray(), token)))
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_READER_REQUIRED");
        await RequireLoginAsync(actor, token);
    }

    public async Task RequireLoginAsync(TerminalActor actor, CancellationToken token)
    {
        if (!await sessions.IsActiveAsync(actor.Subject, actor.LoginBinding, token))
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_LOGIN_REQUIRED");
    }
}

public sealed class WorkspaceReportGrantManagement(WorkspaceReportAuthorization authorization, IWorkspaceReportAccess access)
{
    public async Task<WorkspaceReportGrant> GetAsync(TerminalActor actor, Guid workspaceId, Guid groupId, CancellationToken token)
    {
        await AuthorizeAsync(actor, workspaceId, groupId, token);
        return await access.GetAsync(workspaceId, groupId, token);
    }

    public async Task<WorkspaceReportGrant> SetAsync(TerminalActor actor, Guid workspaceId, Guid groupId, bool enabled,
        long expectedVersion, CancellationToken token)
    {
        if (expectedVersion < 0 || expectedVersion == long.MaxValue || (expectedVersion == 0 && !enabled))
            throw new ArgumentException("WORKSPACE_REPORT_GRANT_VERSION_INVALID");
        await AuthorizeAsync(actor, workspaceId, groupId, token);
        return await access.SetAsync(actor, workspaceId, groupId, enabled, expectedVersion, token);
    }

    private async Task AuthorizeAsync(TerminalActor actor, Guid workspaceId, Guid groupId, CancellationToken token)
    {
        if (workspaceId == Guid.Empty || groupId == Guid.Empty)
            throw new ArgumentException("WORKSPACE_REPORT_GRANT_SCOPE_INVALID");
        if (!(await authorization.ResolveAsync(actor, token)).IsAdministrator)
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_GRANT_ADMINISTRATOR_REQUIRED");
    }
}
