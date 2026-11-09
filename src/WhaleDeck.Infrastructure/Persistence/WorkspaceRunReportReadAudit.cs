using System.Text.Json;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceRunReportReadAudit(PlatformDbContext db, TimeProvider clock) : IWorkspaceRunReportReadAudit
{
    public async Task RecordAsync(TerminalActor actor, Guid workspaceId, Guid runId, WorkspaceReportFormat format,
        CancellationToken cancellationToken)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorSubject = actor.Subject,
            Action = "development.workspace.report-download",
            TargetType = "workspace-run-report",
            TargetId = runId.ToString("D"),
            Result = "Prepared",
            TraceId = actor.TraceId,
            SourceIp = actor.SourceIp,
            DetailJson = JsonSerializer.Serialize(new { workspaceId, format = format.ToString() }),
            OccurredAtUtc = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
