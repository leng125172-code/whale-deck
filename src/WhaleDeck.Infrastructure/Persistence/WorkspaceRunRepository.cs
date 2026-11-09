using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceRunRepository(PlatformDbContext db, TimeProvider clock, WorkspaceRegistrationPolicy policy)
    : IWorkspaceRunStore, IWorkspaceRunReportWork
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string PendingIndex = "uq_workspace_run_pending";
    private sealed record Snapshot(WorkspaceDefinition Workspace, WorkspaceRunCommand Command);

    public async Task<WorkspaceRunView> EnqueueAsync(WorkspaceDefinition workspace, TerminalActor actor, Guid key,
        WorkspaceRunInput input, CancellationToken token)
    {
        var idempotency = key.ToString("D");
        var request = JsonSerializer.Serialize(new { workspaceId = workspace.Id, version = workspace.Version,
            environmentSha256 = workspace.EnvironmentSha256, input.Runtime, input.Task }, Json);
        var prior = await ReplayAsync(actor.Subject, idempotency, request, token);
        if (prior is not null) return prior;
        var now = clock.GetUtcNow();
        var job = new OperationJob
        {
            ActorSubject = actor.Subject, JobType = WorkspaceRuns.JobType, ResourceId = workspace.Id,
            RequestJson = request, IdempotencyKey = idempotency, CreatedAtUtc = now
        };
        var row = new WorkspaceRunIntent
        {
            Id = job.Id, WorkspaceId = workspace.Id, Runtime = input.Runtime, Task = input.Task,
            SnapshotJson = JsonSerializer.Serialize(new Snapshot(workspace,
                WorkspaceRuns.BindCommand(job.Id, workspace, input, policy.ReportRetentionDays)), Json),
            LoginBinding = actor.LoginBinding, AvailableAtUtc = now
        };
        db.OperationJobs.Add(job);
        db.WorkspaceRunIntents.Add(row);
        Event(job, "WORKSPACE_RUN_QUEUED", now);
        Audit(actor, row, "development.workspace.run.enqueue", "Accepted", now);
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            db.ChangeTracker.Clear();
            var winner = await ReplayAsync(actor.Subject, idempotency, request, token);
            if (winner is not null) return winner;
            if (postgres.ConstraintName == PendingIndex) throw new InvalidOperationException("WORKSPACE_RUN_ALREADY_PENDING");
            throw;
        }
        return View(row, job);
    }

    private async Task<WorkspaceRunView?> ReplayAsync(string subject, string key, string request, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ActorSubject == subject && row.IdempotencyKey == key, token);
        if (job is null) return null;
        if (job.JobType != WorkspaceRuns.JobType || !JobRepository.JsonRequestsEqual(job.RequestJson, request))
            throw new InvalidOperationException("WORKSPACE_RUN_KEY_CONFLICT");
        var intent = await db.WorkspaceRunIntents.AsNoTracking().SingleAsync(row => row.Id == job.Id, token);
        return View(intent, job);
    }

    public async Task<WorkspaceRunView?> GetAsync(Guid workspaceId, Guid runId, string owner, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceRunIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId && row.Id == runId && job.ActorSubject == owner && job.JobType == WorkspaceRuns.JobType
                          select new { row, job }).SingleOrDefaultAsync(token);
        return pair is null ? null : View(pair.row, pair.job);
    }

    public Task<WorkspaceRunView> CancelAsync(Guid workspaceId, Guid runId, TerminalActor actor, CancellationToken token) =>
        ChangeAsync(runId, (row, job) =>
        {
            if (row.WorkspaceId != workspaceId || job.ActorSubject != actor.Subject) throw NotFound();
            if (row.FinishedAtUtc is not null || job.CancelRequestedAtUtc is not null) return View(row, job);
            var now = clock.GetUtcNow();
            job.CancelRequestedAtUtc = now;
            job.Version++;
            if (job.StartedAtUtc is null)
            {
                job.State = "Canceled";
                job.Phase = "CanceledBeforeDispatch";
                job.CompletedAtUtc = row.FinishedAtUtc = now;
                row.Lease = null;
                row.LeaseUntilUtc = null;
                QueueReport(row, SnapshotOf(row).Command, now);
            }
            else row.AvailableAtUtc = now;
            Event(job, "WORKSPACE_RUN_CANCEL_REQUESTED", now);
            Audit(actor, row, "development.workspace.run.cancel", "Accepted", now);
            return View(row, job);
        }, token);

    public async Task<WorkspaceRunClaim?> ClaimAsync(CancellationToken token) =>
        (await ClaimCoreAsync(report: false, token))?.Execution;

    public Task<WorkspaceRunReportClaim?> ClaimReportAsync(CancellationToken token) => ClaimCoreAsync(report: true, token);

    private Task<WorkspaceRunReportClaim?> ClaimCoreAsync(bool report, CancellationToken token) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync<WorkspaceRunReportClaim?>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var now = clock.GetUtcNow();
            var rows = await db.WorkspaceRunIntents.FromSqlInterpolated($"""
                SELECT * FROM development_workspace_runs
                 WHERE (CASE WHEN {report} THEN report_stage IN ('Collect', 'Retire') ELSE finished_at_utc IS NULL END)
                   AND available_at_utc <= {now}
                   AND (lease_until_utc IS NULL OR lease_until_utc <= {now})
                 ORDER BY available_at_utc, id
                 LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(token);
            if (rows.Count == 0) return null;
            var row = rows[0];
            var job = await db.OperationJobs.SingleAsync(item => item.Id == row.Id && item.JobType == WorkspaceRuns.JobType, token);
            var lease = Guid.NewGuid();
            row.Lease = lease;
            row.LeaseUntilUtc = now.AddSeconds(40);
            var snapshot = SnapshotOf(row);
            var (workspace, command) = snapshot;
            if (workspace.Id != row.WorkspaceId || workspace.OwnerSubject != job.ActorSubject || job.ResourceId != row.WorkspaceId ||
                command.RunId != job.Id || command.Runtime != row.Runtime || !command.RequireControl ||
                command.CpuMillis != workspace.Budget.CpuMillis || command.MemoryMiB != workspace.Budget.MemoryMiB ||
                command.DeadlineSeconds is < 1 or > 3600 || command.ReportRetentionDays is < 1 or > 365 ||
                (report && command.ReportRetentionDays is null))
                throw new InvalidDataException("WORKSPACE_RUN_SNAPSHOT_INVALID");
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(new(View(row, job), workspace, new(job.ActorSubject, null, [], row.LoginBinding, job.Id.ToString("N"), null),
                lease, command, job.StartedAtUtc), row.ReportStage ?? "", report ? Receipt(job) : null);
        });

    public Task<bool> EnterDispatchAsync(WorkspaceRunClaim claim, CancellationToken token) =>
        ChangeAsync(claim.Run.RunId, (row, job) =>
        {
            var now = clock.GetUtcNow();
            if (!Owns(row, claim, now) || job.StartedAtUtc is not null || job.CancelRequestedAtUtc is not null) return false;
            job.StartedAtUtc = now;
            job.State = "Running";
            job.Phase = "DispatchCommitted";
            job.Version++;
            Event(job, "WORKSPACE_RUN_DISPATCH_COMMITTED", now);
            return true;
        }, token);

    public Task DeferAsync(WorkspaceRunClaim claim, CancellationToken token) => ChangeAsync(claim.Run.RunId, (row, _) =>
    {
        var now = clock.GetUtcNow();
        if (!Owns(row, claim, now)) return false;
        row.AvailableAtUtc = now.AddSeconds(4);
        row.Lease = null;
        row.LeaseUntilUtc = null;
        return true;
    }, token);

    public Task FinishAsync(WorkspaceRunClaim claim, string state, string phase, string? errorCode, WorkspaceRunnerReceipt? receipt,
        CancellationToken token) => ChangeAsync(claim.Run.RunId, (row, job) =>
    {
        var now = clock.GetUtcNow();
        if (!Owns(row, claim, now)) return false;
        job.State = state;
        job.Phase = phase;
        job.ErrorCode = errorCode;
        job.ResultJson = receipt is null ? null : JsonSerializer.Serialize(receipt, Json);
        job.CompletedAtUtc = row.FinishedAtUtc = now;
        job.Version++;
        row.Lease = null;
        row.LeaseUntilUtc = null;
        QueueReport(row, claim.Command, now);
        Event(job, errorCode ?? "WORKSPACE_RUN_SETTLED", now);
        Audit(claim.Actor, row, "development.workspace.run.settle", state, now);
        return true;
    }, token);

    public Task ReleaseReportAsync(WorkspaceRunReportClaim claim, string? terminalStage, CancellationToken token) =>
        ChangeAsync(claim.Execution.Run.RunId, (row, _) =>
        {
            var now = clock.GetUtcNow();
            if (row.ReportStage != claim.Stage || row.Lease != claim.Execution.Lease ||
                row.LeaseUntilUtc is not { } deadline || deadline <= now) return false;
            if (terminalStage is not null)
            {
                if ((claim.Stage, terminalStage) is not (("Retire", "Complete") or ("Retire", "Attention") or ("Collect", "Attention")))
                    throw new ArgumentException("WORKSPACE_REPORT_STAGE_INVALID");
                row.ReportStage = terminalStage;
                Audit(claim.Execution.Actor, row, claim.Stage == "Retire" ? "development.workspace.report.retire" :
                    "development.workspace.report.collect", terminalStage, now);
            }
            else row.AvailableAtUtc = now.AddSeconds(15);
            row.Lease = null;
            row.LeaseUntilUtc = null;
            return true;
        }, token);

    private static void QueueReport(WorkspaceRunIntent row, WorkspaceRunCommand command, DateTimeOffset now)
    {
        if (command.ReportRetentionDays is null) return;
        row.ReportStage = "Collect";
        row.AvailableAtUtc = now;
    }

    private static Snapshot SnapshotOf(WorkspaceRunIntent row) => JsonSerializer.Deserialize<Snapshot>(row.SnapshotJson, Json)
        ?? throw new InvalidDataException("WORKSPACE_RUN_SNAPSHOT_INVALID");

    private static WorkspaceRunnerReceipt? Receipt(OperationJob job) =>
        job.ResultJson is null ? null : JsonSerializer.Deserialize<WorkspaceRunnerReceipt>(job.ResultJson, Json);

    private Task<T> ChangeAsync<T>(Guid id, Func<WorkspaceRunIntent, OperationJob, T> change, CancellationToken token) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'", token);
            var rows = await db.WorkspaceRunIntents.FromSqlInterpolated(
                $"SELECT * FROM development_workspace_runs WHERE id = {id} FOR UPDATE").ToListAsync(token);
            var row = rows.SingleOrDefault() ?? throw NotFound();
            var job = await db.OperationJobs.SingleAsync(item => item.Id == row.Id && item.JobType == WorkspaceRuns.JobType, token);
            var result = change(row, job);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return result;
        });

    private static bool Owns(WorkspaceRunIntent row, WorkspaceRunClaim claim, DateTimeOffset now) =>
        row.FinishedAtUtc is null && row.Lease == claim.Lease && row.LeaseUntilUtc > now;

    private void Event(OperationJob job, string code, DateTimeOffset now) => db.OperationJobEvents.Add(new()
    {
        JobId = job.Id, Sequence = job.Version + 1, State = job.State, Phase = job.Phase, MessageCode = code, OccurredAtUtc = now
    });

    private void Audit(TerminalActor actor, WorkspaceRunIntent row, string action, string result, DateTimeOffset now) =>
        db.AuditEvents.Add(new()
        {
            ActorSubject = actor.Subject, Action = action, TargetType = "workspace", TargetId = row.WorkspaceId.ToString("D"),
            Result = result, JobId = row.Id, TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = now
        });

    private WorkspaceRunView View(WorkspaceRunIntent row, OperationJob job) => new(row.Id, row.WorkspaceId,
        row.Runtime, row.Task, job.State, job.Phase, job.CreatedAtUtc, job.CompletedAtUtc, job.CancelRequestedAtUtc is not null,
        job.ErrorCode, Receipt(job)?.Status, row.ReportStage switch
        {
            not null when job.CompletedAtUtc is { } completed && SnapshotOf(row).Command.ReportRetentionDays is { } days &&
                completed.AddDays(days) <= clock.GetUtcNow() => "Expired",
            "Collect" => "Pending",
            "Retire" or "Complete" => "Available",
            "Attention" => "ArtifactAttention",
            _ => null
        });

    private static KeyNotFoundException NotFound() => new("WORKSPACE_RUN_NOT_FOUND");
}
