using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceLifecycleRepository(PlatformDbContext db, TimeProvider clock) : IWorkspaceLifecycleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Snapshot(WorkspaceDefinition Workspace, WorkspaceLifecyclePlan Plan);

    public async Task RequireIdleAsync(Guid workspaceId, CancellationToken token)
    {
        if (await db.WorkspaceArchiveIntents.AnyAsync(row => row.WorkspaceId == workspaceId && row.FinishedAtUtc == null, token))
            throw new InvalidOperationException("WORKSPACE_ARCHIVE_NOT_SETTLED");
        // Legacy Git cancellation has no explicit host stop acknowledgement.
        // Its unit is bounded to 100s, capability admission to 60s, plus teardown
        // margin. Preserve the cancellation record; do not call it a stop proof.
        var drainBefore = clock.GetUtcNow().AddSeconds(-180);
        if (await db.WorkspaceSourceIntents.AnyAsync(row => row.WorkspaceId == workspaceId &&
            row.GitRepositoryId != null && row.State == WorkspaceSources.Canceled && row.FinishedAtUtc > drainBefore, token))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_GIT_DRAINING");
        // Unknown dispatch is a database terminal, not a host stop receipt.
        if (await (from run in db.WorkspaceRunIntents
                   join job in db.OperationJobs on run.Id equals job.Id
                   where run.WorkspaceId == workspaceId &&
                    (run.FinishedAtUtc == null || run.Lease != null || run.ReportStage == "Collect" || run.ReportStage == "Retire" ||
                     job.Phase == "ResultUnknown" || job.StartedAtUtc != null && job.ResultJson == null &&
                        job.Phase != "CanceledBeforeDispatch" && job.Phase != "AgentUnavailable")
                   select run.Id).AnyAsync(token) ||
            await db.WorkspaceSourceIntents.AnyAsync(row => row.WorkspaceId == workspaceId &&
                (row.FinishedAtUtc == null || row.Lease != null), token))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_NOT_IDLE");
    }

    public async Task<WorkspaceLifecycleView?> ReplayAsync(TerminalActor actor, Guid workspaceId, Guid key,
        WorkspaceLifecycleCommand command, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row =>
            row.ActorSubject == actor.Subject && row.IdempotencyKey == key.ToString("D"), token);
        if (job is null) return null;
        if (job.JobType != WorkspaceLifecycle.JobType || job.ResourceId != workspaceId ||
            !JobRepository.JsonRequestsEqual(job.RequestJson, Request(workspaceId, command)))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_KEY_CONFLICT");
        var intent = await db.WorkspaceLifecycleIntents.AsNoTracking().SingleAsync(row => row.Id == job.Id && row.WorkspaceId == workspaceId, token);
        return View(intent, job);
    }

    public Task<WorkspaceLifecycleView> AdmitAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key,
        WorkspaceLifecycleCommand command, WorkspaceLifecyclePlan plan, CancellationToken token) => TransactionAsync(async () =>
    {
        var current = await LockWorkspaceAsync(workspace.Id, token);
        var replay = await ReplayAsync(actor, workspace.Id, key, command, token);
        if (replay is not null) return replay;
        if (current.LifecycleState != workspace.LifecycleState || current.LifecycleVersion != plan.ExpectedVersion ||
            workspace.LifecycleVersion != plan.ExpectedVersion ||
            current.Version != workspace.Version || current.State != WorkspaceStorageAllocation.Allocated ||
            current.EnvironmentSha256 != workspace.EnvironmentSha256 || current.OwnerSubject != workspace.OwnerSubject ||
            current.StorageRootId != workspace.StorageRootId || plan.WorkspaceId != workspace.Id || plan.Action != command.Action ||
            plan.PlanSha256 != command.PlanSha256 || current.LifecycleState != (command.Action == "stop" ? "Active" : "Stopped"))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_PLAN_CHANGED");
        await RequireIdleAsync(workspace.Id, token);
        if (await db.WorkspaceLifecycleIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_ALREADY_PENDING");
        var now = clock.GetUtcNow();
        var job = new OperationJob
        {
            JobType = WorkspaceLifecycle.JobType, ActorSubject = actor.Subject, ResourceId = workspace.Id,
            IdempotencyKey = key.ToString("D"), RequestJson = Request(workspace.Id, command), CreatedAtUtc = now
        };
        var row = new WorkspaceLifecycleIntent
        {
            Id = job.Id, WorkspaceId = workspace.Id, Action = command.Action, ExpectedVersion = plan.ExpectedVersion,
            SnapshotJson = JsonSerializer.Serialize(new Snapshot(workspace, plan), Json),
            ExecutionSubject = actor.Subject, LoginBinding = actor.LoginBinding, AvailableAtUtc = now
        };
        current.LifecycleState = command.Action == "stop" ? WorkspaceLifecycle.Stopping : WorkspaceLifecycle.Resuming;
        current.LifecycleVersion++;
        db.OperationJobs.Add(job);
        db.WorkspaceLifecycleIntents.Add(row);
        Event(job, "WORKSPACE_LIFECYCLE_QUEUED", now);
        Audit(actor, row, "request", now);
        return View(row, job);
    }, token);

    public async Task<WorkspaceLifecycleView?> CurrentAsync(Guid workspaceId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceLifecycleIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId && job.JobType == WorkspaceLifecycle.JobType
                          orderby job.CreatedAtUtc descending, job.Id descending
                          select new { Row = row, Job = job }).FirstOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }
    public async Task<WorkspaceLifecycleView?> GetAsync(Guid workspaceId, Guid operationId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceLifecycleIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId && row.Id == operationId && job.JobType == WorkspaceLifecycle.JobType
                          select new { Row = row, Job = job }).SingleOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }

    public Task<WorkspaceLifecycleView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid operationId, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var row = await LockIntentAsync(workspaceId, operationId, token);
            var job = await JobAsync(row, token);
            if (row.FinishedAtUtc is not null) return View(row, job);
            if (row.LeaseUntilUtc > clock.GetUtcNow()) throw new InvalidOperationException("WORKSPACE_LIFECYCLE_WORKER_BUSY");
            row.ExecutionSubject = actor.Subject;
            row.LoginBinding = actor.LoginBinding;
            row.AvailableAtUtc = clock.GetUtcNow();
            job.ErrorCode = null;
            job.Version++;
            Event(job, "WORKSPACE_LIFECYCLE_REAUTHORIZED", clock.GetUtcNow());
            Audit(actor, row, "reauthorize", clock.GetUtcNow());
            return View(row, job);
        }, token);

    public Task<WorkspaceLifecycleClaim?> ClaimAsync(CancellationToken token) => TransactionAsync<WorkspaceLifecycleClaim?>(async () =>
    {
        var now = clock.GetUtcNow();
        var candidates = await db.WorkspaceLifecycleIntents.FromSqlInterpolated($"""
            SELECT * FROM development_workspace_lifecycle WHERE finished_at_utc IS NULL AND available_at_utc <= {now}
              AND (lease_until_utc IS NULL OR lease_until_utc <= {now})
            ORDER BY available_at_utc, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToArrayAsync(token);
        if (candidates.Length == 0) return null;
        var row = candidates[0];
        var job = await JobAsync(row, token);
        var snapshot = ReadSnapshot(row);
        var lease = Guid.NewGuid();
        row.Lease = lease;
        row.LeaseUntilUtc = now.AddSeconds(90);
        job.StartedAtUtc ??= now;
        job.State = "Running";
        job.Phase = "HostConfirmation";
        job.Version++;
        Event(job, "WORKSPACE_LIFECYCLE_HOST_CONFIRMATION", now);
        return new(View(row, job), snapshot.Workspace, snapshot.Plan,
            new(row.ExecutionSubject, null, [], row.LoginBinding, row.Id.ToString("N"), null), lease);
    }, token);

    public Task CompleteAsync(WorkspaceLifecycleClaim claim, WorkspaceLifecycleHostProof proof, CancellationToken token) => TransactionAsync(async () =>
    {
        var workspace = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(workspace.Id, claim.View.OperationId, token);
        if (!Owns(row, claim)) return false;
        var snapshot = ReadSnapshot(row);
        var target = row.Action == "stop" ? WorkspaceLifecycle.Stopped : WorkspaceLifecycle.Active;
        var expected = row.Action == "stop" ? WorkspaceLifecycle.Stopping : WorkspaceLifecycle.Resuming;
        if (workspace.LifecycleState != expected || workspace.LifecycleVersion != row.ExpectedVersion + 1 ||
            proof.WorkspaceId != workspace.Id || proof.Action != row.Action || proof.State != target ||
            proof.Epoch != row.ExpectedVersion + 1 || proof.PlanSha256 != snapshot.Plan.HostPlanSha256 ||
            proof.StorageProofSha256 != snapshot.Plan.StorageProofSha256)
            throw new InvalidDataException("WORKSPACE_LIFECYCLE_RECEIPT_INVALID");
        await RequireIdleAsync(workspace.Id, token);
        var job = await JobAsync(row, token);
        var now = clock.GetUtcNow();
        workspace.LifecycleState = target;
        row.FinishedAtUtc = job.CompletedAtUtc = now;
        row.Lease = null;
        row.LeaseUntilUtc = null;
        job.State = "Succeeded";
        job.Phase = target;
        job.ResultJson = JsonSerializer.Serialize(proof, Json);
        job.ErrorCode = null;
        job.Version++;
        Event(job, "WORKSPACE_LIFECYCLE_CONFIRMED", now);
        Audit(claim.Actor, row, "complete", now);
        return true;
    }, token);

    public Task DeferAsync(WorkspaceLifecycleClaim claim, string code, CancellationToken token) => TransactionAsync(async () =>
    {
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.OperationId, token);
        if (!Owns(row, claim)) return false;
        var job = await JobAsync(row, token);
        row.Lease = null;
        row.LeaseUntilUtc = null;
        row.AvailableAtUtc = clock.GetUtcNow().AddSeconds(5);
        job.ErrorCode = code;
        return true;
    }, token);

    private async Task<DevelopmentWorkspaceRecord> LockWorkspaceAsync(Guid id, CancellationToken token) =>
        (await db.DevelopmentWorkspaces.FromSqlInterpolated($"SELECT * FROM development_workspaces WHERE id = {id} FOR UPDATE")
            .ToArrayAsync(token)).SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    private async Task<WorkspaceLifecycleIntent> LockIntentAsync(Guid workspaceId, Guid id, CancellationToken token) =>
        (await db.WorkspaceLifecycleIntents.FromSqlInterpolated($"""
            SELECT * FROM development_workspace_lifecycle WHERE id = {id} AND workspace_id = {workspaceId} FOR UPDATE
            """).ToArrayAsync(token)).SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_LIFECYCLE_NOT_FOUND");
    private Task<OperationJob> JobAsync(WorkspaceLifecycleIntent row, CancellationToken token) =>
        db.OperationJobs.SingleAsync(job => job.Id == row.Id && job.JobType == WorkspaceLifecycle.JobType && job.ResourceId == row.WorkspaceId, token);
    private bool Owns(WorkspaceLifecycleIntent row, WorkspaceLifecycleClaim claim) =>
        row.FinishedAtUtc is null && row.Lease == claim.Lease && row.LeaseUntilUtc > clock.GetUtcNow();

    private static Snapshot ReadSnapshot(WorkspaceLifecycleIntent row)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(row.SnapshotJson, Json)
            ?? throw new InvalidDataException("WORKSPACE_LIFECYCLE_SNAPSHOT_INVALID");
        if (snapshot.Workspace.Id != row.WorkspaceId || snapshot.Workspace.LifecycleVersion != row.ExpectedVersion ||
            snapshot.Plan.ExpectedVersion != row.ExpectedVersion || snapshot.Plan.WorkspaceId != row.WorkspaceId || snapshot.Plan.Action != row.Action ||
            WorkspaceLifecycle.BuildPlan(snapshot.Workspace, new(row.WorkspaceId, row.Action, row.ExpectedVersion,
                snapshot.Workspace.LifecycleState, snapshot.Plan.HostPlanSha256, snapshot.Plan.StorageProofSha256)) != snapshot.Plan)
            throw new InvalidDataException("WORKSPACE_LIFECYCLE_SNAPSHOT_INVALID");
        return snapshot;
    }
    private Task<T> TransactionAsync<T>(Func<Task<T>> change, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SELECT pg_advisory_xact_lock(41307420)", token);
        var result = await change();
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return result;
    });
    private static string Request(Guid id, WorkspaceLifecycleCommand command) => JsonSerializer.Serialize(new { workspaceId = id, command }, Json);
    private static WorkspaceLifecycleView View(WorkspaceLifecycleIntent row, OperationJob job) =>
        new(row.Id, row.WorkspaceId, row.Action, job.State, job.Phase, job.ErrorCode, row.FinishedAtUtc);
    private void Event(OperationJob job, string code, DateTimeOffset at) => db.OperationJobEvents.Add(new()
    { JobId = job.Id, Sequence = job.Version + 1, State = job.State, Phase = job.Phase, MessageCode = code, OccurredAtUtc = at });
    private void Audit(TerminalActor actor, WorkspaceLifecycleIntent row, string action, DateTimeOffset at) => db.AuditEvents.Add(new()
    {
        ActorSubject = actor.Subject, Action = "development.workspace.lifecycle." + action, TargetType = "workspace",
        TargetId = row.WorkspaceId.ToString("D"), JobId = row.Id, TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = at
    });
}
