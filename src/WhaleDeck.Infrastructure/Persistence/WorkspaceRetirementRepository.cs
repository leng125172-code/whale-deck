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

public sealed partial class WorkspaceRetirementRepository(PlatformDbContext db, TimeProvider clock,
    IWorkspaceLifecycleStore lifecycle) : IWorkspaceRetirementStore
{
    private static readonly string[] ExecutionDigests = ["storageProofSha256", "sourceManifestSha256", "captureReceiptSha256"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public Task<WorkspaceRetirementView> BeginAsync(TerminalActor actor, WorkspaceDefinition workspace, Guid key,
        Guid archiveId, long epoch, CancellationToken token) => Transaction(async () =>
    {
        if (key == Guid.Empty || archiveId == Guid.Empty) throw new ArgumentException("WORKSPACE_RETIREMENT_REQUEST_INVALID");
        var current = await LockWorkspace(workspace.Id, token);
        var request = JsonSerializer.Serialize(new { workspaceId = workspace.Id, archiveId, expectedEpoch = epoch }, Json);
        var replay = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(row => row.ActorSubject == actor.Subject && row.IdempotencyKey == key.ToString("D"), token);
        if (replay is not null)
        {
            if (replay.JobType != WorkspaceRetirements.JobType || replay.ResourceId != workspace.Id || !JobRepository.JsonRequestsEqual(request, replay.RequestJson))
                throw new InvalidOperationException("WORKSPACE_RETIREMENT_KEY_CONFLICT");
            return View(await db.WorkspaceRetirementWorkflows.AsNoTracking().SingleAsync(row => row.Id == replay.Id, token), replay);
        }
        if (current.LifecycleState != WorkspaceLifecycle.Stopped || current.State != WorkspaceStorageAllocation.Allocated ||
            epoch != workspace.LifecycleVersion || !JobRepository.JsonRequestsEqual(JsonSerializer.Serialize(DevelopmentWorkspaceRegistry.Map(current), Json), JsonSerializer.Serialize(workspace, Json)))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_STATE_CHANGED");
        await lifecycle.RequireIdleAsync(workspace.Id, token);
        if (await db.WorkspaceRetirementIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token) ||
            await db.WorkspaceRetirementWorkflows.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token) ||
            await db.WorkspaceLifecycleIntents.AnyAsync(row => row.WorkspaceId == workspace.Id && row.FinishedAtUtc == null, token))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_ALREADY_PENDING");
        var evidence = await WorkspaceRetirementPreparationRepository.Evidence(db, workspace, archiveId, token);
        var now = Now();
        var job = new OperationJob { JobType = WorkspaceRetirements.JobType, ActorSubject = actor.Subject, ResourceId = workspace.Id,
            IdempotencyKey = key.ToString("D"), RequestJson = request, CreatedAtUtc = now, Phase = "Plan" };
        var row = new WorkspaceRetirementWorkflow { Id = job.Id, WorkspaceId = workspace.Id, ArchiveId = archiveId, ExpectedEpoch = epoch,
            SnapshotJson = JsonSerializer.Serialize(workspace, Json), ArchivePlanSha256 = evidence.Request.PlanSha256,
            CaptureReceiptSha256 = evidence.Request.ReceiptSha256, ExecutionSubject = actor.Subject, LoginBinding = actor.LoginBinding, AvailableAtUtc = now };
        current.LifecycleState = WorkspaceRetirementPreparations.Retiring;
        db.OperationJobs.Add(job); db.WorkspaceRetirementWorkflows.Add(row); AddStep(row);
        Event(job, "WORKSPACE_RETIREMENT_QUEUED", now); Audit(actor, row, "request", now);
        return View(row, job);
    }, token);

    public async Task<WorkspaceRetirementView?> CurrentAsync(Guid workspaceId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceRetirementWorkflows.AsNoTracking() join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
            where row.WorkspaceId == workspaceId orderby job.CreatedAtUtc descending, row.Id descending select new { Row = row, Job = job }).FirstOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }
    public async Task<WorkspaceRetirementView?> GetAsync(Guid workspaceId, Guid retirementId, CancellationToken token)
    {
        var pair = await (from row in db.WorkspaceRetirementWorkflows.AsNoTracking() join job in db.OperationJobs.AsNoTracking() on row.Id equals job.Id
            where row.WorkspaceId == workspaceId && row.Id == retirementId select new { Row = row, Job = job }).SingleOrDefaultAsync(token);
        return pair is null ? null : View(pair.Row, pair.Job);
    }
    public Task<WorkspaceRetirementView> ConfirmAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, string planSha256, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token); var job = await Job(row, token);
        if (row.PlanJson is null || Property(row.PlanJson, "retirementPlanSha256") != planSha256)
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_PLAN_CHANGED");
        if (row.ConfirmedAtUtc is not null) return View(row, job); // Preserve an accepted confirmation after a lost reply.
        if (row.CancelRequested) throw new InvalidOperationException("WORKSPACE_RETIREMENT_PLAN_CHANGED");
        _ = RequireFence(current, row); RequireNoLease(row);
        if (row.PlanExpiresAtUtc <= Now() || row.FinishedAtUtc is not null || row.AttentionRequired)
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_PLAN_EXPIRED");
        _ = await Evidence(row, token);
        var now = Now(); row.ConfirmedAtUtc = now; row.Phase = "execute"; row.AvailableAtUtc = now;
        row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding; row.AuthorizationRequired = false; AddStep(row);
        job.Phase = "Execute"; job.ErrorCode = null; job.Version++;
        Event(job, "WORKSPACE_RETIREMENT_CONFIRMED", now); Audit(actor, row, "confirm-delete-source", now); return View(row, job);
    }, token);
    public Task<WorkspaceRetirementView> CancelAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token); var job = await Job(row, token);
        if (row.FinishedAtUtc is not null) return View(row, job);
        _ = RequireFence(current, row);
        if (!row.CancelRequested)
        {
            row.CancelRequested = true; row.AvailableAtUtc = Now(); job.CancelRequestedAtUtc = Now(); job.Version++;
            Event(job, "WORKSPACE_RETIREMENT_CANCEL_REQUESTED", Now()); Audit(actor, row, "cancel", Now());
        }
        var step = await Step(row, token);
        if (row.ExecutionAttempt == 0 && (row.LeaseUntilUtc is null || row.LeaseUntilUtc <= Now()) &&
            (step.DispatchedAtUtc is null || row.Phase == "plan" && step.SettledAtUtc is not null))
        {
            current.LifecycleState = WorkspaceLifecycle.Stopped;
            Finish(row, job, "Canceled", "CanceledBeforeExecution", null, Now()); Release(row, Now());
            Audit(actor, row, "canceled-before-execution", Now());
        }
        return View(row, job);
    }, token);
    public Task<WorkspaceRetirementView> ReauthorizeAsync(TerminalActor actor, Guid workspaceId, Guid retirementId, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(workspaceId, token); var row = await LockIntent(workspaceId, retirementId, token); var job = await Job(row, token);
        if (row.FinishedAtUtc is not null || row.CancelRequested || row.AttentionRequired) return View(row, job);
        _ = RequireFence(current, row); RequireNoLease(row); _ = await Evidence(row, token);
        row.ExecutionSubject = actor.Subject; row.LoginBinding = actor.LoginBinding; row.AuthorizationRequired = false; row.AvailableAtUtc = Now();
        job.ErrorCode = null; job.Version++; Event(job, "WORKSPACE_RETIREMENT_REAUTHORIZED", Now()); Audit(actor, row, "reauthorize", Now());
        return View(row, job);
    }, token);

    public Task<WorkspaceRetirementClaim?> ClaimAsync(CancellationToken token) => Transaction<WorkspaceRetirementClaim?>(async () =>
    {
        var now = Now();
        var selected = await db.WorkspaceRetirementWorkflows.AsNoTracking().Where(row => row.FinishedAtUtc == null && !row.AttentionRequired &&
            row.AvailableAtUtc <= now && (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= now) &&
            (row.CancelRequested || !row.AuthorizationRequired && (row.Phase == "execute" || row.PlanJson == null)))
            .OrderBy(row => row.AvailableAtUtc).ThenBy(row => row.Id).FirstOrDefaultAsync(token);
        if (selected is null) return null;
        var current = await LockWorkspace(selected.WorkspaceId, token); var row = await LockIntent(selected.WorkspaceId, selected.Id, token);
        if (row.FinishedAtUtc is not null || row.AttentionRequired || row.AvailableAtUtc > now || row.LeaseUntilUtc > now ||
            !row.CancelRequested && (row.AuthorizationRequired || row.Phase == "plan" && row.PlanJson != null)) return null;
        var workspace = RequireFence(current, row); var step = await Step(row, token); var task = ReadTask(row, step); ValidateRequest(workspace, task, step);
        var job = await Job(row, token);
        if (row.ExecutionAttempt == 0 && row.CancelRequested && (step.DispatchedAtUtc is null || row.Phase == "plan" && step.SettledAtUtc is not null))
        {
            current.LifecycleState = WorkspaceLifecycle.Stopped;
            Finish(row, job, "Canceled", "CanceledBeforeDispatch", null, now); Release(row, now);
            Audit(new(row.ExecutionSubject, null, [], row.LoginBinding, step.Id.ToString("N"), null), row, "canceled-before-dispatch", now);
            return null;
        }
        var lease = Guid.NewGuid(); row.Lease = lease; row.LeaseUntilUtc = now.AddSeconds(90);
        job.StartedAtUtc ??= now; job.State = "Running";
        return new(View(row, job), workspace, task, new(row.ExecutionSubject, null, [], row.LoginBinding, step.Id.ToString("N"), null), lease);
    }, token);
    public Task<bool> MarkDispatchAsync(WorkspaceRetirementClaim claim, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(claim.Workspace.Id, token); var row = await LockIntent(claim.Workspace.Id, claim.View.RetirementId, token);
        if (!Owns(row, claim) || row.CancelRequested || row.AuthorizationRequired || row.AttentionRequired) return false;
        _ = RequireFence(current, row); var step = await Step(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        if (step.DispatchedAtUtc is not null) return false;
        _ = await Evidence(row, token); step.DispatchedAtUtc = Now(); return true;
    }, token);

    public Task RecordAsync(WorkspaceRetirementClaim claim, WorkspaceRetirementTaskProof proof, CancellationToken token) => Transaction(async () =>
    {
        var current = await LockWorkspace(claim.Workspace.Id, token); var row = await LockIntent(claim.Workspace.Id, claim.View.RetirementId, token);
        if (!Owns(row, claim)) return false;
        _ = RequireFence(current, row); var step = await Step(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        var request = AgentGateway.BuildRetirementRequest(claim.Actor, claim.Workspace, claim.Task);
        var receipt = new WorkspaceRetirementReceipt { TaskId = proof.TaskId.ToString("D"), RequestSha256 = proof.RequestSha256,
            Status = proof.Status, Code = proof.Code ?? "", ResultJson = proof.ResultJson is null ? ByteString.Empty : ByteString.CopyFromUtf8(proof.ResultJson) };
        if (!DevelopmentRetirementBinding.Matches(request, receipt) || proof.Status == "Succeeded" && step.DispatchedAtUtc is null)
            throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Integrity);
        var job = await Job(row, token); var now = Now(); var terminal = proof.Status is "Succeeded" or "Rejected" or "Stopped" or "Interrupted";
        if (proof.Status == "Succeeded")
        {
            var evidence = await Evidence(row, token);
            if (row.Phase == "plan")
            {
                if (!DevelopmentRetirementPlanBinding.Matches(evidence.Request, evidence.Receipt, proof.ResultJson!))
                    throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Integrity);
                row.PlanJson = proof.ResultJson; row.PlanExpiresAtUtc = now.AddMinutes(10);
            }
            else
            {
                using var plan = JsonDocument.Parse(row.PlanJson!); using var result = JsonDocument.Parse(proof.ResultJson!);
                foreach (var name in ExecutionDigests)
                    if (result.RootElement.GetProperty(name).GetString() != plan.RootElement.GetProperty(name).GetString())
                        throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Integrity);
                row.ExecutionJson = proof.ResultJson;
            }
        }
        step.ProofJson = JsonSerializer.Serialize(proof, Json); if (terminal) step.SettledAtUtc = now;
        if (row.Phase == "execute" && proof.Status == "Succeeded")
        {
            // Physical success is retained even after a late cancel/revocation.
            // It is never downgraded to a fictitious intact source or rerun.
            current.LifecycleState = WorkspaceRetirements.Retired;
            db.WorkspaceRetirementCompletions.Add(new() { WorkspaceId = row.WorkspaceId, RetirementId = row.Id, ExecuteStepId = step.Id, CompletedAtUtc = now });
            Finish(row, job, "Succeeded", "Retired", null, now); Audit(claim.Actor, row, "budget-released", now);
        }
        else if (terminal && row.Phase == "plan" && (row.CancelRequested || proof.Status != "Succeeded"))
        {
            current.LifecycleState = WorkspaceLifecycle.Stopped;
            Finish(row, job, row.CancelRequested ? "Canceled" : "Failed", row.CancelRequested ? "Canceled" : proof.Status, proof.Code, now);
        }
        else if (terminal && row.Phase == "execute")
        {
            // A stopped execution may have already unlinked source. Keep the
            // fence/budget and original terminal proof for explicit recovery.
            row.AttentionRequired = true; job.Phase = "AttentionRequired"; job.ErrorCode = proof.Code ?? "WORKSPACE_RETIREMENT_EXECUTION_INCOMPLETE"; job.Version++;
            Event(job, "WORKSPACE_RETIREMENT_ATTENTION_REQUIRED", now);
        }
        else if (proof.Status == "Succeeded")
        {
            job.Phase = "AwaitingConfirmation"; job.ErrorCode = null; job.Version++; Event(job, "WORKSPACE_RETIREMENT_PLAN_READY", now);
        }
        Release(row, now); if (terminal) Audit(claim.Actor, row, "settled", now); return true;
    }, token);

    public Task<bool> AuthorizationLostAsync(WorkspaceRetirementClaim claim, CancellationToken token) => Transaction(async () =>
    {
        _ = await LockWorkspace(claim.Workspace.Id, token); var row = await LockIntent(claim.Workspace.Id, claim.View.RetirementId, token);
        if (!Owns(row, claim)) return false; var step = await Step(row, token); var job = await Job(row, token);
        row.AuthorizationRequired = true; job.ErrorCode = "WORKSPACE_RETIREMENT_AUTHORIZATION_REQUIRED";
        Audit(claim.Actor, row, "authorization-required", Now());
        if (step.DispatchedAtUtc is not null) { row.CancelRequested = true; return true; }
        Release(row, Now()); return false;
    }, token);
    public Task DeferAsync(WorkspaceRetirementClaim claim, string code, CancellationToken token) => Defer(claim, code, false, token);
    public Task DeferUndispatchedAsync(WorkspaceRetirementClaim claim, string code, CancellationToken token) => Defer(claim, code, true, token);
    private Task<bool> Defer(WorkspaceRetirementClaim claim, string code, bool undispatched, CancellationToken token) => Transaction(async () =>
    {
        _ = await LockWorkspace(claim.Workspace.Id, token); var row = await LockIntent(claim.Workspace.Id, claim.View.RetirementId, token);
        if (!Owns(row, claim)) return false; var step = await Step(row, token); ValidateRequest(claim.Workspace, claim.Task, step);
        if (undispatched)
        {
            if (step.ProofJson is not null || step.SettledAtUtc is not null) return false;
            step.DispatchedAtUtc = null;
        }
        (await Job(row, token)).ErrorCode = code; Release(row, Now()); return true;
    }, token);

    private async Task<(WorkspaceArchiveRequest Request, WorkspaceArchiveReceipt Receipt)> Evidence(WorkspaceRetirementWorkflow row, CancellationToken token)
    {
        var evidence = await WorkspaceRetirementPreparationRepository.Evidence(db, ReadSnapshot(row), row.ArchiveId, token);
        if (evidence.Request.PlanSha256 != row.ArchivePlanSha256 || evidence.Request.ReceiptSha256 != row.CaptureReceiptSha256)
            throw new InvalidDataException("WORKSPACE_RETIREMENT_ARCHIVE_CHANGED");
        return evidence;
    }
    private void AddStep(WorkspaceRetirementWorkflow row)
    {
        var id = Guid.NewGuid(); var task = new WorkspaceRetirementTask(id, row.Id, row.ArchiveId, row.Phase, row.ExpectedEpoch,
            row.ArchivePlanSha256, row.CaptureReceiptSha256, row.Phase == "execute" ? Property(row.PlanJson!, "retirementPlanSha256") : "");
        db.WorkspaceRetirementSteps.Add(new() { Id = id, RetirementId = row.Id, Phase = row.Phase,
            Attempt = row.Phase == "execute" ? row.ExecutionAttempt : 0, RequestJson = Canonical(ReadSnapshot(row), task) });
        if (row.Phase == "execute") row.ExecuteStepId = id;
    }
    private static WorkspaceRetirementTask ReadTask(WorkspaceRetirementWorkflow row, WorkspaceRetirementStep step) => new(step.Id, row.Id, row.ArchiveId, step.Phase,
        row.ExpectedEpoch, row.ArchivePlanSha256, row.CaptureReceiptSha256, step.Phase == "execute" ? Property(row.PlanJson!, "retirementPlanSha256") : "");
    private static string Canonical(WorkspaceDefinition workspace, WorkspaceRetirementTask task)
    {
        var request = AgentGateway.BuildRetirementRequest(new(workspace.OwnerSubject, null, [], new string('a', 64), "retirement-validation", null), workspace, task);
        if (!DevelopmentRetirementBinding.IsValid(request)) throw new InvalidDataException("WORKSPACE_RETIREMENT_REQUEST_INVALID");
        return DevelopmentRetirementBinding.HostInput(request);
    }
    private static void ValidateRequest(WorkspaceDefinition workspace, WorkspaceRetirementTask task, WorkspaceRetirementStep step)
    {
        if (step.Id != task.TaskId || step.RetirementId != task.RetirementId || step.Phase != task.Phase || step.RequestJson != Canonical(workspace, task))
            throw new InvalidDataException("WORKSPACE_RETIREMENT_REQUEST_CHANGED");
    }
    private static WorkspaceDefinition ReadSnapshot(WorkspaceRetirementWorkflow row)
    {
        var value = JsonSerializer.Deserialize<WorkspaceDefinition>(row.SnapshotJson, Json) ?? throw new InvalidDataException("WORKSPACE_RETIREMENT_SNAPSHOT_INVALID");
        if (value.Id != row.WorkspaceId || value.State != WorkspaceStorageAllocation.Allocated || value.LifecycleState != WorkspaceLifecycle.Stopped || value.LifecycleVersion != row.ExpectedEpoch)
            throw new InvalidDataException("WORKSPACE_RETIREMENT_SNAPSHOT_INVALID");
        return value;
    }
    private static WorkspaceDefinition RequireFence(DevelopmentWorkspaceRecord current, WorkspaceRetirementWorkflow row)
    {
        var snapshot = ReadSnapshot(row);
        if (current.LifecycleState != WorkspaceRetirementPreparations.Retiring ||
            !JobRepository.JsonRequestsEqual(row.SnapshotJson, JsonSerializer.Serialize(DevelopmentWorkspaceRegistry.Map(current) with { LifecycleState = WorkspaceLifecycle.Stopped }, Json)))
            throw new InvalidOperationException("WORKSPACE_RETIREMENT_FENCE_CHANGED");
        return snapshot;
    }
    private async Task<DevelopmentWorkspaceRecord> LockWorkspace(Guid id, CancellationToken token) =>
        (await db.DevelopmentWorkspaces.FromSqlInterpolated($"SELECT * FROM development_workspaces WHERE id={id} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
    private async Task<WorkspaceRetirementWorkflow> LockIntent(Guid workspaceId, Guid id, CancellationToken token) =>
        (await db.WorkspaceRetirementWorkflows.FromSqlInterpolated($"SELECT * FROM development_workspace_retirement_workflows WHERE id={id} AND workspace_id={workspaceId} FOR UPDATE").ToArrayAsync(token))
            .SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_RETIREMENT_NOT_FOUND");
    private Task<WorkspaceRetirementStep> Step(WorkspaceRetirementWorkflow row, CancellationToken token) => db.WorkspaceRetirementSteps.SingleAsync(step =>
        step.RetirementId == row.Id && step.Phase == row.Phase && step.Attempt == (row.Phase == "execute" ? row.ExecutionAttempt : 0), token);
    private Task<OperationJob> Job(WorkspaceRetirementWorkflow row, CancellationToken token) => db.OperationJobs.SingleAsync(job => job.Id == row.Id && job.JobType == WorkspaceRetirements.JobType, token);
    private static string Property(string json, string name) { using var value = JsonDocument.Parse(json); return value.RootElement.GetProperty(name).GetString()!; }
    private bool Owns(WorkspaceRetirementWorkflow row, WorkspaceRetirementClaim claim) => row.FinishedAtUtc is null && row.Lease == claim.Lease &&
        row.LeaseUntilUtc > Now() && row.Phase == claim.Task.Phase && (row.Phase != "execute" || row.ExecuteStepId == claim.Task.TaskId);
    private void RequireNoLease(WorkspaceRetirementWorkflow row) { if (row.LeaseUntilUtc > Now()) throw new InvalidOperationException("WORKSPACE_RETIREMENT_WORKER_BUSY"); }
    private static void Release(WorkspaceRetirementWorkflow row, DateTimeOffset now) { row.Lease = null; row.LeaseUntilUtc = null; row.AvailableAtUtc = now.AddSeconds(3); }
    private static WorkspaceRetirementView View(WorkspaceRetirementWorkflow row, OperationJob job) => new(row.Id, row.WorkspaceId, row.ArchiveId, job.State, job.Phase, job.ErrorCode,
        row.Phase == "plan" && row.PlanJson != null && row.FinishedAtUtc == null && !row.CancelRequested, row.AuthorizationRequired, row.CancelRequested, row.AttentionRequired,
        row.PlanJson, row.PlanExpiresAtUtc, row.ConfirmedAtUtc, row.ExecutionJson, row.FinishedAtUtc)
        { ExecuteTaskId = row.ExecuteStepId, ExecutionAttempt = row.ExecutionAttempt };
    private DateTimeOffset Now() { var now = clock.GetUtcNow(); return new(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero); }
    private void Finish(WorkspaceRetirementWorkflow row, OperationJob job, string state, string phase, string? code, DateTimeOffset now)
    {
        row.FinishedAtUtc = job.CompletedAtUtc = now; job.State = state; job.Phase = phase; job.ErrorCode = code; job.Version++;
        job.ResultJson = JsonSerializer.Serialize(new { retirementId = row.Id, sourceRetired = state == "Succeeded", budgetReleased = state == "Succeeded", retainsHdd = true }, Json);
        Event(job, "WORKSPACE_RETIREMENT_SETTLED", now);
    }
    private void Event(OperationJob job, string code, DateTimeOffset now) => db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = job.Version + 1, State = job.State, Phase = job.Phase, MessageCode = code, OccurredAtUtc = now });
    private void Audit(TerminalActor actor, WorkspaceRetirementWorkflow row, string action, DateTimeOffset now) => db.AuditEvents.Add(new() { ActorSubject = actor.Subject,
        Action = "development.workspace.retirement." + action, TargetType = "workspace", TargetId = row.WorkspaceId.ToString("D"), JobId = row.Id, TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = now });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SELECT pg_advisory_xact_lock(41307420)", token);
        var result = await action(); await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return result;
    });
}
