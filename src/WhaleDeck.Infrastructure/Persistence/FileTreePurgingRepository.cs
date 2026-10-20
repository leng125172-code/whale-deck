using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreePurgingRepository(PlatformDbContext db, IFileTreePurgingProtector protector, TimeProvider clock,
    IFileTreeRecyclingProtector captureProtector, IFileTreeOverwriteRestoringProtector overwriteProtector) : IFileTreePurgingRepository
{
    public Task<FileTreePurgingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.FileTreePurgingOperations.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileTreePurgingOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreePurgingCommand request, CancellationToken cancellationToken) => Transaction(async () =>
    {
        FileTreePurgingValidation.Request(request); var id = FileTreePurgingValidation.IntentId(actor, key);
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            var previous = FileTreePurgingValidation.ReadCommand(prior);
            if (!FileTreePurgingValidation.HasValidBinding(prior, protector) || previous.CaptureId != request.CaptureId || previous.Scope != request.Scope) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == actor.Subject && item.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.EnsureBudget(db, actor.Subject, null, cancellationToken);
        var command = await CaptureCommand(request, cancellationToken);
        var now = Now(); var expires = now.AddMinutes(15);
        var row = new FileTreePurgingOperation { Id = id, CaptureId = command.CaptureId, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = protector.FingerprintRequest(new(id, actor.Subject, actor.LoginBinding, command, now, expires)),
            SourceHash = new FilePathScope(command.Source).Hash,
            CommandJson = JsonSerializer.Serialize(command), CreatedAtUtc = now, PlanningExpiresAtUtc = expires, NextAttemptAtUtc = now };
        db.FileTreePurgingOperations.Add(row);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.tree-purge-plan", ActorSubject = actor.Subject, IdempotencyKey = key, Phase = "AwaitingTreePlan",
            RequestJson = JsonSerializer.Serialize(new { sourceHash = row.SourceHash }) });
        Message(id); db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingTreePlan", MessageCode = "FILES_TREE_PLAN_ACCEPTED" });
        Audit(row, id, "files.tree-purge.plan-accepted", "Accepted", actor.TraceId, actor.SourceIp);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task<FileTreePurgingOperation> ConfirmAsync(TerminalActor actor, FileTreePurgingConfirmation claims, long version, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(claims.Id, cancellationToken); RequireOwner(row, actor);
        if (!FileTreePurgingValidation.HasValidBinding(row, protector) || claims.Actor != row.ActorSubject || claims.LoginBinding != row.LoginBinding ||
            claims.CaptureId != row.CaptureId || claims.Scope != FileTreePurgingValidation.ReadCommand(row).Scope || claims.Fingerprint != FileTreePurgingValidation.ReadPlan(row).Fingerprint || claims.ExpiresAtUtc != row.ReadyExpiresAtUtc) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        // Replaying the same confirmation finds the original execution even
        // after expiry or completion. It never creates a second root operation.
        if (row.ExecutionJobId is not null) return row;
        var planningJob = await Job(row.Id, cancellationToken); var now = Now();
        if (row.State != "Ready" || row.CancelRequestedAtUtc is not null || planningJob.CancelRequestedAtUtc is not null || row.ReadyExpiresAtUtc <= now)
            throw new InvalidOperationException("FILES_PLAN_EXPIRED_OR_CANCELED");
        if (row.Version != version) throw new InvalidOperationException("FILES_CONFIRMATION_CONFLICT");
        var id = FileTreePurgingValidation.ExecutionId(row.Id); var key = id.ToString("D");
        if (await db.OperationJobs.AnyAsync(item => item.Id == id || item.ActorSubject == row.ActorSubject && item.IdempotencyKey == key, cancellationToken))
            throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        var plan = FileTreePurgingValidation.ReadPlan(row);
        await RequireCapture(plan, cancellationToken, row.Id);
        await FilePathLocks.Acquire(db, id, row.ActorSubject, [plan.Command.Source],
            [FileTreePurgingValidation.CaptureToken(row.CaptureId)], cancellationToken, row.Id);
        // A lock wait must not extend a five-minute human confirmation window.
        now = Now(); if (row.ReadyExpiresAtUtc <= now) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        row.ExecutionJobId = id; row.ExecutionExpiresAtUtc = now.AddMinutes(15); row.State = "PurgingQueued"; row.NextAttemptAtUtc = now;
        row.ResultCode = "FILES_TREE_PURGE_ACCEPTED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        db.OperationJobs.Add(new() { Id = id, JobType = "files.tree-purge", ActorSubject = row.ActorSubject, IdempotencyKey = key, Phase = "AwaitingTreePurging",
            RequestJson = JsonSerializer.Serialize(new { intentId = row.Id, sourceHash = row.SourceHash, plan.EntryCount, plan.TotalBytes, plan.Fingerprint }) });
        Message(id); db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingTreePurging", MessageCode = "FILES_TREE_PURGE_ACCEPTED" });
        Audit(row, id, "files.tree-purge.confirmed", "Accepted", actor.TraceId, actor.SourceIp);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); RequireOwner(row, actor);
        if (row.CompletedAtUtc is not null) return true;
        if (!FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        var job = await Job(row.ExecutionJobId ?? row.Id, cancellationToken); var now = Now();
        if (row.CancelRequestedAtUtc is null)
        {
            row.CancelRequestedAtUtc = now; row.NextAttemptAtUtc = now; row.Version++;
            if (row.ExecutionJobId is null)
            {
                // Planning cannot publish data; this transaction fences a late
                // plan result AND a late confirmation without a root mutation.
                row.State = "Canceled"; row.CompletedAtUtc = now; row.ResultCode = "FILES_CANCELED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
                if (job.State != "Succeeded") { job.CancelRequestedAtUtc ??= now; await FinishJob(job, "Canceled", "Canceled", "FILES_CANCELED", cancellationToken); }
                await Processed(row.Id, now, cancellationToken);
            }
            else
            {
                job.CancelRequestedAtUtc ??= now; job.Version++;
                db.OutboxMessages.Add(new() { MessageType = "FileTreePurgingCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = job.Id }) });
            }
            Audit(row, job.Id, "files.tree-purge.cancel-requested", "Accepted", actor.TraceId, actor.SourceIp);
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }, cancellationToken);
    public Task<FileTreePurgingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.FileTreePurgingOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken);
            if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM file_tree_purging_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                    AND NOT EXISTS (SELECT 1 FROM file_recovery_operations r WHERE r.kind='tree-purge' AND r.original_intent_id=file_tree_purging_operations.id AND r.completed_at_utc IS NULL)
                    AND (state<>'Ready' OR ready_expires_at_utc<={now} OR cancel_requested_at_utc IS NOT NULL)
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE file_tree_purging_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.FileTreePurgingOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public async Task<bool> CancellationRequestedAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken)
    {
        if (await db.FileTreePurgingOperations.AnyAsync(item => item.Id == claimed.Id && item.CancelRequestedAtUtc != null, cancellationToken)) return true;
        var id = claimed.ExecutionJobId ?? claimed.Id;
        return await db.OperationJobs.AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    }
    public Task<bool> MarkPlanningAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null || row.State is not ("Queued" or "Planning")) return false;
        var job = await Job(row.Id, cancellationToken); var now = Now();
        if (row.LeaseExpiresAtUtc <= now || row.PlanningExpiresAtUtc <= now || row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return false;
        row.State = "Planning"; row.Version++;
        if (job.State != "Running")
        { job.State = "Running"; job.Phase = "GeneratingTreePlan"; job.StartedAtUtc ??= now; job.Version++; await Event(job, "FILES_TREE_PLANNING", cancellationToken); }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishPlanningAsync(FileTreePurgingOperation claimed, FileTreePurgingPlan? plan, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null) return false;
        var job = await Job(row.Id, cancellationToken); var now = Now(); if (row.LeaseExpiresAtUtc <= now) return false;
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) { plan = null; code = "FILES_CANCELED"; }
        else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= now) { plan = null; code = "FILES_PLAN_EXPIRED"; }
        if (plan is not null)
        {
            FileTreePurgingValidation.Plan(plan);
            await RequireCapture(plan, cancellationToken, row.Id);
            if (!FileTreePurgingValidation.HasValidBinding(row, protector) || plan.Command != FileTreePurgingValidation.ReadCommand(row)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");

            row.PlanJson = JsonSerializer.Serialize(plan); row.ReadyExpiresAtUtc = now.AddMinutes(5); row.PlanHash = protector.FingerprintPlan(FileTreePurgingValidation.PlanBinding(row));
            row.State = "Ready"; row.NextAttemptAtUtc = row.ReadyExpiresAtUtc.Value; row.ResultCode = "FILES_TREE_READY_FOR_CONFIRMATION";
            await FinishJob(job, "Succeeded", "TreePlanReady", row.ResultCode, cancellationToken);
        }
        else
        {
            row.State = code == "FILES_CANCELED" ? "Canceled" : code == "FILES_PLAN_EXPIRED" ? "Expired" : "Failed";
            row.ResultCode = code; row.CompletedAtUtc = now;
            // An already successful planning job remains historical success;
            // expiry/cancel of its unconfirmed result does not rewrite history.
            if (job.State != "Succeeded") await FinishJob(job, row.State == "Canceled" ? "Canceled" : "Failed", row.State, code, cancellationToken);
        }
        row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        Audit(row, row.Id, "files.tree-purge.plan-result", row.State, row.Id.ToString("D"), null);
        await Processed(row.Id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileUploadStartResult> StartedAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row?.ExecutionJobId is not { } id) return FileUploadStartResult.LeaseLost;
        var job = await Job(id, cancellationToken); var now = Now();
        if (row.LeaseExpiresAtUtc <= now || row.RootStartedAtUtc is not null) return FileUploadStartResult.LeaseLost;
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return FileUploadStartResult.Canceled;
        if (row.ExecutionExpiresAtUtc <= now) return FileUploadStartResult.Expired;
        await RequireCapture(FileTreePurgingValidation.ReadPlan(row), cancellationToken, row.Id);
        row.RootStartedAtUtc = now; row.State = "Purging"; row.Version++;
        job.State = "Running"; job.Phase = "ApplyingTreePurging"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_TREE_PURGING", cancellationToken); await db.SaveChangesAsync(cancellationToken); return FileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> RenewAsync(FileTreePurgingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        row.LeaseExpiresAtUtc = Now().AddSeconds(90); row.Version++; await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        if (row.RootStartedAtUtc is not null && row.ExecutionJobId is { } id && row.State != "Unknown")
        {
            var job = await Job(id, cancellationToken);
            if (row.LeaseExpiresAtUtc <= Now()) return false;
            // A lost response is not evidence that purging is still running.
            // Keep the intent and shared path locks until the root receipt is
            // reconciled; subsequent attempts may inspect/discard, never apply.
            row.State = "Unknown"; row.ResultCode = "FILES_RECEIPT_UNAVAILABLE";
            job.State = "Running"; job.Phase = "AwaitingTreeReceipt"; job.ErrorCode = row.ResultCode; job.Version++;
            await Event(job, row.ResultCode, cancellationToken);
            Audit(row, id, "files.tree-purge.result", "Unknown", row.Id.ToString("D"), null);
        }
        row.NextAttemptAtUtc = Now().AddSeconds(6); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
}
