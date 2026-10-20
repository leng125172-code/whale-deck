using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileRecoveryRepository(PlatformDbContext db, FileRecoverySources sources, FileRecoveryMaterial material,
    FileArchiveRepository archiveCatalog, IFileRecoveryProtector protector, TimeProvider clock, FileTreeRecyclingRepository? recyclingCatalog = null,
    FileTreeOverwriteRestoringRepository? overwriteCatalog = null) : IFileRecoveryRepository
{
    private DateTimeOffset Now() => FileTreeCopyValidation.Timestamp(clock.GetUtcNow());
    public Task<FileRecoveryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) =>
        db.FileRecoveryOperations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.ActorSubject == actor.Subject && row.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileRecoveryOperation> QueueAsync(TerminalActor actor, string key, QueueFileRecoveryCommand command, CancellationToken cancellationToken) => Transaction(async () =>
    {
        FileRecoveryValidation.Command(command); var id = FileRecoveryValidation.RegistrationId(actor, key);
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null)
        {
            Bound(prior);
            if (prior.Kind != command.Kind || prior.OriginalIntentId != command.IntentId || prior.OriginalVersion != command.OriginalVersion ||
                prior.Disposition != command.Disposition || prior.ReasonCode != command.ReasonCode) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        if (await db.OperationJobs.AnyAsync(row => row.ActorSubject == actor.Subject && row.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        if (await db.FileRecoveryOperations.AnyAsync(row => row.CompletedAtUtc == null && row.Kind == command.Kind && row.OriginalIntentId == command.IntentId, cancellationToken))
            throw new InvalidOperationException("FILES_RECOVERY_ALREADY_RESERVED");
        if (await db.FileRecoveryOperations.CountAsync(row => row.CompletedAtUtc == null, cancellationToken) >= 64 ||
            await db.FileRecoveryOperations.CountAsync(row => row.CompletedAtUtc == null && row.ActorSubject == actor.Subject, cancellationToken) >= 4)
            throw new InvalidOperationException("FILES_RECOVERY_BUDGET_EXCEEDED");
        var original = await sources.LockAsync(command.Kind, command.IntentId, cancellationToken);
        if (original.Binding.OriginalVersion != command.OriginalVersion) throw new InvalidOperationException("FILES_RECOVERY_ORIGINAL_CHANGED");
        var now = Now(); var row = new FileRecoveryOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            Kind = command.Kind, OriginalIntentId = command.IntentId, OriginalExecutionJobId = original.Binding.ExecutionJobId,
            OriginalVersion = command.OriginalVersion, OriginalBindingHash = original.Binding.OriginalBindingHash, OriginalJson = original.OriginalJson,
            Disposition = command.Disposition, ReasonCode = command.ReasonCode, RequestHash = "", CreatedAtUtc = now, PlanningExpiresAtUtc = now.AddMinutes(15), NextAttemptAtUtc = now };
        row = Seal(row); db.FileRecoveryOperations.Add(row);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.recovery-plan", ActorSubject = actor.Subject, IdempotencyKey = key,
            Phase = "AwaitingRecoveryPlan", RequestJson = JsonSerializer.Serialize(new { command.Kind, command.IntentId, command.Disposition, command.ReasonCode }) });
        Message(id); db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingRecoveryPlan", MessageCode = "FILES_RECOVERY_PLAN_ACCEPTED" });
        Audit(row, id, "files.recovery.plan-accepted", "Accepted", actor.TraceId, actor.SourceIp); await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    private FileRecoveryOperation Seal(FileRecoveryOperation row)
    {
        // Init-only request identity remains immutable after this one construction.
        var data = JsonSerializer.SerializeToNode(row)!; data[nameof(FileRecoveryOperation.RequestHash)] = protector.FingerprintRecoveryRequest(FileRecoveryValidation.Request(row));
        return data.Deserialize<FileRecoveryOperation>()!;
    }
    public Task<FileRecoveryOperation> ConfirmAsync(TerminalActor actor, FileRecoveryPlanClaims claims, long version, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(claims.Id, cancellationToken); Owner(row, actor); Bound(row);
        if (FileRecoveryValidation.Ready(row) != claims) throw new InvalidOperationException("FILES_RECOVERY_PLAN_CHANGED");
        if (row.ExecutionJobId is not null) return row;
        var planJob = await Job(row.Id, cancellationToken);
        if (row.State != "Ready" || row.Version != version || row.CancelRequestedAtUtc is not null || planJob.CancelRequestedAtUtc is not null)
            throw new InvalidOperationException("FILES_RECOVERY_CONFIRMATION_CONFLICT");
        FileRecoveryValidation.RequireCurrentActor(claims, actor, Now()); await sources.RequireAsync(row, cancellationToken);
        FileRecoveryValidation.RequireCurrentActor(claims, actor, Now());
        var id = FileRecoveryValidation.ExecutionId(row.Id); var now = Now(); row.ExecutionJobId = id; row.ExecutionExpiresAtUtc = now.AddMinutes(15);
        row.State = "DispositionQueued"; row.NextAttemptAtUtc = now; row.ResultCode = "FILES_RECOVERY_ACCEPTED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        db.OperationJobs.Add(new() { Id = id, JobType = "files.recovery", ActorSubject = actor.Subject, IdempotencyKey = id.ToString("D"), Phase = "AwaitingRecoveryFence",
            RequestJson = JsonSerializer.Serialize(new { recoveryId = row.Id, row.Kind, row.OriginalIntentId, row.Disposition, row.ReasonCode }) });
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingRecoveryFence", MessageCode = row.ResultCode });
        Message(id); Audit(row, id, "files.recovery.confirmed", "Accepted", actor.TraceId, actor.SourceIp); await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); Owner(row, actor); Bound(row);
        if (row.CompletedAtUtc is not null) return true;
        if (row.FenceStartedAtUtc is not null) throw new InvalidOperationException("FILES_RECOVERY_FENCE_ALREADY_STARTED");
        row.CancelRequestedAtUtc ??= Now(); await End(row, "Canceled", "FILES_RECOVERY_CANCELED", cancellationToken);
        Audit(row, row.ExecutionJobId ?? row.Id, "files.recovery.canceled", "Canceled", actor.TraceId, actor.SourceIp);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileRecoveryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var token = Guid.NewGuid(); var expiry = now.AddSeconds(90);
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
              SELECT id FROM file_recovery_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                AND (state<>'Ready' OR ready_expires_at_utc<={now} OR cancel_requested_at_utc IS NOT NULL
                  OR EXISTS (SELECT 1 FROM operation_jobs j WHERE j.id=file_recovery_operations.id AND j.cancel_requested_at_utc IS NOT NULL))
                AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
              ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
            ) UPDATE file_recovery_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
              FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
            """).ToArrayAsync(cancellationToken);
        return ids.Length == 0 ? null : await db.FileRecoveryOperations.AsNoTracking().SingleAsync(row => row.Id == ids[0], cancellationToken);
    }, cancellationToken);
    public Task<bool> RenewAsync(FileRecoveryOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        var job = await Job(row.ExecutionJobId ?? row.Id, cancellationToken);
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) return false;
        await sources.RequireAsync(row, cancellationToken);
        if (row.LeaseExpiresAtUtc <= Now()) return false;
        row.LeaseExpiresAtUtc = Now().AddSeconds(90); row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishPlanningAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is not null) return false; Bound(row);
        var job = await Job(row.Id, cancellationToken);
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null) await End(row, "Canceled", "FILES_RECOVERY_CANCELED", cancellationToken);
        else if (row.PlanningExpiresAtUtc <= Now() || row.State == "Ready") await End(row, "Expired", "FILES_RECOVERY_PLAN_EXPIRED", cancellationToken);
        else if (observation is null) await End(row, "Failed", code, cancellationToken);
        else
        {
            await sources.RequireAsync(row, cancellationToken); material.ValidateObservation(FileRecoveryValidation.Snapshot(row), observation);
            RequireLive(row);
            if ((observation.State == "Completed") != (row.Disposition == "ReconcileCompleted")) await End(row, "Failed", "FILES_RECOVERY_DISPOSITION_MISMATCH", cancellationToken);
            else
            {
                row.RootState = observation.State; row.ObservationHash = observation.ReceiptHash; row.ObservationJson = observation.ReceiptJson;
                var readyAt = Now();
                row.ObservationBindingCurrent = observation.BindingCurrent; row.ReadyAtUtc = readyAt; row.ReadyExpiresAtUtc = readyAt.AddMinutes(5);
                row.PlanHash = protector.FingerprintRecoveryReady(FileRecoveryValidation.Ready(row)); row.State = "Ready"; row.ResultCode = "FILES_RECOVERY_READY";
                row.NextAttemptAtUtc = row.ReadyExpiresAtUtc.Value; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
                await FinishJob(job, "Succeeded", "RecoveryPlanReady", row.ResultCode, cancellationToken); await Processed(row.Id, cancellationToken);
                Audit(row, row.Id, "files.recovery.plan-ready", "Ready", row.Id.ToString("D"), null);
            }
        }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> BeginFenceAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is null) return false; Bound(row);
        var job = await Job(row.ExecutionJobId.Value, cancellationToken);
        if (row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null || row.ExecutionExpiresAtUtc <= Now()) return false;
        await sources.RequireAsync(row, cancellationToken); RequireLive(row);
        row.FenceStartedAtUtc ??= Now(); row.State = "Fencing"; row.Version++;
        job.State = "Running"; job.Phase = "FencingOriginalOperation"; job.Version++; await Event(job, "FILES_RECOVERY_FENCING", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is null) return false; Bound(row);
        if (observation is null) await End(row, "Failed", code, cancellationToken);
        else
        {
            if (row.FenceStartedAtUtc is null) throw new InvalidOperationException("FILES_RECOVERY_FENCE_REQUIRED");
            material.ValidateObservation(FileRecoveryValidation.Snapshot(row), observation);
            var same = observation.ReceiptHash == row.ObservationHash;
            if (!same && !(row.RootState == "Missing" && observation.State == "Aborted" && row.Disposition == "AcknowledgeUnknown"))
                await End(row, "Failed", "FILES_RECOVERY_RECEIPT_CHANGED", cancellationToken);
            else
            {
                row.OriginalResolution = await sources.DisposeAsync(row, observation, archiveCatalog, cancellationToken, recyclingCatalog, overwriteCatalog);
                // The original Job lock and catalogue queries may have waited.
                // Roll the whole transaction back if this lease/deadline expired.
                RequireLive(row);
                await End(row, "Disposed", "FILES_RECOVERY_DISPOSED", cancellationToken);
                Audit(row, row.ExecutionJobId.Value, "files.recovery.original-disposed", row.OriginalResolution, row.Id.ToString("D"), null);
            }
        }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lease(claimed, cancellationToken); if (row is null) return false;
        if (row.FenceStartedAtUtc is not null && row.State != "Unknown")
        {
            row.State = "Unknown"; row.ResultCode = "FILES_RECOVERY_FENCE_UNCERTAIN";
            var job = await Job(row.ExecutionJobId!.Value, cancellationToken); job.Phase = "AwaitingOriginalFenceReceipt"; job.ErrorCode = row.ResultCode; job.Version++;
            await Event(job, row.ResultCode, cancellationToken);
        }
        row.NextAttemptAtUtc = Now().AddSeconds(6); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task End(FileRecoveryOperation row, string state, string code, CancellationToken cancellationToken)
    {
        row.State = state; row.ResultCode = code; row.CompletedAtUtc = Now(); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        var job = await Job(row.ExecutionJobId ?? row.Id, cancellationToken);
        // A ready planning Job is historical success and is never rewritten.
        if (job.CompletedAtUtc is null) await FinishJob(job, state == "Disposed" ? "Succeeded" : state == "Canceled" ? "Canceled" : "Failed", state, code, cancellationToken);
        await Processed(row.ExecutionJobId ?? row.Id, cancellationToken);
        Audit(row, row.ExecutionJobId ?? row.Id, "files.recovery.result", state, row.Id.ToString("D"), null);
    }
    private void Bound(FileRecoveryOperation row) { if (!FileRecoveryValidation.Bound(row, protector)) throw new InvalidOperationException("FILES_RECOVERY_BINDING_CHANGED"); }
    private void RequireLive(FileRecoveryOperation row)
    {
        if (row.LeaseExpiresAtUtc <= Now()) throw new InvalidOperationException("FILES_RECOVERY_LEASE_LOST");
        if ((row.ExecutionJobId is null ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc) <= Now())
            throw new InvalidOperationException("FILES_RECOVERY_DEADLINE_EXPIRED");
    }
    private static void Owner(FileRecoveryOperation row, TerminalActor actor)
    { if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_RECOVERY_NOT_FOUND"); }
    private Task<FileRecoveryOperation> Row(Guid id, CancellationToken cancellationToken) => db.FileRecoveryOperations.FromSqlInterpolated($"SELECT * FROM file_recovery_operations WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private async Task<FileRecoveryOperation?> Lease(FileRecoveryOperation claimed, CancellationToken cancellationToken)
    {
        var row = await Row(claimed.Id, cancellationToken);
        return row.CompletedAtUtc is null && claimed.LeaseToken is not null && row.LeaseToken == claimed.LeaseToken && row.LeaseExpiresAtUtc > Now() ? row : null;
    }
    private Task<OperationJob> Job(Guid id, CancellationToken cancellationToken) => db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private void Message(Guid id) => db.OutboxMessages.Add(new() { MessageType = "FileRecoveryRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
    private Task<int> Processed(Guid id, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={Now()} WHERE message_type IN ('FileRecoveryRequested','FileRecoveryCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(row => row.JobId == job.Id).MaxAsync(row => (long?)row.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private async Task FinishJob(OperationJob job, string state, string phase, string code, CancellationToken cancellationToken)
    { job.State = state; job.Phase = phase; job.ErrorCode = state == "Succeeded" ? null : code; job.CompletedAtUtc = Now(); job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++; await Event(job, code, cancellationToken); }
    private void Audit(FileRecoveryOperation row, Guid job, string action, string result, string trace, string? source) => db.AuditEvents.Add(new()
    {
        ActorSubject = row.ActorSubject, Action = action, TargetType = "file-recovery", TargetId = row.Id.ToString("D"), JobId = job, TraceId = trace, SourceIp = source,
        Result = result, DetailJson = JsonSerializer.Serialize(new { row.Kind, row.OriginalIntentId, row.OriginalExecutionJobId, row.Disposition, row.ReasonCode, row.OriginalResolution })
    });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
