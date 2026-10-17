using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreeMoveRepository(PlatformDbContext db, IFileTreeMoveProtector protector, TimeProvider clock) : IFileTreeMoveRepository
{
    public Task<FileTreeMoveOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.FileTreeMoveOperations.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileTreeMoveOperation> QueueAsync(TerminalActor actor, string key, FileTreeCopyCommand command, CancellationToken cancellationToken) => Transaction(async () =>
    {
        FileTreeMoveValidation.Command(command); var id = FileTreeMoveValidation.IntentId(actor, key);
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            if (!FileTreeMoveValidation.HasValidBinding(prior, protector) || FileTreeMoveValidation.ReadCommand(prior) != command) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == actor.Subject && item.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.EnsureBudget(db, actor.Subject, null, cancellationToken);
        var now = Now(); var expires = now.AddMinutes(15);
        var row = new FileTreeMoveOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding),
            RequestHash = protector.FingerprintRequest(new(id, actor.Subject, actor.LoginBinding, command, expires)),
            SourceHash = new FilePathScope(command.Source).Hash, TargetHash = new FilePathScope(command.Target).Hash,
            CommandJson = JsonSerializer.Serialize(command), CreatedAtUtc = now, PlanningExpiresAtUtc = expires, NextAttemptAtUtc = now };
        db.FileTreeMoveOperations.Add(row);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.tree-move-plan", ActorSubject = actor.Subject, IdempotencyKey = key, Phase = "AwaitingTreePlan",
            RequestJson = JsonSerializer.Serialize(new { sourceHash = row.SourceHash, targetHash = row.TargetHash }) });
        Message(id); db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingTreePlan", MessageCode = "FILES_TREE_PLAN_ACCEPTED" });
        Audit(row, id, "files.tree-move.plan-accepted", "Accepted", actor.TraceId, actor.SourceIp);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task<FileTreeMoveOperation> ConfirmAsync(TerminalActor actor, FileTreeMoveConfirmation claims, long version, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(claims.Id, cancellationToken); RequireOwner(row, actor);
        if (!FileTreeMoveValidation.HasValidBinding(row, protector) || claims.Actor != row.ActorSubject || claims.LoginBinding != row.LoginBinding ||
            claims.Fingerprint != FileTreeMoveValidation.ReadPlan(row).Fingerprint || claims.ExpiresAtUtc != row.ReadyExpiresAtUtc) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        // Replaying the same confirmation finds the original execution even
        // after expiry or completion. It never creates a second root operation.
        if (row.ExecutionJobId is not null) return row;
        var planningJob = await Job(row.Id, cancellationToken); var now = Now();
        if (row.State != "Ready" || row.CancelRequestedAtUtc is not null || planningJob.CancelRequestedAtUtc is not null || row.ReadyExpiresAtUtc <= now)
            throw new InvalidOperationException("FILES_PLAN_EXPIRED_OR_CANCELED");
        if (row.Version != version) throw new InvalidOperationException("FILES_CONFIRMATION_CONFLICT");
        var id = FileTreeMoveValidation.ExecutionId(row.Id); var key = id.ToString("D");
        if (await db.OperationJobs.AnyAsync(item => item.Id == id || item.ActorSubject == row.ActorSubject && item.IdempotencyKey == key, cancellationToken))
            throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        var plan = FileTreeMoveValidation.ReadPlan(row);
        await FilePathLocks.Acquire(db, id, row.ActorSubject, [plan.Command.Source, plan.Command.Target], [], cancellationToken, row.Id);
        // A lock wait must not extend a five-minute human confirmation window.
        now = Now(); if (row.ReadyExpiresAtUtc <= now) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        row.ExecutionJobId = id; row.ExecutionExpiresAtUtc = now.AddMinutes(15); row.State = "MoveQueued"; row.NextAttemptAtUtc = now;
        row.ResultCode = "FILES_TREE_MOVE_ACCEPTED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        db.OperationJobs.Add(new() { Id = id, JobType = "files.tree-move", ActorSubject = row.ActorSubject, IdempotencyKey = key, Phase = "AwaitingTreeMove",
            RequestJson = JsonSerializer.Serialize(new { intentId = row.Id, sourceHash = row.SourceHash, targetHash = row.TargetHash, plan.EntryCount, plan.TotalBytes, plan.Fingerprint }) });
        Message(id); db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingTreeMove", MessageCode = "FILES_TREE_MOVE_ACCEPTED" });
        Audit(row, id, "files.tree-move.confirmed", "Accepted", actor.TraceId, actor.SourceIp);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); RequireOwner(row, actor);
        if (row.CompletedAtUtc is not null) return true;
        if (!FileTreeMoveValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
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
                db.OutboxMessages.Add(new() { MessageType = "FileTreeMoveCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = job.Id }) });
            }
            Audit(row, job.Id, "files.tree-move.cancel-requested", "Accepted", actor.TraceId, actor.SourceIp);
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }, cancellationToken);
    public Task<FileTreeMoveOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.FileTreeMoveOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken);
            if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM file_tree_move_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                    AND NOT EXISTS (SELECT 1 FROM file_recovery_operations r WHERE r.kind='tree-move' AND r.original_intent_id=file_tree_move_operations.id AND r.completed_at_utc IS NULL)
                    AND (state<>'Ready' OR ready_expires_at_utc<={now} OR cancel_requested_at_utc IS NOT NULL)
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE file_tree_move_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.FileTreeMoveOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public async Task<bool> CancellationRequestedAsync(FileTreeMoveOperation row, CancellationToken cancellationToken)
    {
        if (await db.FileTreeMoveOperations.AnyAsync(item => item.Id == row.Id && item.CancelRequestedAtUtc != null, cancellationToken)) return true;
        var id = row.ExecutionJobId ?? row.Id;
        return await db.OperationJobs.AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    }
    public Task<bool> MarkPlanningAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null || row.State is not ("Queued" or "Planning")) return false;
        var job = await Job(row.Id, cancellationToken); var now = Now();
        if (row.LeaseExpiresAtUtc <= now || row.PlanningExpiresAtUtc <= now || row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return false;
        row.State = "Planning"; row.Version++;
        if (job.State != "Running")
        { job.State = "Running"; job.Phase = "GeneratingTreePlan"; job.StartedAtUtc ??= now; job.Version++; await Event(job, "FILES_TREE_PLANNING", cancellationToken); }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishPlanningAsync(FileTreeMoveOperation claimed, FileTreeMovePlan? plan, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null) return false;
        var job = await Job(row.Id, cancellationToken); var now = Now(); if (row.LeaseExpiresAtUtc <= now) return false;
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) { plan = null; code = "FILES_CANCELED"; }
        else if (row.State == "Ready" || row.PlanningExpiresAtUtc <= now) { plan = null; code = "FILES_PLAN_EXPIRED"; }
        if (plan is not null)
        {
            FileTreeMoveValidation.Plan(plan);
            if (!FileTreeMoveValidation.HasValidBinding(row, protector) || plan.Command != FileTreeMoveValidation.ReadCommand(row)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
            row.PlanJson = JsonSerializer.Serialize(plan); row.ReadyExpiresAtUtc = now.AddMinutes(5); row.PlanHash = protector.FingerprintPlan(FileTreeMoveValidation.PlanBinding(row));
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
        Audit(row, row.Id, "files.tree-move.plan-result", row.State, row.Id.ToString("D"), null);
        await Processed(row.Id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileUploadStartResult> StartedAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row?.ExecutionJobId is not { } id) return FileUploadStartResult.LeaseLost;
        var job = await Job(id, cancellationToken); var now = Now();
        if (row.LeaseExpiresAtUtc <= now || row.RootStartedAtUtc is not null) return FileUploadStartResult.LeaseLost;
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return FileUploadStartResult.Canceled;
        if (row.ExecutionExpiresAtUtc <= now) return FileUploadStartResult.Expired;
        row.RootStartedAtUtc = now; row.State = "Moving"; row.Version++;
        job.State = "Running"; job.Phase = "ApplyingTreeMove"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_TREE_MOVEING", cancellationToken); await db.SaveChangesAsync(cancellationToken); return FileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> RenewAsync(FileTreeMoveOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        row.LeaseExpiresAtUtc = Now().AddSeconds(90); row.Version++; await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(FileTreeMoveOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        if (row.RootStartedAtUtc is not null && row.ExecutionJobId is { } id && row.State != "Unknown")
        {
            var job = await Job(id, cancellationToken);
            if (row.LeaseExpiresAtUtc <= Now()) return false;
            // A lost response is not evidence that moving is still running.
            // Keep the intent and shared path locks until the root receipt is
            // reconciled; subsequent attempts may inspect/discard, never apply.
            row.State = "Unknown"; row.ResultCode = "FILES_RECEIPT_UNAVAILABLE";
            job.State = "Running"; job.Phase = "AwaitingTreeReceipt"; job.ErrorCode = row.ResultCode; job.Version++;
            await Event(job, row.ResultCode, cancellationToken);
            Audit(row, id, "files.tree-move.result", "Unknown", row.Id.ToString("D"), null);
        }
        row.NextAttemptAtUtc = Now().AddSeconds(6); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
}
