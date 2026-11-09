using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>All transitions share gate 41307341 with host paths and other container publishers.</summary>
public sealed partial class ContainerRecycleRestoreRepository(PlatformDbContext db, IContainerRecycleRestoreProtector protector, TimeProvider clock, IContainerRecycleProtector sourceProtector) : IContainerRecycleRestoreRepository
{
    public Task<ContainerRecycleRestoreIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.ContainerRecycleRestoreIntents.AsNoTracking()
        .SingleOrDefaultAsync(r => r.Id == id && r.ActorSubject == actor.Subject && r.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<ContainerRecycleRestoreIntent?> FindAsync(Guid id, CancellationToken cancellationToken) => db.ContainerRecycleRestoreIntents.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    public async Task<IReadOnlyList<ContainerRecycleRestoreIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 50) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_PAGE_INVALID");
        return await db.ContainerRecycleRestoreIntents.AsNoTracking().Where(r => (r.State == "Unknown") && r.CompletedAtUtc == null).OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).Take(take).ToArrayAsync(cancellationToken);
    }
    public Task<ContainerRecycleRestoreIntent> EnqueueAsync(ContainerRecycleRestoreIntent row, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Validate(row); var prior = await FindAsync(row.Id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.ActorSubject != row.ActorSubject || prior.LoginBinding != row.LoginBinding || prior.IdempotencyKey != row.IdempotencyKey || prior.CommandJson != row.CommandJson || prior.SourceIntentId != row.SourceIntentId || prior.SourceProofHash != row.SourceProofHash)
                throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        var now = Now();
        if (row.PlanningExpiresAtUtc <= now || row.CreatedAtUtc > now) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED");
        if (row.State != "Queued" || row.PlanJson is not null || row.ExecutionJobId is not null || row.CompletedAtUtc is not null || row.RootStartedAtUtc is not null || row.PublishBarrier ||
            row.LeaseToken is not null || row.LeaseExpiresAtUtc is not null || row.CancelRequestedAtUtc is not null || row.Version != 0 || row.ReceiptJson is not null || row.ReceiptHash is not null || row.HistoryVerified || row.HistoryDigest is not null || row.RootState is not null ||
            row.AcknowledgedBy is not null || row.AcknowledgementJson is not null) throw ContainerRecycleRestoreWorkflowValidation.Invalid();
        var key = row.IdempotencyKey.ToString("D");
        if (await db.OperationJobs.AnyAsync(j => j.Id == row.Id || j.ActorSubject == row.ActorSubject && j.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.EnsureBudget(db, row.ActorSubject, null, cancellationToken);
        await EnsureSource(row, cancellationToken);
        await EnsurePaths(row, cancellationToken);
        db.ContainerRecycleRestoreIntents.Add(row);
        var job = new OperationJob { Id = row.Id, ActorSubject = row.ActorSubject, IdempotencyKey = key, JobType = "containers.files.recycle-restore-plan", Phase = "AwaitingContainerRecycleRestorePlan", RequestJson = Metadata(row) };
        db.OperationJobs.Add(job); Message(job.Id, "ContainerRecycleRestoreRequested");
        await Event(job, "CONTAINER_RECYCLE_RESTORE_PLAN_ACCEPTED", cancellationToken); Audit(row, job.Id, row.ActorSubject, row.TraceId, "plan-accepted", "Accepted");
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task<ContainerRecycleRestoreIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleRestoreConfirmation claims, long version, CancellationToken cancellationToken) => Transaction(async () =>
    {
        ContainerRecycleRestoreWorkflowValidation.Confirmation(claims); if (version < 0) throw ContainerRecycleRestoreWorkflowValidation.Invalid();
        var row = await Row(claims.Id, cancellationToken); Owner(row, actor); Validate(row);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding || claims.PlanHash != row.PlanHash || claims.ExpiresAtUtc != row.ReadyExpiresAtUtc)
            throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_CHANGED");
        // An acknowledged old confirmation still identifies only the original execution.
        if (row.ExecutionJobId is not null) return row;
        var planning = await Job(row, false, cancellationToken); var now = Now();
        if (row.State != "Ready" || row.CompletedAtUtc is not null || row.CancelRequestedAtUtc is not null || planning.CancelRequestedAtUtc is not null || row.ReadyExpiresAtUtc <= now)
            throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED_OR_CANCELED");
        if (row.Version != version) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_CONFIRMATION_CONFLICT");
        var id = ContainerRecycleRestoreWorkflowValidation.ExecutionId(row.Id); var key = id.ToString("D");
        if (await db.OperationJobs.AnyAsync(j => j.Id == id || j.ActorSubject == row.ActorSubject && j.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_IDEMPOTENCY_CONFLICT");
        await EnsureSource(row, cancellationToken);
        await EnsurePaths(row, cancellationToken); now = Now();
        if (row.ReadyExpiresAtUtc <= now) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED");
        row.ExecutionJobId = id; row.ConfirmedAtUtc = now; row.ExecutionExpiresAtUtc = now.AddMinutes(15);
        row.ExecutionHash = protector.FingerprintRecycleRestoreExecution(ContainerRecycleRestoreWorkflowValidation.ExecutionBinding(row));
        row.State = "Pending"; row.NextAttemptAtUtc = now; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.ResultCode = "CONTAINER_RECYCLE_RESTORE_ACCEPTED"; row.Version++;
        var job = new OperationJob { Id = id, ActorSubject = row.ActorSubject, IdempotencyKey = key, JobType = "containers.files.recycle-restore", Phase = "AwaitingContainerRecycleRestore", RequestJson = Metadata(row) };
        db.OperationJobs.Add(job); Message(id, "ContainerRecycleRestoreRequested"); await Event(job, row.ResultCode, cancellationToken);
        Audit(row, id, actor.Subject, actor.TraceId, "confirmed", "Accepted"); await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); Owner(row, actor); Validate(row); if (row.CompletedAtUtc is not null) return true;
        var now = Now(); var job = await Job(row, row.ExecutionJobId is not null, cancellationToken);
        if (row.CancelRequestedAtUtc is null)
        {
            row.CancelRequestedAtUtc = now; row.NextAttemptAtUtc = now; row.Version++;
            if (row.ExecutionJobId is null)
            {
                row.State = "Canceled"; row.ResultCode = "CONTAINER_RECYCLE_RESTORE_CANCELED"; row.CompletedAtUtc = now; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
                if (job.State != "Succeeded") { job.CancelRequestedAtUtc ??= now; await FinishJob(job, row, "Canceled", "Canceled", row.ResultCode, cancellationToken); }
                await Processed(job.Id, now, cancellationToken);
            }
            else { job.CancelRequestedAtUtc ??= now; job.Version++; Message(job.Id, "ContainerRecycleRestoreCancellationRequested"); }
            Audit(row, job.Id, actor.Subject, actor.TraceId, "cancel-requested", "Accepted"); await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }, cancellationToken);
    public Task<ContainerRecycleRestoreIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lease = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.ContainerRecycleRestoreIntents.AsNoTracking().SingleOrDefaultAsync(r => r.LeaseToken == lease, cancellationToken); if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM container_recycle_restore_intents WHERE completed_at_utc IS NULL AND state NOT IN ('Unknown')
                    AND next_attempt_at_utc<={now} AND (state<>'Ready' OR ready_expires_at_utc<={now} OR cancel_requested_at_utc IS NOT NULL
                      OR EXISTS (SELECT 1 FROM operation_jobs j WHERE j.id=container_recycle_restore_intents.id AND j.cancel_requested_at_utc IS NOT NULL))
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE container_recycle_restore_intents f SET lease_token={lease},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await FindAsync(ids[0], cancellationToken);
        }, cancellationToken);
    }
    public async Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_RESTORE_NOT_FOUND");
        return row.CancelRequestedAtUtc is not null || await db.OperationJobs.AnyAsync(j => (j.Id == row.Id || j.Id == row.ExecutionJobId) && j.CancelRequestedAtUtc != null, cancellationToken);
    }
    public Task<bool> MarkPlanningAsync(ContainerRecycleRestoreIntent claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null || row.State is not ("Queued" or "Planning")) return false;
        Validate(row); var job = await Job(row, false, cancellationToken); var now = Now();
        if (row.PlanningExpiresAtUtc <= now || row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return false;
        row.State = "Planning"; row.Version++;
        if (job.State != "Running") { job.State = "Running"; job.Phase = "GeneratingContainerRecycleRestorePlan"; job.StartedAtUtc ??= now; job.Version++; await Event(job, "CONTAINER_RECYCLE_RESTORE_PLANNING", cancellationToken); }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task FinishPlanningAsync(ContainerRecycleRestoreIntent claimed, ContainerRecycleRestorePlanDto? plan, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Code(code); var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null) return false;
        var job = await Job(row, false, cancellationToken); var now = Now();
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) { plan = null; code = "CONTAINER_RECYCLE_RESTORE_CANCELED"; }
        else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= now) { plan = null; code = "CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED"; }
        if (plan is not null)
        {
            await EnsureSource(row, cancellationToken);
            Validate(row); plan = ContainerRecycleRestoreWorkflowValidation.SnapshotPlan(plan);
            if (!ContainerRecycleRestoreWorkflowValidation.SameCommand(plan.Command, ContainerRecycleRestoreWorkflowValidation.ReadCommand(row))) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_CHANGED");
            row.PlanJson = ContainerRecycleRestoreWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = now.AddMinutes(5);
            row.PlanHash = protector.FingerprintRecycleRestorePlan(ContainerRecycleRestoreWorkflowValidation.PlanBinding(row));
            row.State = "Ready"; row.NextAttemptAtUtc = row.ReadyExpiresAtUtc.Value; row.ResultCode = "CONTAINER_RECYCLE_RESTORE_READY";
            await FinishJob(job, row, "Succeeded", "ContainerRecycleRestorePlanReady", row.ResultCode, cancellationToken);
        }
        else
        {
            row.State = code == "CONTAINER_RECYCLE_RESTORE_CANCELED" ? "Canceled" : code == "CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED" ? "Expired" : "Failed";
            row.ResultCode = code; row.CompletedAtUtc = now;
            if (job.State != "Succeeded") await FinishJob(job, row, row.State == "Canceled" ? "Canceled" : "Failed", row.State, code, cancellationToken);
        }
        row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        Audit(row, row.Id, row.ActorSubject, row.TraceId, "plan-result", row.State); await Processed(row.Id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is null || row.State != "Pending" || row.RootStartedAtUtc is not null) return ContainerFileUploadStartResult.LeaseLost;
        Validate(row); var job = await Job(row, true, cancellationToken); now = Now();
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return ContainerFileUploadStartResult.Canceled;
        if (row.ExecutionExpiresAtUtc <= now) return ContainerFileUploadStartResult.Expired;
        if (await db.ContainerRecyclePurgeIntents.AnyAsync(r=>r.PublishBarrier,cancellationToken) ||
            await db.ContainerRecycleArchiveRetirementIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerRecycleArchiveIntents.AnyAsync(r=>r.PublishBarrier,cancellationToken) ||
            await db.ContainerRecycleIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerRecursiveAclRestoreIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerRecursiveOwnershipIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerRecursivePermissionIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.FilePathLocks.AnyAsync(cancellationToken) || await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier, cancellationToken) || await db.ContainerRecycleRestoreIntents.AnyAsync(r => r.PublishBarrier && r.Id != row.Id, cancellationToken) ||
            await db.ContainerRecursiveAclIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerAclIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerOwnershipIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerPermissionIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerCopyIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerMoveIntents.AnyAsync(r => r.PublishBarrier, cancellationToken)) return ContainerFileUploadStartResult.Busy;
        await EnsureSource(row, cancellationToken);
        row.State = "Applying"; row.RootStartedAtUtc = now; row.PublishBarrier = true; row.Version++;
        job.State = "Running"; job.Phase = "ApplyingContainerRecycleRestore"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "CONTAINER_RECYCLE_RESTORE_APPLYING", cancellationToken); await db.SaveChangesAsync(cancellationToken);
        claimed.State = row.State; claimed.RootStartedAtUtc = now; claimed.PublishBarrier = true; return ContainerFileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> RenewAsync(ContainerRecycleRestoreIntent claimed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.State == "Unknown") return false;
        Validate(row); row.LeaseExpiresAtUtc = Now().AddSeconds(90); row.Version++; await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(ContainerRecycleRestoreIntent claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        // Keep the permanent started marker and publication fence. The next
        // automatic pass can observe/discard, never Apply another time.
        row.NextAttemptAtUtc = Now().AddSeconds(6); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
}
