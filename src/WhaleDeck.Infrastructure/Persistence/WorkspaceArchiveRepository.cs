using System.Text.Json;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceArchiveRepository(PlatformDbContext db, TimeProvider clock,
    IWorkspaceLifecycleStore lifecycle) : IWorkspaceArchiveStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<WorkspaceArchiveView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key, long epoch, CancellationToken token) =>
        TransactionAsync(async () =>
        {
            var current = await LockWorkspaceAsync(workspace.Id, token);
            var request = JsonSerializer.Serialize(new { workspaceId = workspace.Id, expectedEpoch = epoch }, Json);
            var replay = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row => row.ActorSubject == actor.Subject && row.IdempotencyKey == key.ToString("D"), token);
            if (replay is not null)
            {
                if (replay.JobType != WorkspaceArchives.JobType || replay.ResourceId != workspace.Id || !JobRepository.JsonRequestsEqual(replay.RequestJson, request))
                    throw new InvalidOperationException("WORKSPACE_ARCHIVE_KEY_CONFLICT");
                return View(await db.WorkspaceArchiveIntents.AsNoTracking().SingleAsync(row => row.Id == replay.Id, token), replay);
            }
            var latest = DevelopmentWorkspaceRegistry.Map(current);
            if (latest.State != WorkspaceStorageAllocation.Allocated || latest.LifecycleState != WorkspaceLifecycle.Stopped ||
                latest.LifecycleVersion != epoch || JsonSerializer.Serialize(latest, Json) != JsonSerializer.Serialize(workspace, Json))
                throw new InvalidOperationException("WORKSPACE_ARCHIVE_STATE_CHANGED");
            await lifecycle.RequireIdleAsync(workspace.Id, token);
            if (await db.WorkspaceArchiveIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token) ||
                await db.WorkspaceLifecycleIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token))
                throw new InvalidOperationException("WORKSPACE_ARCHIVE_ALREADY_PENDING");
            var now = clock.GetUtcNow();
            var job = new OperationJob { JobType = WorkspaceArchives.JobType, ActorSubject = actor.Subject, ResourceId = workspace.Id,
                IdempotencyKey = key.ToString("D"), RequestJson = request, CreatedAtUtc = now, Phase = "Plan" };
            var intent = new WorkspaceArchiveIntent { Id = job.Id, WorkspaceId = workspace.Id, ExpectedEpoch = epoch,
                SnapshotJson = JsonSerializer.Serialize(workspace, Json), ExecutionSubject = actor.Subject, LoginBinding = actor.LoginBinding, AvailableAtUtc = now };
            db.OperationJobs.Add(job); db.WorkspaceArchiveIntents.Add(intent);
            AddStep(intent, workspace);
            Event(job, "WORKSPACE_ARCHIVE_PLAN_QUEUED", now); Audit(actor, intent, "request", now);
            return View(intent, job);
        }, token);

    public async Task<WorkspaceArchiveView?> CurrentAsync(Guid workspaceId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceArchiveIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId orderby job.CreatedAtUtc descending, row.Id descending
                          select new { Row = row, Job = job }).FirstOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }
    public async Task<WorkspaceArchiveView?> GetAsync(Guid workspaceId, Guid archiveId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceArchiveIntents.AsNoTracking()
                          join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
                          where row.WorkspaceId == workspaceId && row.Id == archiveId
                          select new { Row = row, Job = job }).SingleOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }
    public Task<WorkspaceArchiveView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, string planSha256, CancellationToken token) =>
        ChangeAsync(workspaceId, archiveId, async (row, job) =>
        {
            if (row.PlanJson is null || Digest(row.PlanJson, "planSha256") != planSha256 || row.CancelRequested)
                throw new InvalidOperationException("WORKSPACE_ARCHIVE_PLAN_CHANGED");
            if (!row.AwaitingConfirmation) return View(row, job); // Lost confirmation response: never create another step.
            RequireNoLease(row);
            row.AwaitingConfirmation = false; row.AuthorizationRequired = false;
            row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding;
            row.Phase = "capture"; row.AvailableAtUtc = clock.GetUtcNow();
            AddStep(row, ReadSnapshot(row));
            job.Phase = "Capture"; job.ErrorCode = null; job.Version++;
            Event(job, "WORKSPACE_ARCHIVE_CONFIRMED", clock.GetUtcNow()); Audit(actor, row, "confirm", clock.GetUtcNow());
            await Task.CompletedTask;
            return View(row, job);
        }, token);

    public Task<WorkspaceArchiveView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, CancellationToken token) =>
        ChangeAsync(workspaceId, archiveId, (row, job) =>
        {
            if (row.FinishedAtUtc is null && !row.CancelRequested)
            {
                row.CancelRequested = true; row.AvailableAtUtc = clock.GetUtcNow();
                job.CancelRequestedAtUtc = clock.GetUtcNow(); job.Version++;
                Event(job, "WORKSPACE_ARCHIVE_CANCEL_REQUESTED", clock.GetUtcNow()); Audit(actor, row, "cancel", clock.GetUtcNow());
            }
            return Task.FromResult(View(row, job));
        }, token);

    public Task<WorkspaceArchiveView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid archiveId, CancellationToken token) =>
        ChangeAsync(workspaceId, archiveId, (row, job) =>
        {
            if (row.FinishedAtUtc is not null || row.CancelRequested) return Task.FromResult(View(row, job));
            RequireNoLease(row);
            row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding;
            row.AuthorizationRequired = false; row.AvailableAtUtc = clock.GetUtcNow(); job.ErrorCode = null; job.Version++;
            Event(job, "WORKSPACE_ARCHIVE_REAUTHORIZED", clock.GetUtcNow()); Audit(actor, row, "reauthorize", clock.GetUtcNow());
            return Task.FromResult(View(row, job));
        }, token);

    public Task<WorkspaceArchiveClaim?> ClaimAsync(CancellationToken token) => TransactionAsync<WorkspaceArchiveClaim?>(async () =>
    {
        var now = clock.GetUtcNow();
        // Choose without row locks, then follow budget -> workspace -> intent.
        var selected = await db.WorkspaceArchiveIntents.AsNoTracking().Where(row => row.FinishedAtUtc == null && row.AvailableAtUtc <= now &&
            (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= now) &&
            (row.CancelRequested || !row.AwaitingConfirmation && !row.AuthorizationRequired))
            .OrderBy(row => row.AvailableAtUtc).ThenBy(row => row.Id).FirstOrDefaultAsync(token);
        if (selected is null) return null;
        _ = await LockWorkspaceAsync(selected.WorkspaceId, token);
        var row = await LockIntentAsync(selected.WorkspaceId, selected.Id, token);
        if (row.FinishedAtUtc is not null || row.LeaseUntilUtc > now || row.AvailableAtUtc > now ||
            !row.CancelRequested && (row.AwaitingConfirmation || row.AuthorizationRequired)) return null;
        var job = await JobAsync(row, token); var snapshot = ReadSnapshot(row);
        var step = await StepAsync(row, token); var task = ReadTask(row, step); ValidateRequest(snapshot, task, step);
        var lease = Guid.NewGuid(); row.Lease = lease; row.LeaseUntilUtc = now.AddSeconds(90);
        job.StartedAtUtc ??= now; job.State = "Running";
        return new(View(row, job), snapshot, task, new(row.ExecutionSubject, null, [], row.LoginBinding, step.Id.ToString("N"), null), lease);
    }, token);

    public Task<bool> MarkDispatchAsync(WorkspaceArchiveClaim claim, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.ArchiveId, token);
        if (!Owns(row, claim) || row.CancelRequested) return false;
        var step = await StepAsync(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        if (step.DispatchedAtUtc is not null) return false;
        step.DispatchedAtUtc = clock.GetUtcNow();
        return true;
    }, token);

    public Task RecordAsync(WorkspaceArchiveClaim claim, WorkspaceArchiveTaskProof proof, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.ArchiveId, token);
        if (!Owns(row, claim)) return false;
        var step = await StepAsync(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        var request = AgentGateway.BuildArchiveRequest(claim.Actor, claim.Workspace, claim.Task);
        var receipt = new WorkspaceArchiveReceipt { TaskId = proof.TaskId.ToString("D"), RequestSha256 = proof.RequestSha256,
            Status = proof.Status, Code = proof.Code ?? "", ResultJson = proof.ResultJson is null ? ByteString.Empty : ByteString.CopyFromUtf8(proof.ResultJson) };
        if (!DevelopmentArchiveBinding.Matches(request, receipt)) throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Integrity);
        step.ProofJson = JsonSerializer.Serialize(proof, Json);
        var job = await JobAsync(row, token); var now = clock.GetUtcNow();
        var settled = proof.Status is "Succeeded" or "Rejected" or "Interrupted" or "Stopped";
        if (settled) step.SettledAtUtc = now;
        if (proof.Status == "Succeeded")
        {
            if (row.Phase == "plan") row.PlanJson = proof.ResultJson;
            else if (row.Phase == "capture") row.CaptureJson = proof.ResultJson;
            else row.VerifyJson = proof.ResultJson;
        }
        if (settled && row.CancelRequested) Finish(row, job, "Canceled", "Canceled", "WORKSPACE_ARCHIVE_CANCELED", now);
        else if (settled && proof.Status != "Succeeded") Finish(row, job, "Failed", proof.Status, proof.Code ?? "WORKSPACE_ARCHIVE_" + proof.Status.ToUpperInvariant(), now);
        else if (proof.Status == "Succeeded" && row.Phase == "plan")
        {
            row.AwaitingConfirmation = true; job.Phase = "AwaitingConfirmation"; job.ErrorCode = null; job.Version++;
            Event(job, "WORKSPACE_ARCHIVE_PLAN_READY", now);
        }
        else if (proof.Status == "Succeeded" && row.Phase == "capture")
        {
            // This transaction durably saves the capture proof before a new
            // independent Verify task becomes visible to another Worker.
            row.Phase = "verify"; AddStep(row, ReadSnapshot(row)); job.Phase = "Verify"; job.ErrorCode = null; job.Version++;
            Event(job, "WORKSPACE_ARCHIVE_VERIFY_QUEUED", now);
        }
        else if (proof.Status == "Succeeded") Finish(row, job, "Succeeded", "Archived", null, now);
        else job.ErrorCode = row.CancelRequested ? "WORKSPACE_ARCHIVE_STOP_UNCONFIRMED" : null;
        Release(row, now);
        if (settled) Audit(claim.Actor, row, "settled", now);
        return true;
    }, token);

    public Task<bool> AuthorizationLostAsync(WorkspaceArchiveClaim claim, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.ArchiveId, token);
        if (!Owns(row, claim)) return false;
        var step = await StepAsync(row, token); var job = await JobAsync(row, token);
        row.AuthorizationRequired = true; job.ErrorCode = "WORKSPACE_ARCHIVE_AUTHORIZATION_REQUIRED";
        Audit(claim.Actor, row, "authorization-required", clock.GetUtcNow());
        if (step.DispatchedAtUtc is not null) { row.CancelRequested = true; return true; }
        Release(row, clock.GetUtcNow());
        return false;
    }, token);

    public Task DeferAsync(WorkspaceArchiveClaim claim, string code, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.ArchiveId, token);
        if (!Owns(row, claim)) return false;
        (await JobAsync(row, token)).ErrorCode = code; Release(row, clock.GetUtcNow());
        return true;
    }, token);

    public Task DeferUndispatchedAsync(WorkspaceArchiveClaim claim, string code, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(claim.Workspace.Id, token);
        var row = await LockIntentAsync(claim.Workspace.Id, claim.View.ArchiveId, token);
        if (!Owns(row, claim)) return false;
        var step = await StepAsync(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        if (step.ProofJson is not null || step.SettledAtUtc is not null) return false;
        // The current lease holder has established that no archive RPC was
        // invoked. Never clear a fence for an ambiguous or expired execution.
        step.DispatchedAtUtc = null;
        (await JobAsync(row, token)).ErrorCode = code; Release(row, clock.GetUtcNow());
        return true;
    }, token);

    private Task<WorkspaceArchiveView> ChangeAsync(Guid workspaceId, Guid id,
        Func<WorkspaceArchiveIntent, OperationJob, Task<WorkspaceArchiveView>> change, CancellationToken token) => TransactionAsync(async () =>
    {
        _ = await LockWorkspaceAsync(workspaceId, token);
        var row = await LockIntentAsync(workspaceId, id, token);
        return await change(row, await JobAsync(row, token));
    }, token);
    private async Task<DevelopmentWorkspaceRecord> LockWorkspaceAsync(Guid id, CancellationToken token) =>
        (await db.DevelopmentWorkspaces.FromSqlInterpolated($"SELECT * FROM development_workspaces WHERE id = {id} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    private async Task<WorkspaceArchiveIntent> LockIntentAsync(Guid workspaceId, Guid id, CancellationToken token) =>
        (await db.WorkspaceArchiveIntents.FromSqlInterpolated($"SELECT * FROM development_workspace_archives WHERE id = {id} AND workspace_id = {workspaceId} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_ARCHIVE_NOT_FOUND");
    private Task<OperationJob> JobAsync(WorkspaceArchiveIntent row, CancellationToken token) => db.OperationJobs.SingleAsync(job => job.Id == row.Id && job.JobType == WorkspaceArchives.JobType, token);
    private Task<WorkspaceArchiveStep> StepAsync(WorkspaceArchiveIntent row, CancellationToken token) =>
        db.WorkspaceArchiveSteps.SingleAsync(step => step.ArchiveId == row.Id && step.Phase == row.Phase, token);
    private static WorkspaceDefinition ReadSnapshot(WorkspaceArchiveIntent row)
    {
        var value = JsonSerializer.Deserialize<WorkspaceDefinition>(row.SnapshotJson, Json) ?? throw new InvalidDataException("WORKSPACE_ARCHIVE_SNAPSHOT_INVALID");
        if (value.Id != row.WorkspaceId || value.State != WorkspaceStorageAllocation.Allocated || value.LifecycleState != WorkspaceLifecycle.Stopped || value.LifecycleVersion != row.ExpectedEpoch)
            throw new InvalidDataException("WORKSPACE_ARCHIVE_SNAPSHOT_INVALID");
        return value;
    }
    private static WorkspaceArchiveTask ReadTask(WorkspaceArchiveIntent row, WorkspaceArchiveStep step) =>
        new(step.Id, row.Id, step.Phase, row.ExpectedEpoch, step.Phase == "plan" ? "" : Digest(row.PlanJson!, "planSha256"),
            step.Phase == "verify" ? Digest(row.CaptureJson!, "receiptSha256") : "");
    private void AddStep(WorkspaceArchiveIntent row, WorkspaceDefinition workspace)
    {
        var step = new WorkspaceArchiveStep { Id = Guid.NewGuid(), ArchiveId = row.Id, Phase = row.Phase, RequestJson = "" };
        var task = ReadTask(row, step);
        step = new WorkspaceArchiveStep { Id = step.Id, ArchiveId = row.Id, Phase = row.Phase, RequestJson = Canonical(workspace, task) };
        db.WorkspaceArchiveSteps.Add(step);
    }
    private static string Canonical(WorkspaceDefinition workspace, WorkspaceArchiveTask task)
    {
        var request = AgentGateway.BuildArchiveRequest(new(workspace.OwnerSubject, null, [], new string('a', 64), "archive-validation", null), workspace, task);
        if (!DevelopmentArchiveBinding.IsValid(request)) throw new InvalidDataException("WORKSPACE_ARCHIVE_REQUEST_INVALID");
        return DevelopmentArchiveBinding.HostInput(request);
    }
    private static void ValidateRequest(WorkspaceDefinition workspace, WorkspaceArchiveTask task, WorkspaceArchiveStep step)
    {
        if (step.Id != task.TaskId || step.ArchiveId != task.ArchiveId || step.Phase != task.Phase || step.RequestJson != Canonical(workspace, task))
            throw new InvalidDataException("WORKSPACE_ARCHIVE_REQUEST_CHANGED");
    }
    private static string Digest(string json, string name) { using var value = JsonDocument.Parse(json); return value.RootElement.GetProperty(name).GetString()!; }
    private bool Owns(WorkspaceArchiveIntent row, WorkspaceArchiveClaim claim) => row.FinishedAtUtc is null && row.Lease == claim.Lease &&
        row.LeaseUntilUtc > clock.GetUtcNow() && row.Phase == claim.Task.Phase;
    private void RequireNoLease(WorkspaceArchiveIntent row)
    {
        if (row.LeaseUntilUtc > clock.GetUtcNow()) throw new InvalidOperationException("WORKSPACE_ARCHIVE_WORKER_BUSY");
    }
    private static void Release(WorkspaceArchiveIntent row, DateTimeOffset now)
    { row.Lease = null; row.LeaseUntilUtc = null; row.AvailableAtUtc = now.AddSeconds(3); }
    private void Finish(WorkspaceArchiveIntent row, OperationJob job, string state, string phase, string? code, DateTimeOffset now)
    {
        row.FinishedAtUtc = job.CompletedAtUtc = now; row.AwaitingConfirmation = false;
        job.State = state; job.Phase = phase; job.ErrorCode = code; job.Version++;
        job.ResultJson = JsonSerializer.Serialize(new { archiveId = row.Id, retainsSource = true, reservesBudget = true,
            verified = state == "Succeeded", partialHddDataMayRemain = state != "Succeeded" }, Json);
        Event(job, "WORKSPACE_ARCHIVE_SETTLED", now);
    }
    private static WorkspaceArchiveView View(WorkspaceArchiveIntent row, OperationJob job) => new(row.Id, row.WorkspaceId,
        job.State, job.Phase, job.ErrorCode, row.AwaitingConfirmation, row.AuthorizationRequired, row.CancelRequested,
        row.PlanJson, row.CaptureJson, row.VerifyJson, row.FinishedAtUtc);
    private void Event(OperationJob job, string code, DateTimeOffset now) => db.OperationJobEvents.Add(new()
    { JobId = job.Id, Sequence = job.Version + 1, State = job.State, Phase = job.Phase, MessageCode = code, OccurredAtUtc = now });
    private void Audit(TerminalActor actor, WorkspaceArchiveIntent row, string action, DateTimeOffset now) => db.AuditEvents.Add(new()
    { ActorSubject = actor.Subject, Action = "development.workspace.archive." + action, TargetType = "workspace",
        TargetId = row.WorkspaceId.ToString("D"), JobId = row.Id, TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = now });
    private Task<T> TransactionAsync<T>(Func<Task<T>> change, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SELECT pg_advisory_xact_lock(41307420)", token);
        var result = await change(); await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return result;
    });
}
