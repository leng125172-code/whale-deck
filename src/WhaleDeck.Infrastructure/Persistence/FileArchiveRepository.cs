using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class FileArchiveRepository(PlatformDbContext db, IFileArchiveProtector protector) : IFileArchiveRepository
{
    public Task<FileArchiveRegistration> RememberAsync(TerminalActor actor, Guid id, string key, FileArchivePlan plan, DateTimeOffset expires, CancellationToken cancellationToken) => Transaction(async () =>
    {
        FileArchiveValidation.Plan(plan);
        if (id != FileArchiveService.IntentId(actor, key)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        var prior = await db.FileArchiveRegistrations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (prior is not null)
        {
            if (prior.ActorSubject != actor.Subject || prior.LoginBinding != actor.LoginBinding || prior.IdempotencyKey != key ||
                prior.RequestHash != protector.FingerprintRegistration(new(id, actor.Subject, actor.LoginBinding, plan, prior.ExpiresAtUtc))) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
            if (prior.CanceledAtUtc is not null || prior.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("FILES_PLAN_EXPIRED"); return prior;
        }
        var now = DateTimeOffset.UtcNow; if (expires <= now || expires > now.AddMinutes(5)) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        if (await db.FileArchiveRegistrations.Where(item => !db.FileArchiveOperations.Any(operation => operation.Id == item.Id)).Take(50000).CountAsync(cancellationToken) >= 50000 ||
            await db.FileArchiveRegistrations.CountAsync(item => item.CanceledAtUtc == null && item.ExpiresAtUtc > now && !db.FileArchiveOperations.Any(operation => operation.Id == item.Id), cancellationToken) >= 128 ||
            await db.FileArchiveRegistrations.CountAsync(item => item.ActorSubject == actor.Subject && item.CanceledAtUtc == null && item.ExpiresAtUtc > now && !db.FileArchiveOperations.Any(operation => operation.Id == item.Id), cancellationToken) >= 16)
            throw new InvalidOperationException("FILES_OPERATION_BUDGET_EXHAUSTED");
        var row = new FileArchiveRegistration { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key, PlanJson = JsonSerializer.Serialize(plan),
            RequestHash = protector.FingerprintRegistration(new(id, actor.Subject, actor.LoginBinding, plan, expires)), CreatedAtUtc = now, ExpiresAtUtc = expires };
        db.FileArchiveRegistrations.Add(row); await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task<(FileArchiveRegistration? Registration, FileArchiveOperation? Operation, bool Expired)> RegistrationAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.FileArchiveRegistrations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
        var operation = await OwnedAsync(actor, id, cancellationToken);
        return (row, operation, row is not null && row.ExpiresAtUtc <= DateTimeOffset.UtcNow);
    }, cancellationToken);
    public Task<int> PruneRegistrationsAsync(DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var cutoff = now.AddDays(-1);
        var ids = await db.FileArchiveRegistrations.Where(item => item.ExpiresAtUtc < cutoff && !db.FileArchiveOperations.Any(operation => operation.Id == item.Id))
            .OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.Id).Select(item => item.Id).Take(1000).ToArrayAsync(cancellationToken);
        return await db.FileArchiveRegistrations.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
    }, cancellationToken);
    public Task CancelRegistrationAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.FileArchiveRegistrations.SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken)
            ?? throw new KeyNotFoundException("FILES_PLAN_NOT_FOUND");
        if (await db.FileArchiveOperations.AnyAsync(item => item.Id == id, cancellationToken)) return true;
        row.CanceledAtUtc ??= DateTimeOffset.UtcNow; await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task ObserveAsync(IReadOnlyCollection<FileVersionDto> versions, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (versions.Count > 50) throw new ArgumentException("FILES_ARCHIVE_LIST_INVALID");
        foreach (var version in versions) await Observe(version, null, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task Observe(FileVersionDto value, Guid? source, string? actor, CancellationToken cancellationToken)
    {
        FileArchiveValidation.History(new(value.RootId, "", "", 1, ""), new([value], "", new('A', 64), 1, 0, true));
        var hash = FileManagementService.Hash(value.RootId + ":" + value.Path);
        var row = await db.FileVersions.SingleOrDefaultAsync(item => item.Id == value.Id, cancellationToken);
        if (row is null)
        {
            row = new() { Id = value.Id, SourceJobId = source, CreatedBySubject = actor, RootId = value.RootId, RelativePath = value.Path, TargetHash = hash,
                SourceVersion = value.SourceVersion, Sha256 = value.Sha256, SizeBytes = value.SizeBytes, CaptureKind = value.CaptureKind, CaptureState = value.CaptureState,
                Availability = value.Availability, DifferentSourceDevice = value.DifferentSourceDevice, CapturedAtUtc = value.CapturedAtUtc, LastObservedAtUtc = DateTimeOffset.UtcNow };
            db.FileVersions.Add(row); return;
        }
        if (row.RootId != value.RootId || row.RelativePath != value.Path || row.TargetHash != hash || row.SourceVersion != value.SourceVersion || row.Sha256 != value.Sha256 ||
            row.SizeBytes != value.SizeBytes || row.CaptureKind != value.CaptureKind || row.DifferentSourceDevice != value.DifferentSourceDevice || row.CapturedAtUtc != value.CapturedAtUtc)
            throw new InvalidOperationException("FILES_ARCHIVE_CATALOG_CONFLICT");
        if (source is not null)
        {
            if (source != row.Id || row.SourceJobId is not null && (row.SourceJobId != source || row.CreatedBySubject != actor)) throw new InvalidOperationException("FILES_ARCHIVE_CATALOG_CONFLICT");
            row.SourceJobId = source; row.CreatedBySubject = actor;
        }
        if (row.CaptureState != "Purged")
        {
            if (row.CaptureState == "Pending" || row.CaptureState == "Trashed" && value.CaptureState == "Restored") row.CaptureState = value.CaptureState;
            row.Availability = value.Availability;
        }
        row.LastObservedAtUtc = DateTimeOffset.UtcNow; row.Version++;
    }
    public Task<FileArchiveOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.FileArchiveOperations.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<FileArchiveOperation?> ResolveExpiredRegistrationAsync(TerminalActor actor, Guid id, DateTimeOffset expiry, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken);
        if (row is null && expiry > DateTimeOffset.UtcNow) throw new InvalidOperationException("FILES_REGISTRATION_STILL_POSSIBLE"); return row;
    }, cancellationToken);
    public Task<FileArchiveOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileArchivePlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var prior = await OwnedAsync(actor, id, cancellationToken);
        if (prior is not null) { if (prior.RequestHash != requestHash) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT"); return prior; }
        var now = DateTimeOffset.UtcNow; FileArchiveValidation.Plan(claims.Plan);
        if (claims.ExpiresAtUtc <= now || expires <= now) throw new InvalidOperationException("FILES_PLAN_EXPIRED");
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_PLAN_OWNER_REQUIRED");
        var registration = await db.FileArchiveRegistrations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (id != FileArchiveService.IntentId(actor, key) || claims.IdempotencyKey != key || registration is null || registration.ActorSubject != actor.Subject || registration.LoginBinding != actor.LoginBinding ||
            registration.IdempotencyKey != key || registration.ExpiresAtUtc != claims.ExpiresAtUtc || registration.CanceledAtUtc is not null || registration.ExpiresAtUtc <= now ||
            registration.RequestHash != protector.FingerprintRegistration(new(id, actor.Subject, actor.LoginBinding, claims.Plan, registration.ExpiresAtUtc)))
            throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == actor.Subject && item.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("FILES_IDEMPOTENCY_CONFLICT");
        var command = claims.Plan.Command;
        if (command.VersionId is { } version && !await db.FileVersions.AnyAsync(item => item.Id == version && item.RootId == command.Location.RootId &&
            item.RelativePath == command.Location.Path && item.CaptureState != "Purged" && item.Sha256 == claims.Plan.Sha256 && item.SizeBytes == claims.Plan.SizeBytes, cancellationToken))
            throw new InvalidOperationException("FILES_ARCHIVE_CATALOG_CONFLICT");
        await FilePathLocks.Acquire(db, id, actor.Subject, [command.Location],
            command.VersionId is { } lockedVersion ? [FileManagementService.Hash("file-version-v1:" + lockedVersion.ToString("N"))] : [], cancellationToken);
        var row = new FileArchiveOperation { Id = id, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, RequestHash = requestHash,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding), Kind = command.Kind, RootId = command.Location.RootId,
            RelativePath = command.Location.Path, TargetHash = FileManagementService.Hash(command.Location.RootId + ":" + command.Location.Path), ArchiveVersionId = command.VersionId,
            ExpectedVersion = command.ExpectedVersion, Sha256 = claims.Plan.Sha256, SizeBytes = claims.Plan.SizeBytes, PlanJson = JsonSerializer.Serialize(claims.Plan),
            CreatedAtUtc = now, ExpiresAtUtc = expires, NextAttemptAtUtc = now };
        if (!FileArchiveValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        db.FileArchiveOperations.Add(row);
        db.OperationJobs.Add(new() { Id = id, JobType = "files.archive", ActorSubject = actor.Subject, IdempotencyKey = key, Phase = "AwaitingFileArchive",
            RequestJson = JsonSerializer.Serialize(new { kind = command.Kind, pathHashes = FileArchiveService.PathHashes(claims.Plan), sizeBytes = row.SizeBytes }) });
        db.OutboxMessages.Add(new() { MessageType = "FileArchiveRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = "Queued", Phase = "AwaitingFileArchive", MessageCode = "FILES_ARCHIVE_ACCEPTED" });
        Audit(actor.Subject, "files.archive.accepted", row, actor.TraceId, actor.SourceIp, "Accepted"); await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.CompletedAtUtc is not null) return true;
        var job = await Job(id, cancellationToken);
        if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = DateTimeOffset.UtcNow; job.Version++;
            db.OutboxMessages.Add(new() { MessageType = "FileArchiveCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
            Audit(actor.Subject, "files.archive.cancel-requested", row, actor.TraceId, actor.SourceIp, "Accepted");
            await db.FileArchiveOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<FileArchiveOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        return Transaction(async () =>
        {
            var prior = await db.FileArchiveOperations.AsNoTracking().SingleOrDefaultAsync(item => item.LeaseToken == token, cancellationToken); if (prior is not null) return prior;
            var expiry = now.AddSeconds(90);
            var ids = await db.Database.SqlQuery<Guid>($"""
                WITH candidate AS (
                  SELECT id FROM file_archive_operations WHERE completed_at_utc IS NULL AND next_attempt_at_utc<={now}
                    AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
                  ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
                ) UPDATE file_archive_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
                  FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
                """).ToArrayAsync(cancellationToken);
            return ids.Length == 0 ? null : await db.FileArchiveOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
        }, cancellationToken);
    }
    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => db.OperationJobs.AsNoTracking().AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);
    public Task<FileUploadStartResult> StartedAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lock(operation, cancellationToken); if (row is null) return FileUploadStartResult.LeaseLost;
        var job = await Job(row.Id, cancellationToken); var now = DateTimeOffset.UtcNow;
        if (row.LeaseExpiresAtUtc <= now || !FileArchiveValidation.HasValidBinding(row, protector)) return FileUploadStartResult.LeaseLost;
        if (job.CancelRequestedAtUtc is not null) return FileUploadStartResult.Canceled;
        if (row.ExpiresAtUtc <= now) return FileUploadStartResult.Expired;
        if (row.RootStartedAtUtc is not null) return FileUploadStartResult.LeaseLost;
        row.RootStartedAtUtc = now; row.State = "Running"; row.Version++;
        job.State = "Running"; job.Phase = "ApplyingFileArchive"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "FILES_ARCHIVE_APPLYING", cancellationToken); await db.SaveChangesAsync(cancellationToken); return FileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> RenewAsync(FileArchiveOperation operation, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lock(operation, cancellationToken);
        if (row is null || !FileArchiveValidation.HasValidBinding(row, protector) || row.RequestHash != operation.RequestHash) return false;
        row.LeaseExpiresAtUtc = now.AddSeconds(90); row.Version++; await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishAsync(FileArchiveOperation operation, string state, string code, FileArchiveReceipt? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Superseded" or "Unknown" or "Canceled" or "Failed")) throw new ArgumentException("FILES_RESULT_INVALID");
        var row = await Lock(operation, cancellationToken); if (row is null) return false;
        var valid = FileArchiveValidation.HasValidBinding(row, protector);
        if (!valid && (state != "Unknown" || receipt is not null)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (receipt is not null) FileArchiveValidation.Receipt(row.Id, FileArchiveValidation.ReadPlan(row), receipt);
        if (state is "Succeeded" or "Superseded" && (receipt?.State != "Completed" || state == "Succeeded" && !receipt.BindingCurrent)) throw new InvalidOperationException("FILES_RECEIPT_REQUIRED");
        if (state is "Canceled" or "Failed" && receipt?.State != "Aborted") throw new InvalidOperationException("FILES_ABORT_RECEIPT_REQUIRED");
        var job = await Job(row.Id, cancellationToken); var now = DateTimeOffset.UtcNow; if (row.LeaseExpiresAtUtc <= now) return false;
        var changed = row.State != state; var terminal = state != "Unknown";
        row.State = state; row.ResultCode = code; row.CompletedAtUtc = terminal ? now : null; row.PublishedVersion = receipt?.State == "Completed" ? receipt.Version : null;
        row.SourceRetained = receipt?.SourceRetained == true; row.NextAttemptAtUtc = now.AddSeconds(30); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (valid && receipt is not null) await Capture(row, receipt, now, cancellationToken);
        if (changed || terminal)
        {
            job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
            job.Phase = terminal ? state : "AwaitingFileReceipt"; job.ErrorCode = state == "Succeeded" ? null : code; job.CompletedAtUtc = terminal ? now : null;
            job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
            await Event(job, code, cancellationToken); Audit(row.ActorSubject, "files.archive.result", row, row.Id.ToString("D"), null, state);
        }
        if (terminal) await Complete(row.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task Capture(FileArchiveOperation row, FileArchiveReceipt receipt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (receipt.HddBackup is { } proof)
        {
            var kind = row.Kind == "trash" ? "Trash" : "Overwrite";
            await Observe(new(row.Id, row.RootId, row.RelativePath, proof.SourceVersion, proof.SizeBytes, proof.Sha256, DateTimeOffset.FromUnixTimeMilliseconds(proof.CreatedAtUnixMs), kind,
                kind == "Overwrite" ? "Available" : receipt.State == "Completed" ? "Trashed" : receipt.State == "Aborted" ? "Canceled" : "Pending", "Present", proof.DifferentSourceDevice), row.Id, row.ActorSubject, cancellationToken);
        }
        if (receipt.State != "Completed" || row.ArchiveVersionId is null) return;
        var selected = await db.FileVersions.SingleAsync(item => item.Id == row.ArchiveVersionId, cancellationToken);
        if (row.Kind == "purge") { selected.CaptureState = "Purged"; selected.Availability = "Purged"; }
        else if (selected.CaptureKind == "Trash" && selected.CaptureState is "Pending" or "Trashed") selected.CaptureState = "Restored";
        selected.LastOperationId = row.Id; selected.LastObservedAtUtc = now; selected.Version++;
    }
    public Task DeferAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Lock(operation, cancellationToken); if (row is null) return false;
        row.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(6); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileArchiveReceipt receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.FileArchiveOperations.FromSqlInterpolated($"SELECT * FROM file_archive_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
        if (row.State != "Unknown" || row.Version != version || row.CompletedAtUtc is not null || row.LeaseExpiresAtUtc > DateTimeOffset.UtcNow || !FileArchiveValidation.HasValidBinding(row, protector))
            throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        FileArchiveValidation.Receipt(row.Id, FileArchiveValidation.ReadPlan(row), receipt);
        var now = DateTimeOffset.UtcNow; row.State = "Acknowledged"; row.CompletedAtUtc = now; row.Version++; row.ResultCode = "FILES_UNKNOWN_ACKNOWLEDGED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var job = await Job(id, cancellationToken); job.State = "Failed"; job.Phase = "Acknowledged"; job.ErrorCode = row.ResultCode; job.CompletedAtUtc = now; job.Version++;
        await Event(job, row.ResultCode, cancellationToken); Audit(actor.Subject, "files.archive.unknown-acknowledged", row, actor.TraceId, actor.SourceIp, receipt.State);
        await Complete(id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task<FileArchiveOperation?> Lock(FileArchiveOperation row, CancellationToken cancellationToken)
    {
        var current = await db.FileArchiveOperations.FromSqlInterpolated($"SELECT * FROM file_archive_operations WHERE id={row.Id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        return row.LeaseToken is not null && current?.LeaseToken == row.LeaseToken && current.CompletedAtUtc is null && current.LeaseExpiresAtUtc > DateTimeOffset.UtcNow ? current : null;
    }
    private Task<OperationJob> Job(Guid id, CancellationToken cancellationToken) => db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private async Task Complete(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await FilePathLocks.Release(db, id, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('FileArchiveRequested','FileArchiveCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    }
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(string actor, string action, FileArchiveOperation row, string trace, string? source, string result) => db.AuditEvents.Add(new() {
        ActorSubject = actor, Action = action, TargetType = "file-root", TargetId = row.RootId, JobId = row.Id, TraceId = trace, SourceIp = source, Result = result,
        DetailJson = JsonSerializer.Serialize(new { row.Kind, row.TargetHash, row.ArchiveVersionId }) });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
