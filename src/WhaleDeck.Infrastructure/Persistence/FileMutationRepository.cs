using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class FileMutationRepository(PlatformDbContext db) : IFileMutationRepository
{
    public Task<FileMutationOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.FileMutationOperations.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileMutationOperation?> ResolveExpiredRegistrationAsync(TerminalActor actor, Guid id, DateTimeOffset planExpiry, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken);
        if (row is null && planExpiry > DateTimeOffset.UtcNow) throw new InvalidOperationException("FILES_REGISTRATION_STILL_POSSIBLE");
        // The same transaction gate fences a delayed Enqueue. Its expiry check
        // is inside that gate, so null now proves it cannot register later.
        return row;
    }, cancellationToken);
    public Task<FileMutationOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileMutationPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null) { if (prior.RequestHash != requestHash) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT"); return prior; }
        var now = DateTimeOffset.UtcNow; FileMutationService.Validate(claims.Plan);
        if (claims.ExpiresAtUtc <= now) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == actor.Subject && item.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.Acquire(db, id, actor.Subject, FileMutationService.PathHashes(claims.Plan), cancellationToken);
        var row = new FileMutationOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding), RequestHash = requestHash,
            PlanJson = JsonSerializer.Serialize(claims.Plan), ExpiresAtUtc = expires, CreatedAtUtc = now, NextAttemptAtUtc = now };
        db.FileMutationOperations.Add(row);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.mutation", ActorSubject = actor.Subject, IdempotencyKey = key, Phase = "AwaitingFileMutation",
            RequestJson = JsonSerializer.Serialize(new { kind = claims.Plan.Command.Kind, pathHashes = FileMutationService.PathHashes(claims.Plan), sizeBytes = claims.Plan.SizeBytes }) });
        db.OutboxMessages.Add(new() { MessageType = "FileMutationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingFileMutation", MessageCode = "FILES_MUTATION_ACCEPTED" });
        Audit(actor.Subject, "files.mutation.accepted", row, actor.TraceId, actor.SourceIp, "Accepted");
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.CompletedAtUtc is not null) return true;
        var job = await Job(id, cancellationToken);
        if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = DateTimeOffset.UtcNow; job.Version++;
            db.OutboxMessages.Add(new() { MessageType = "FileMutationCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
            Audit(actor.Subject, "files.mutation.cancel-requested", row, actor.TraceId, actor.SourceIp, "Accepted");
            await db.FileMutationOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileMutationOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.FileMutationOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken);
            if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM file_mutation_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE file_mutation_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.FileMutationOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => db.OperationJobs.AsNoTracking()
        .AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    public Task<FileUploadStartResult> StartedAsync(FileMutationOperation operation, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null) return FileUploadStartResult.LeaseLost;
        var job = await Job(current.Id, cancellationToken); var now = DateTimeOffset.UtcNow;
        // Re-check after waiting for the Job row; lease/cancellation cannot be
        // validated only before a blocked lock acquisition.
        if (current.LeaseExpiresAtUtc <= now) return FileUploadStartResult.LeaseLost;
        if (job.CancelRequestedAtUtc is not null) return FileUploadStartResult.Canceled;
        if (current.ExpiresAtUtc <= now) return FileUploadStartResult.Expired;
        if (current.RootStartedAtUtc is not null) return FileUploadStartResult.LeaseLost;
        current.RootStartedAtUtc = now; current.State = "Running"; current.Version++;
        job.State = "Running"; job.Phase = "ApplyingFileMutation"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_MUTATION_APPLYING", cancellationToken); await db.SaveChangesAsync(cancellationToken);
        return FileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> FinishAsync(FileMutationOperation operation, string state, string code, FileMutationReceipt? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Superseded" or "Unknown" or "Canceled" or "Failed")) throw new ArgumentException("FILES_RESULT_INVALID");
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        if (receipt is not null) FileMutationService.ValidateReceipt(FileMutationService.ReadPlan(current), receipt);
        if (state is "Succeeded" or "Superseded" && (receipt?.State != "Completed" || state == "Succeeded" && !receipt.BindingCurrent)) throw new InvalidOperationException("FILES_RECEIPT_REQUIRED");
        if (state is "Canceled" or "Failed" && receipt?.State != "Aborted") throw new InvalidOperationException("FILES_ABORT_RECEIPT_REQUIRED");
        var job = await Job(current.Id, cancellationToken); var now = DateTimeOffset.UtcNow;
        if (current.LeaseExpiresAtUtc <= now) return false;
        var changed = current.State != state; var terminal = state != "Unknown";
        current.State = state; current.ResultCode = code; current.CompletedAtUtc = terminal ? now : null;
        current.PublishedVersion = receipt?.State == "Completed" ? receipt.Version : null; current.SourceRetained = receipt?.SourceRetained == true;
        current.NextAttemptAtUtc = now.AddSeconds(30); current.LeaseToken = null; current.LeaseExpiresAtUtc = null; current.Version++;
        if (changed || terminal)
        {
            job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
            job.Phase = terminal ? state : "AwaitingFileReceipt"; job.ErrorCode = state == "Succeeded" ? null : code;
            job.CompletedAtUtc = terminal ? now : null; job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
            await Event(job, code, cancellationToken); Audit(current.ActorSubject, "files.mutation.result", current, current.Id.ToString("D"), null, state);
        }
        if (terminal) await CompleteOutboxAndUnlock(current.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(FileMutationOperation operation, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        current.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(6); current.LeaseToken = null; current.LeaseExpiresAtUtc = null; current.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileMutationReceipt receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.FileMutationOperations.FromSqlInterpolated($"SELECT * FROM file_mutation_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.State != "Unknown" || row.Version != version || row.CompletedAtUtc is not null || row.LeaseExpiresAtUtc > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        FileMutationService.ValidateReceipt(FileMutationService.ReadPlan(row), receipt);
        // Human acceptance of an uncertain historical outcome, NOT success,
        // rollback or deletion of root evidence. The root was fenced first.
        var now = DateTimeOffset.UtcNow; row.State = "Acknowledged"; row.CompletedAtUtc = now; row.Version++;
        row.ResultCode = "FILES_UNKNOWN_ACKNOWLEDGED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var job = await Job(id, cancellationToken); job.State = "Failed"; job.Phase = "Acknowledged"; job.ErrorCode = row.ResultCode; job.CompletedAtUtc = now; job.Version++;
        await Event(job, row.ResultCode, cancellationToken); Audit(actor.Subject, "files.mutation.unknown-acknowledged", row, actor.TraceId, actor.SourceIp, receipt.State);
        await CompleteOutboxAndUnlock(id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task<FileMutationOperation?> Lock(FileMutationOperation row, CancellationToken cancellationToken)
    {
        var current = await db.FileMutationOperations.FromSqlInterpolated($"SELECT * FROM file_mutation_operations WHERE id={row.Id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        return row.LeaseToken is not null && current?.LeaseToken == row.LeaseToken && current.CompletedAtUtc is null && current.LeaseExpiresAtUtc > DateTimeOffset.UtcNow ? current : null;
    }
    private Task<OperationJob> Job(Guid id, CancellationToken cancellationToken) => db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private async Task CompleteOutboxAndUnlock(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await FilePathLocks.Release(db, id, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('FileMutationRequested','FileMutationCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    }
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(string actor, string action, FileMutationOperation row, string trace, string? source, string result)
    {
        FileMutationPlan? plan = null;
        try { plan = FileMutationService.ReadPlan(row); FileMutationService.Validate(plan); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { plan = null; }
        var root = (plan?.Command.Target ?? plan?.Command.Source)?.RootId ?? "invalid-intent";
        db.AuditEvents.Add(new() { ActorSubject = actor, Action = action, TargetType = "file-root", TargetId = root, JobId = row.Id, TraceId = trace,
            SourceIp = source, Result = result, DetailJson = JsonSerializer.Serialize(new { kind = plan?.Command.Kind ?? "invalid", pathHashes = plan is null ? [] : FileMutationService.PathHashes(plan), fingerprint = plan?.Fingerprint }) });
    }
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
