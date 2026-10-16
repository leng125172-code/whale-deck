using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class FileUploadRepository(PlatformDbContext db) : IFileUploadRepository
{
    public Task<FileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.FileUploadOperations.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileUploadOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileUploadPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null) { if (prior.RequestHash != requestHash) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT"); return prior; }
        var now = DateTimeOffset.UtcNow;
        if (claims.ExpiresAtUtc <= now) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == actor.Subject && item.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        var file = claims.File; var targetHash = FileManagementService.Hash(file.RootId + ":" + file.Path);
        await FilePathLocks.Acquire(db, id, actor.Subject, [targetHash], cancellationToken);
        var operation = new FileUploadOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding), RootId = file.RootId, RelativePath = file.Path,
            TargetHash = targetHash, RequestHash = requestHash, ExpectedVersion = file.ExpectedVersion, Sha256 = file.Sha256, SizeBytes = file.SizeBytes,
            ExpiresAtUtc = expires, CreatedAtUtc = now, NextAttemptAtUtc = now.AddSeconds(6) };
        db.FileUploadOperations.Add(operation);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.upload", ActorSubject = actor.Subject, IdempotencyKey = key, Phase = "AwaitingFileUpload",
            RequestJson = JsonSerializer.Serialize(new { rootId = file.RootId, targetHash, sizeBytes = file.SizeBytes, sha256 = file.Sha256 }) });
        db.OutboxMessages.Add(new() { MessageType = "FileUploadRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingFileUpload", MessageCode = "FILES_UPLOAD_ACCEPTED" });
        Audit(actor.Subject, "files.upload.accepted", file.RootId, id, actor.TraceId, actor.SourceIp, "Accepted", targetHash);
        await db.SaveChangesAsync(cancellationToken); return operation;
    }, cancellationToken);
    public Task<FileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var operation = await db.FileUploadOperations.FromSqlInterpolated($"SELECT * FROM file_upload_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("FILES_UPLOAD_NOT_FOUND");
        var job = await db.OperationJobs.SingleAsync(item => item.Id == id, cancellationToken);
        if (operation.ActorSubject != actor.Subject || operation.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_UPLOAD_NOT_FOUND");
        if (operation.State != "AwaitingUpload" || job.CancelRequestedAtUtc is not null || operation.ExpiresAtUtc <= now) throw new InvalidOperationException("FILES_UPLOAD_CANNOT_RECEIVE");
        operation.ReceiveToken = Guid.NewGuid(); operation.State = "Receiving"; operation.Version++; operation.NextAttemptAtUtc = now.AddSeconds(15);
        job.State = "Running"; job.Phase = "ReceivingFile"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_RECEIVING", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return operation;
    }, cancellationToken);
    public Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => db.FileUploadOperations.AsNoTracking()
        .AnyAsync(item => item.Id == id && item.ReceiveToken == token && item.State == "Receiving" && item.ExpiresAtUtc > now &&
            !db.OperationJobs.Any(job => job.Id == item.Id && job.CancelRequestedAtUtc != null), cancellationToken);
    public async Task ReadyAsync(FileUploadOperation operation, CancellationToken cancellationToken) => _ = await db.FileUploadOperations
        .Where(item => item.Id == operation.Id && item.State == "Receiving" && item.ReceiveToken == operation.ReceiveToken)
        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, "Ready").SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow)
            .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var operation = await OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_UPLOAD_NOT_FOUND");
        if (operation.CompletedAtUtc is not null) return true;
        var job = await db.OperationJobs.SingleAsync(item => item.Id == id, cancellationToken);
        if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = DateTimeOffset.UtcNow; job.Version++;
            db.OutboxMessages.Add(new() { MessageType = "FileUploadCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
            Audit(actor.Subject, "files.upload.cancel-requested", operation.RootId, id, actor.TraceId, actor.SourceIp, "Accepted", operation.TargetHash);
        }
        await db.FileUploadOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.FileUploadOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken);
            if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM file_upload_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE file_upload_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.FileUploadOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => db.OperationJobs.AsNoTracking().AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    public Task<FileUploadStartResult> StartedAsync(FileUploadOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null) return FileUploadStartResult.LeaseLost;
        var job = await db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={current.Id} FOR UPDATE").SingleAsync(cancellationToken);
        now = DateTimeOffset.UtcNow;
        if (current.LeaseExpiresAtUtc is null || current.LeaseExpiresAtUtc <= now) return FileUploadStartResult.LeaseLost;
        if (job.CancelRequestedAtUtc is not null) return FileUploadStartResult.Canceled;
        if (current.ExpiresAtUtc <= now) return FileUploadStartResult.Expired;
        await db.FileUploadOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, "Publishing")
            .SetProperty(item => item.RootStartedAtUtc, item => item.RootStartedAtUtc ?? now).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        job.State = "Running"; job.Phase = "PublishingFile"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_PUBLISHING", cancellationToken); await db.SaveChangesAsync(cancellationToken); return FileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> FinishAsync(FileUploadOperation operation, string state, string code, FileUploadReceiptDto? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Superseded" or "Unknown" or "Canceled" or "Failed")) throw new ArgumentException("FILES_RESULT_INVALID");
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        if (state is "Succeeded" or "Superseded")
        {
            if (receipt is null || receipt.State != "Completed" || receipt.SizeBytes != current.SizeBytes || receipt.Sha256 != current.Sha256 ||
                !FileManagementService.Hex(receipt.Version) || state == "Succeeded" && !receipt.BindingCurrent) throw new InvalidOperationException("FILES_RECEIPT_MISMATCH");
        }
        if (state is "Failed" or "Canceled" && receipt?.State != "Aborted") throw new InvalidOperationException("FILES_ABORT_RECEIPT_REQUIRED");
        var terminal = state != "Unknown"; var now = DateTimeOffset.UtcNow;
        await db.FileUploadOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, state)
            .SetProperty(item => item.ResultCode, code).SetProperty(item => item.PublishedVersion, receipt != null && receipt.State == "Completed" ? receipt.Version : null)
            .SetProperty(item => item.PreviousRetained, receipt != null && receipt.PreviousRetained).SetProperty(item => item.CompletedAtUtc, terminal ? now : null)
            .SetProperty(item => item.NextAttemptAtUtc, now.AddSeconds(30)).SetProperty(item => item.LeaseToken, (Guid?)null)
            .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = await db.OperationJobs.SingleAsync(item => item.Id == current.Id, cancellationToken);
        var phase = terminal ? state : "AwaitingFileReceipt";
        if (job.Phase != phase || terminal)
        {
            job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
            job.Phase = phase; job.ErrorCode = state == "Succeeded" ? null : code; job.CompletedAtUtc = terminal ? now : null;
            job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
            await Event(job, code, cancellationToken);
            Audit(current.ActorSubject, "files.upload.result", current.RootId, current.Id, current.Id.ToString("D"), null, state, current.TargetHash);
        }
        if (terminal)
        {
            await FilePathLocks.Release(db, current.Id, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('FileUploadRequested','FileUploadCancellationRequested') AND payload_json->>'jobId'={current.Id.ToString("D")}", cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(FileUploadOperation operation, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(operation, cancellationToken); if (current is null) return false;
        await db.FileUploadOperations.Where(item => item.Id == current.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(6))
            .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return true;
    }, cancellationToken);
    public async Task AuditDownloadAsync(TerminalActor actor, FileLocationDto location, string result, CancellationToken cancellationToken)
    {
        if (result is not ("Started" or "Validated" or "Incomplete")) throw new ArgumentException("FILES_DOWNLOAD_RESULT_INVALID");
        Audit(actor.Subject, "files.download", location.RootId, null, actor.TraceId, actor.SourceIp, result, FileManagementService.Hash(location.RootId + ":" + location.Path));
        await db.SaveChangesAsync(cancellationToken);
    }
    private async Task<FileUploadOperation?> Lock(FileUploadOperation operation, CancellationToken cancellationToken)
    {
        var current = await db.FileUploadOperations.FromSqlInterpolated($"SELECT * FROM file_upload_operations WHERE id={operation.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return operation.LeaseToken is not null && current?.LeaseToken == operation.LeaseToken && current.CompletedAtUtc is null && current.LeaseExpiresAtUtc > DateTimeOffset.UtcNow ? current : null;
    }
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(string actor, string action, string root, Guid? job, string trace, string? source, string result, string target) => db.AuditEvents.Add(new()
    { ActorSubject = actor, Action = action, TargetType = "file-root", TargetId = root, JobId = job, TraceId = trace, SourceIp = source, Result = result, DetailJson = JsonSerializer.Serialize(new { targetHash = target }) });
    private Task<T> Transaction<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await operation(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
