using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceRunReportRepository(PlatformDbContext db, TimeProvider clock) : IWorkspaceRunReportStore, IWorkspaceCollectedReportStore
{
    private const int MaxExportBytes = 512 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<WorkspaceReportWriteState> SaveAsync(WorkspaceRunReport report, CancellationToken cancellationToken) =>
        SaveCoreAsync(report, null, cancellationToken);

    public async Task<WorkspaceReportWriteState> SaveCollectedAsync(WorkspaceRunReportClaim claim, WorkspaceRunReport report, CancellationToken token)
    {
        var result = await SaveCoreAsync(report, claim, token);
        if (result == WorkspaceReportWriteState.Conflict)
            throw new InvalidDataException("WORKSPACE_REPORT_COLLECTION_CONFLICT");
        return result;
    }

    private async Task<WorkspaceReportWriteState> SaveCoreAsync(WorkspaceRunReport report, WorkspaceRunReportClaim? claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retention = report.RetainUntilUtc - report.FinalizedAtUtc;
        if (retention < TimeSpan.FromDays(1) || retention > TimeSpan.FromDays(365) ||
            retention.Ticks % TimeSpan.TicksPerDay != 0 || report.Outcome == WorkspaceRunOutcome.Skipped ||
            !Enum.IsDefined(report.Outcome) || report.SchemaVersion != 1)
            throw new ArgumentException("WORKSPACE_REPORT_STORAGE_EVIDENCE_INVALID");
        if (report.RetainUntilUtc <= clock.GetUtcNow())
            return WorkspaceReportWriteState.Expired;

        if (claim is null && (report.RunId == report.JobId || report.Source.Fingerprint is null))
            throw new ArgumentException("WORKSPACE_REPORT_STORAGE_EVIDENCE_INVALID");
        if (claim is not null) ValidateCollected(claim, report, retention.Days);
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
            var source = claim is null
                ? await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM operation_jobs
                WHERE id = {report.JobId} AND job_type = {WorkspaceRunJobBinding.JobType}
                  AND resource_id = {report.WorkspaceId} AND actor_subject = {report.ActorSubject}
                  AND state = {state} AND phase = {phase}
                  AND COALESCE(started_at_utc, created_at_utc) = {report.StartedAtUtc}
                  AND completed_at_utc = {report.FinalizedAtUtc}
                  AND (request_json -> 'workspaceRun') = (CAST({new WorkspaceRunJobBinding(report.RunId, report.TraceId,
                      report.Source.Fingerprint!, report.Environment.Digest, report.Environment.Version, retention.Days).ToJobRequestJson()} AS jsonb) -> 'workspaceRun')
                FOR SHARE
                """).ToListAsync(cancellationToken)
                : await LockCollectedSourceAsync(claim, report, cancellationToken);
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
            if (claim is not null && result is WorkspaceReportWriteState.Created or WorkspaceReportWriteState.AlreadyStored)
            {
                var stage = RetirementStage(claim, report);
                var available = clock.GetUtcNow();
                var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE development_workspace_runs
                    SET report_stage = {stage}, available_at_utc = {available}, lease = NULL, lease_until_utc = NULL
                    WHERE id = {report.RunId} AND lease = {claim.Execution.Lease} AND report_stage = 'Collect'
                    """, cancellationToken);
                if (updated != 1) throw new InvalidDataException("WORKSPACE_REPORT_STAGE_CONFLICT");
                db.AuditEvents.Add(new AuditEvent
                {
                    ActorSubject = report.ActorSubject, Action = "development.workspace.report.collect", TargetType = "workspace",
                    TargetId = report.WorkspaceId.ToString("D"), Result = stage, JobId = report.JobId, TraceId = report.TraceId,
                    OccurredAtUtc = available
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private Task<List<Guid>> LockCollectedSourceAsync(WorkspaceRunReportClaim claim, WorkspaceRunReport report, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var receipt = claim.Receipt is null ? null : JsonSerializer.Serialize(claim.Receipt, Json);
        return db.Database.SqlQuery<Guid>($"""
            SELECT job.id AS "Value"
            FROM development_workspace_runs run
            JOIN operation_jobs job ON job.id = run.id
            WHERE run.id = {report.RunId} AND run.workspace_id = {report.WorkspaceId}
              AND run.report_stage = 'Collect' AND run.lease = {claim.Execution.Lease} AND run.lease_until_utc > {now}
              AND run.finished_at_utc = {report.FinalizedAtUtc}
              AND run.snapshot_json -> 'workspace' ->> 'environmentSha256' = {report.Environment.Digest}
              AND (run.snapshot_json -> 'workspace' ->> 'version')::bigint = {report.Environment.Version}
              AND (run.snapshot_json -> 'command' ->> 'reportRetentionDays')::integer = {claim.Execution.Command.ReportRetentionDays}
              AND job.job_type = {WorkspaceRunJobBinding.JobType} AND job.resource_id = run.workspace_id
              AND job.actor_subject = {report.ActorSubject}
              AND job.state = {claim.Execution.Run.State} AND job.phase = {claim.Execution.Run.Phase}
              AND COALESCE(job.started_at_utc, job.created_at_utc) = {report.StartedAtUtc}
              AND job.completed_at_utc = {report.FinalizedAtUtc}
              AND job.result_json IS NOT DISTINCT FROM CAST({receipt} AS jsonb)
            FOR UPDATE OF run
            """).ToListAsync(token);
    }

    private static void ValidateCollected(WorkspaceRunReportClaim claim, WorkspaceRunReport report, int retentionDays)
    {
        var execution = claim.Execution;
        if (claim.Stage != "Collect" || report.RunId != report.JobId ||
            report.RunId != execution.Run.RunId || report.WorkspaceId != execution.Run.WorkspaceId ||
            report.TraceId != execution.Actor.TraceId || report.ActorSubject != execution.Actor.Subject ||
            report.Outcome != WorkspaceRunReportPipeline.Outcome(execution.Run) || report.ExitCode != claim.Receipt?.RuntimeExitCode ||
            report.Source != new WorkspaceReportSource(null, null, null, null) ||
            retentionDays != execution.Command.ReportRetentionDays)
            throw new ArgumentException("WORKSPACE_REPORT_STORAGE_EVIDENCE_INVALID");
    }

    private static string RetirementStage(WorkspaceRunReportClaim claim, WorkspaceRunReport report)
    {
        if (claim.Receipt?.TestReport is not { State: "Present" } manifest) return "Complete";
        var test = report.Steps.Single().Tests.SingleOrDefault();
        return test is { Integrity: WorkspaceArtifactIntegrity.Matched, Collection: WorkspaceArtifactCollectionState.Present } &&
            test.Format == manifest.Format && test.Result?.ArtifactSha256 == manifest.Sha256 ? "Retire" : "Attention";
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
