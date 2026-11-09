using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Development;

public interface IWorkspaceRunReportReadAudit
{
    Task RecordAsync(TerminalActor actor, Guid workspaceId, Guid runId, WorkspaceReportFormat format,
        CancellationToken cancellationToken);
}

public sealed class WorkspaceRunReportDownloads(
    WorkspaceReportAuthorization authorization, IWorkspaceRunReportStore reports,
    IWorkspaceRunReportReadAudit audit, TimeProvider clock)
{
    public async Task<WorkspaceReportDownload> ReadAsync(TerminalActor actor, Guid workspaceId, Guid runId,
        WorkspaceReportFormat format, CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty || runId == Guid.Empty || !Enum.IsDefined(format))
            throw new ArgumentException("WORKSPACE_REPORT_REFERENCE_INVALID");

        await authorization.RequireReaderAsync(actor, workspaceId, cancellationToken);
        var result = await reports.ReadAsync(workspaceId, runId, format, cancellationToken);
        await authorization.RequireReaderAsync(actor, workspaceId, cancellationToken);
        if (result is null || result.RetainUntilUtc <= clock.GetUtcNow())
            throw new KeyNotFoundException("WORKSPACE_REPORT_NOT_FOUND");

        // This records authorization to return the attachment, not network delivery.
        await audit.RecordAsync(actor, workspaceId, runId, format, cancellationToken);
        await authorization.RequireReaderAsync(actor, workspaceId, cancellationToken);
        if (result.RetainUntilUtc <= clock.GetUtcNow())
            throw new KeyNotFoundException("WORKSPACE_REPORT_NOT_FOUND");
        return result;
    }

}
