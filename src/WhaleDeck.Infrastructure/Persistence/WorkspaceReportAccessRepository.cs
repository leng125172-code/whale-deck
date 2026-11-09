using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceReportAccessRepository(PlatformDbContext db, TimeProvider clock) : IWorkspaceReportAccess
{
    public Task<bool> AllowsAsync(Guid workspaceId, Guid[] groupIds, CancellationToken token) =>
        db.WorkspaceReportGrants.AnyAsync(row => row.WorkspaceId == workspaceId && row.Enabled && groupIds.Contains(row.GroupId), token);

    public async Task<WorkspaceReportGrant> GetAsync(Guid workspaceId, Guid groupId, CancellationToken token)
    {
        await RequireKnownAsync(workspaceId, token);
        return await FindAsync(workspaceId, groupId, token) ?? new(groupId, false, 0);
    }

    public Task<WorkspaceReportGrant> SetAsync(TerminalActor actor, Guid workspaceId, Guid groupId, bool enabled,
        long expectedVersion, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await RequireKnownAsync(workspaceId, token);
        var next = checked(expectedVersion + 1);
        var changed = expectedVersion == 0
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workspace_report_grants(workspace_id, group_id, enabled, version)
                VALUES ({workspaceId}, {groupId}, {enabled}, {next}) ON CONFLICT DO NOTHING
                """, token)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workspace_report_grants SET enabled={enabled}, version={next}
                WHERE workspace_id={workspaceId} AND group_id={groupId} AND version={expectedVersion}
                """, token);
        if (changed == 0)
        {
            var current = await FindAsync(workspaceId, groupId, token);
            if (current == new WorkspaceReportGrant(groupId, enabled, next)) return current;
            throw new InvalidOperationException("WORKSPACE_REPORT_GRANT_VERSION_CONFLICT");
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_events(id, actor_subject, action, target_type, target_id, result, source_ip,
                trace_id, detail_json, occurred_at_utc)
            VALUES ({Guid.NewGuid()}, {actor.Subject}, 'development.workspace.report-grant', 'workspace', {workspaceId.ToString("D")},
                'Succeeded', {actor.SourceIp}, {actor.TraceId},
                CAST({JsonSerializer.Serialize(new { groupId, enabled, version = next })} AS jsonb), {clock.GetUtcNow()})
            """, token);
        await transaction.CommitAsync(token);
        return new WorkspaceReportGrant(groupId, enabled, next);
    });

    private Task<WorkspaceReportGrant?> FindAsync(Guid workspaceId, Guid groupId, CancellationToken token) =>
        db.WorkspaceReportGrants.AsNoTracking().Where(row => row.WorkspaceId == workspaceId && row.GroupId == groupId)
            .Select(row => new WorkspaceReportGrant(row.GroupId, row.Enabled, row.Version)).SingleOrDefaultAsync(token);

    private async Task RequireKnownAsync(Guid workspaceId, CancellationToken token)
    {
        if (!await db.DevelopmentWorkspaces.AnyAsync(row => row.Id == workspaceId, token) &&
            !await db.WorkspaceRunReports.AnyAsync(row => row.WorkspaceId == workspaceId, token))
            throw new KeyNotFoundException("WORKSPACE_REPORT_SCOPE_NOT_FOUND");
    }
}
