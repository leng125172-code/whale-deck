using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceRunReportRepository(PlatformDbContext db, TimeProvider clock) : IWorkspaceRunReportStore
{
    private const int MaxExportBytes = 512 * 1024;

    public async Task<WorkspaceReportWriteState> SaveAsync(WorkspaceRunReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retention = report.RetainUntilUtc - report.FinalizedAtUtc;
        if (retention < TimeSpan.FromDays(1) || retention > TimeSpan.FromDays(365) ||
            retention.Ticks % TimeSpan.TicksPerDay != 0 || report.Outcome == WorkspaceRunOutcome.Skipped ||
            !Enum.IsDefined(report.Outcome) || report.SchemaVersion != 1)
            throw new ArgumentException("WORKSPACE_REPORT_STORAGE_EVIDENCE_INVALID");
        if (report.RetainUntilUtc <= clock.GetUtcNow())
            return WorkspaceReportWriteState.Expired;

        var binding = new WorkspaceRunJobBinding(report.RunId, report.TraceId, report.Source.Fingerprint,
            report.Environment.Digest, report.Environment.Version, retention.Days);
        var request = binding.ToJobRequestJson();
        var phase = report.Outcome.ToString();
        var state = report.Outcome switch
        {
            WorkspaceRunOutcome.Succeeded => "Succeeded",
            WorkspaceRunOutcome.Canceled => "Canceled",
            _ => "Failed"
        };
        var json = WorkspaceRunReportExport.ToJson(report);
        var markdown = Encoding.UTF8.GetBytes(WorkspaceRunReportExport.ToMarkdown(report));
        if (json.Length is 0 or > MaxExportBytes || markdown.Length is 0 or > MaxExportBytes)
            throw new ArgumentException("WORKSPACE_REPORT_EXPORT_LIMIT");
        var jsonHash = Hash(json);
        var markdownHash = Hash(markdown);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var source = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM operation_jobs
                WHERE id = {report.JobId} AND job_type = {WorkspaceRunJobBinding.JobType}
                  AND resource_id = {report.WorkspaceId} AND actor_subject = {report.ActorSubject}
                  AND state = {state} AND phase = {phase}
                  AND COALESCE(started_at_utc, created_at_utc) = {report.StartedAtUtc}
                  AND completed_at_utc = {report.FinalizedAtUtc}
                  AND (request_json -> 'workspaceRun') = (CAST({request} AS jsonb) -> 'workspaceRun')
                FOR SHARE
                """).ToListAsync(cancellationToken);
            if (source.Count == 0) return WorkspaceReportWriteState.SourceUnavailable;

            if (report.RetainUntilUtc <= clock.GetUtcNow()) return WorkspaceReportWriteState.Expired;
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workspace_run_reports
                    (run_id, workspace_id, job_id, schema_version, finalized_at_utc, retain_until_utc,
                     json_content, markdown_content, json_sha256, markdown_sha256)
                VALUES
                    ({report.RunId}, {report.WorkspaceId}, {report.JobId}, {report.SchemaVersion},
                     {report.FinalizedAtUtc}, {report.RetainUntilUtc}, {json}, {markdown}, {jsonHash}, {markdownHash})
                ON CONFLICT DO NOTHING
                """, cancellationToken);
            var result = WorkspaceReportWriteState.Created;
            if (inserted == 0)
            {
                var existing = await db.WorkspaceRunReports.AsNoTracking()
                    .Where(row => row.RunId == report.RunId)
                    .Select(row => new { row.WorkspaceId, row.JobId, row.JsonSha256, row.MarkdownSha256, row.ExpiredAtUtc })
                    .SingleOrDefaultAsync(cancellationToken);
                result = existing is null || existing.WorkspaceId != report.WorkspaceId || existing.JobId != report.JobId ||
                    existing.JsonSha256 != jsonHash || existing.MarkdownSha256 != markdownHash
                    ? WorkspaceReportWriteState.Conflict
                    : existing.ExpiredAtUtc is null ? WorkspaceReportWriteState.AlreadyStored : WorkspaceReportWriteState.Expired;
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public async Task<WorkspaceReportDownload?> ReadAsync(
        Guid workspaceId, Guid runId, WorkspaceReportFormat format, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (workspaceId == Guid.Empty || runId == Guid.Empty) return null;
        var markdown = format == WorkspaceReportFormat.Markdown;
        var now = clock.GetUtcNow();
        var row = await db.WorkspaceRunReports.AsNoTracking()
            .Where(row => row.RunId == runId && row.WorkspaceId == workspaceId &&
                row.RetainUntilUtc > now && row.ExpiredAtUtc == null)
            .Select(row => new
            {
                Content = markdown ? row.MarkdownContent : row.JsonContent,
                Sha256 = markdown ? row.MarkdownSha256 : row.JsonSha256,
                row.RetainUntilUtc
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.RetainUntilUtc <= clock.GetUtcNow()) return null;
        if (row.Content is not { Length: > 0 and <= MaxExportBytes } || Hash(row.Content) != row.Sha256)
            throw new InvalidDataException("WORKSPACE_REPORT_STORAGE_INTEGRITY");
        return new(row.Content, row.Sha256, row.RetainUntilUtc);
    }

    public Task<int> ExpireAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = clock.GetUtcNow();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE workspace_run_reports
            SET json_content = NULL, markdown_content = NULL, expired_at_utc = {now}
            WHERE run_id IN (
                SELECT run_id FROM workspace_run_reports
                WHERE expired_at_utc IS NULL AND retain_until_utc <= {now}
                ORDER BY retain_until_utc, run_id
                LIMIT {limit} FOR UPDATE SKIP LOCKED
            )
            """, cancellationToken);
    }

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
