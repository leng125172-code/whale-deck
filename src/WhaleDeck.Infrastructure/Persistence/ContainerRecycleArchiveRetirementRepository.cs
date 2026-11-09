using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;
using StartResult = WhaleDeck.Application.Models.ContainerFileUploadStartResult;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class ContainerRecycleArchiveRetirementRepository(PlatformDbContext db, IContainerRecycleArchiveRetirementProtector protector,
    IContainerRecycleArchiveProtector archiveProtector, IContainerRecyclePurgeProtector purgeProtector, IContainerRecycleProtector recycleProtector, TimeProvider clock)
    : IContainerRecycleArchiveRetirementRepository
{
    private const string PlanJobType = "containers.files.recycle-archive-retirement-plan";
    private const string ExecutionJobType = "containers.files.recycle-archive-retirement";
    private const string Requested = "ContainerRecycleArchiveRetirementRequested";
    private const string CancellationRequested = "ContainerRecycleArchiveRetirementCancellationRequested";
    private const string Prefix = Retirement.Prefix;

    public Task<ContainerRecycleArchiveRetirementIntent?> FindAsync(Guid id, CancellationToken token) =>
        db.ContainerRecycleArchiveRetirementIntents.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);

    public Task<ContainerRecycleArchiveRetirementIntent> EnqueueAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) =>
        Transaction(async () =>
        {
            Validate(row);
            var prior = await FindAsync(row.Id, token);
            if (prior is not null)
            {
                Validate(prior);
                if (prior.ActorSubject != row.ActorSubject || prior.LoginBinding != row.LoginBinding || prior.IdempotencyKey != row.IdempotencyKey ||
                    prior.ArchiveIntentId != row.ArchiveIntentId || prior.RetainDays != row.RetainDays || prior.SourceProofHash != row.SourceProofHash)
                    throw Failure("IDEMPOTENCY_CONFLICT");
                return prior;
            }
            var now = Now();
            if (row.State != "Queued" || row.PlanJson is not null || row.ExecutionJobId is not null || row.Version != 0 || row.ResultCode is not null ||
                row.RootStartedAtUtc is not null || row.ApplyStartedAtUtc is not null || row.CompletedAtUtc is not null || row.PublishBarrier ||
                row.CancelRequestedAtUtc is not null || row.LeaseToken is not null || row.LeaseExpiresAtUtc is not null || row.AcknowledgedBy is not null ||
                row.AcknowledgementJson is not null || row.NextAttemptAtUtc != row.CreatedAtUtc)
                throw Retirement.Invalid();
            if (row.CreatedAtUtc > now || row.PlanningExpiresAtUtc <= now) throw Failure("PLAN_EXPIRED");
            await FilePathLocks.EnsureBudget(db, row.ActorSubject, null, token);
            await LockSource(row, token);
            if (await db.ContainerRecycleArchiveRetirementIntents.AnyAsync(r => r.ArchiveIntentId == row.ArchiveIntentId &&
                (r.CompletedAtUtc == null || r.RootState == "Unknown" || r.RootState == "Completed"), token))
                throw Failure("SOURCE_BUSY");
            db.ContainerRecycleArchiveRetirementIntents.Add(row);
            AddJob(row, row.Id, PlanJobType, "PLAN_ACCEPTED", now);
            await db.SaveChangesAsync(token);
            return row;
        }, token);

    public Task<ContainerRecycleArchiveRetirementIntent> ConfirmAsync(TerminalActor actor, string confirmation, long version, CancellationToken token) =>
        Transaction(async () =>
        {
            var claims = protector.VerifyRetirementConfirmation(confirmation);
            var row = await Row(claims.Id, token);
            Own(row, actor);
            Validate(row);
            if (claims.Actor != row.ActorSubject || claims.LoginBinding != row.LoginBinding || !Retirement.EqualMac(claims.PlanHash, row.PlanHash ?? "") ||
                claims.ExpiresAtUtc != row.ReadyExpiresAtUtc) throw Failure("PLAN_CHANGED");
            if (row.ExecutionJobId is not null) return row;
            var planningJob = await Job(row, token);
            var now = Now();
            if (row.Version != version || row.State != "Ready" || planningJob.State != "Succeeded" || Canceled(row, planningJob) || row.ReadyExpiresAtUtc <= now)
                throw Failure("CONFIRMATION_CONFLICT");
            await LockSource(row, token);
            now = Now();
            if (row.ReadyExpiresAtUtc <= now) throw Failure("PLAN_EXPIRED");
            // An early diagnostic plan has no execution authority. Confirm a
            // freshly due plan instead of holding a global barrier until expiry.
            if (!Due(row, now)) throw Failure("NOT_DUE");
            var id = Retirement.ExecutionId(row.Id);
            row.ExecutionJobId = id;
            row.ConfirmedAtUtc = now;
            row.ExecutionExpiresAtUtc = now.AddMinutes(15);
            row.ExecutionHash = protector.FingerprintRetirementExecution(row);
            row.State = "Pending";
            row.NextAttemptAtUtc = now;
            ReleaseLease(row);
            row.ResultCode = Prefix + "CONFIRMED";
            row.Version++;
            AddJob(row, id, ExecutionJobType, "CONFIRMED", now);
            await db.SaveChangesAsync(token);
            return row;
        }, token);

    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) => Transaction(async () =>
    {
        var row = await Row(id, token);
        Own(row, actor);
        Validate(row);
        if (row.CompletedAtUtc is not null || row.State == "Unknown" || row.CancelRequestedAtUtc is not null) return false;
        var job = await Job(row, token);
        var now = Now();
        row.CancelRequestedAtUtc = now;
        row.NextAttemptAtUtc = now;
        row.Version++;
        if (row.ExecutionJobId is null)
            await ClosePlanning(row, job, "Canceled", Prefix + "CANCELED", now, token);
        else if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = now;
            job.Version++;
            Message(job.Id, CancellationRequested);
        }
        Audit(row, job.Id, actor.Subject, actor.TraceId, "cancel-requested", "Accepted");
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task<ContainerRecycleArchiveRetirementIntent?> ClaimAsync(CancellationToken token)
    {
        var lease = Guid.NewGuid();
        return Transaction(async () =>
        {
            var existing = await db.ContainerRecycleArchiveRetirementIntents.AsNoTracking().SingleOrDefaultAsync(r => r.LeaseToken == lease, token);
            if (existing is not null) return existing;
            var now = Now();
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT r.id FROM container_recycle_archive_retirement_intents r
                  WHERE r.completed_at_utc IS NULL AND r.state <> 'Unknown'
                    AND (r.lease_expires_at_utc IS NULL OR r.lease_expires_at_utc <= {now})
                    AND ((r.next_attempt_at_utc <= {now} AND (r.state <> 'Ready' OR r.ready_expires_at_utc <= {now}))
                      OR r.cancel_requested_at_utc IS NOT NULL
                      OR EXISTS (SELECT 1 FROM operation_jobs j WHERE j.id=COALESCE(r.execution_job_id,r.id) AND j.cancel_requested_at_utc IS NOT NULL))
                  ORDER BY r.next_attempt_at_utc,r.id LIMIT 1 FOR UPDATE SKIP LOCKED
                )
                UPDATE container_recycle_archive_retirement_intents r SET lease_token={lease},lease_expires_at_utc={expiry},version=r.version+1
                FROM candidate WHERE r.id=candidate.id RETURNING r.id AS "Value"
                """).ToArrayAsync(token);
            return ids.Length == 0 ? null : await FindAsync(ids[0], token);
        }, token);
    }

    public async Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token)
    {
        var row = await FindAsync(id, token) ?? throw Missing();
        return row.CancelRequestedAtUtc is not null ||
            await db.OperationJobs.AnyAsync(j => j.Id == (row.ExecutionJobId ?? row.Id) && j.CancelRequestedAtUtc != null, token);
    }

    public Task<bool> MarkPlanningAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null || row.State is not ("Queued" or "Planning")) return false;
        var job = await Job(row, token);
        var now = Now();
        if (Canceled(row, job) || row.PlanningExpiresAtUtc <= now || row.LeaseExpiresAtUtc <= now) return false;
        if (row.State == "Planning") return true;
        row.State = "Planning";
        row.Version++;
        await AdvanceJob(job, "Running", "Planning", Prefix + "PLANNING", now, token);
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task FinishPlanningAsync(ContainerRecycleArchiveRetirementIntent claimed, ContainerRecycleArchiveRetirementPlanDto? plan, string code, CancellationToken token)
    {
        Code(code);
        // Freeze caller-owned arrays before crossing any asynchronous boundary.
        var frozen = plan is null ? null : Retirement.Read<ContainerRecycleArchiveRetirementPlanDto>(Retirement.Write(plan));
        return Transaction(async () =>
        {
            var row = await Lease(claimed, token);
            if (row is null || row.ExecutionJobId is not null) return false;
            var job = await Job(row, token);
            var now = Now();
            if (row.LeaseExpiresAtUtc <= now) return false;
            if (Canceled(row, job)) await ClosePlanning(row, job, "Canceled", Prefix + "CANCELED", now, token);
            else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= now)
                await ClosePlanning(row, job, "Expired", Prefix + "PLAN_EXPIRED", now, token);
            else if (frozen is null)
                await ClosePlanning(row, job, "Failed", code, now, token);
            else
            {
                if (row.State != "Planning") throw Retirement.Invalid();
                var source = await LockSource(row, token);
                ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source, frozen);
                now = Now();
                if (row.PlanningExpiresAtUtc <= now || row.LeaseExpiresAtUtc <= now) return false;
                row.PlanJson = Retirement.Write(frozen);
                row.ReadyExpiresAtUtc = now.AddMinutes(5);
                row.PlanHash = protector.FingerprintRetirementPlan(row);
                row.State = "Ready";
                row.ResultCode = Prefix + "READY";
                row.NextAttemptAtUtc = row.ReadyExpiresAtUtc.Value;
                ReleaseLease(row);
                await AdvanceJob(job, "Succeeded", "Ready", row.ResultCode, now, token);
                await Processed(job.Id, now, token);
            }
            row.Version++;
            Audit(row, job.Id, row.ActorSubject, row.TraceId, "plan-result", row.State);
            await db.SaveChangesAsync(token);
            return true;
        }, token);
    }

    public Task<StartResult> StartAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null || row.State != "Pending" || row.RootStartedAtUtc is not null) return StartResult.LeaseLost;
        var job = await Job(row, token);
        if (Canceled(row, job)) return StartResult.Canceled;
        if (row.ExecutionExpiresAtUtc <= Now()) return StartResult.Expired;
        if (await WritersBusy(token)) return StartResult.Busy;
        await LockSource(row, token);
        var now = Now();
        if (row.ExecutionExpiresAtUtc <= now || !Due(row, now)) return StartResult.Expired;
        if (row.LeaseExpiresAtUtc <= now) return StartResult.LeaseLost;
        row.State = "Reserving";
        row.RootStartedAtUtc = now;
        row.PublishBarrier = true;
        row.Version++;
        await AdvanceJob(job, "Running", "Reserving", Prefix + "RESERVING", now, token);
        Audit(row, job.Id, row.ActorSubject, row.TraceId, "reserve-started", row.State);
        await db.SaveChangesAsync(token);
        return StartResult.Started;
    }, token);

    public Task<bool> ReserveAsync(ContainerRecycleArchiveRetirementIntent claimed, ContainerRecycleArchiveRetirementReceiptDto receipt, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null || row.State != "Reserving" || !row.PublishBarrier) return false;
        if (receipt.State != "Reserved") throw Retirement.Invalid();
        var mac = protector.FingerprintRetirementReceipt(Retirement.Operation(row), receipt);
        if (row.ReservationHash is not null)
        {
            if (!Retirement.EqualMac(row.ReservationHash, mac)) throw Retirement.Invalid();
            return true;
        }
        await LockSource(row, token);
        var job = await Job(row, token);
        var now = Now();
        if (row.LeaseExpiresAtUtc <= now) return false;
        row.ReservationJson = Retirement.Write(receipt);
        row.ReservationHash = mac;
        row.Version++;
        await AdvanceJob(job, "Running", "Reserved", Prefix + "RESERVED", now, token);
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task<StartResult> EnterApplyAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null || row.State != "Reserving" || row.ApplyStartedAtUtc is not null || row.ReservationJson is null || !row.PublishBarrier)
            return StartResult.LeaseLost;
        var job = await Job(row, token);
        if (Canceled(row, job)) return StartResult.Canceled;
        if (row.ExecutionExpiresAtUtc <= Now()) return StartResult.Expired;
        await LockSource(row, token);
        var now = Now();
        if (row.ExecutionExpiresAtUtc <= now || !Due(row, now)) return StartResult.Expired;
        if (row.LeaseExpiresAtUtc <= now) return StartResult.LeaseLost;
        row.State = "Applying";
        row.ApplyStartedAtUtc = now;
        row.Version++;
        await AdvanceJob(job, "Running", "Applying", Prefix + "APPLYING", now, token);
        Audit(row, job.Id, row.ActorSubject, row.TraceId, "apply-started", row.State);
        await db.SaveChangesAsync(token);
        return StartResult.Started;
    }, token);
}
