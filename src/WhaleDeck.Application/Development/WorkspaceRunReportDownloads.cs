using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Application.Development;

public interface IWorkspaceRunReportReadAudit
{
    Task RecordAsync(TerminalActor actor, Guid workspaceId, Guid runId, WorkspaceReportFormat format,
        CancellationToken cancellationToken);
}

public sealed class WorkspaceRunReportDownloads(
    IPrivilegedIdentityDirectory identity, ILoginSessionRegistry sessions, IWorkspaceRunReportStore reports,
    IWorkspaceRunReportReadAudit audit, TimeProvider clock)
{
    public async Task<WorkspaceReportDownload> ReadAsync(TerminalActor actor, Guid workspaceId, Guid runId,
        WorkspaceReportFormat format, CancellationToken cancellationToken)
    {
        FileManagementService.ValidateActor(actor);
        if (workspaceId == Guid.Empty || runId == Guid.Empty || !Enum.IsDefined(format))
            throw new ArgumentException("WORKSPACE_REPORT_REFERENCE_INVALID");

        await RequireAdministratorAsync(actor, cancellationToken);
        var result = await reports.ReadAsync(workspaceId, runId, format, cancellationToken);
        await RequireAdministratorAsync(actor, cancellationToken);
        if (result is null || result.RetainUntilUtc <= clock.GetUtcNow())
            throw new KeyNotFoundException("WORKSPACE_REPORT_NOT_FOUND");

        // This records authorization to return the attachment, not network delivery.
        await audit.RecordAsync(actor, workspaceId, runId, format, cancellationToken);
        await RequireLoginAsync(actor, cancellationToken);
        if (result.RetainUntilUtc <= clock.GetUtcNow())
            throw new KeyNotFoundException("WORKSPACE_REPORT_NOT_FOUND");
        return result;
    }

    private async Task RequireAdministratorAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        await RequireLoginAsync(actor, cancellationToken);
        var current = await identity.ResolveForPrivilegedActionAsync(actor.Subject, actor.Name, cancellationToken);
        if (current.Subject != actor.Subject || !current.IsAdministrator || current.PermissionExpiresAtUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_ADMINISTRATOR_REQUIRED");
        await RequireLoginAsync(actor, cancellationToken);
    }

    private async Task RequireLoginAsync(TerminalActor actor, CancellationToken cancellationToken)
    {
        if (!await sessions.IsActiveAsync(actor.Subject, actor.LoginBinding, cancellationToken))
            throw new UnauthorizedAccessException("WORKSPACE_REPORT_LOGIN_REQUIRED");
    }
}
