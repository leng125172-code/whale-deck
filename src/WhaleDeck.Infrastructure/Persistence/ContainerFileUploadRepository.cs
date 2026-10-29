using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>All write reservations and publication barriers use the host file transaction gate.</summary>
public sealed class ContainerFileUploadRepository(PlatformDbContext db, IContainerFileUploadProtector plans) : IContainerFileUploadRepository
{
    public Task<ContainerFileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) =>
        db.ContainerFileUploadOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.ActorSubject == actor.Subject && item.LoginBinding == actor.LoginBinding, cancellationToken);

    public Task<ContainerFileUploadOperation> EnqueueAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Validate(row);
        if (row.State != "AwaitingUpload" || row.CompletedAtUtc is not null || row.RootStartedAtUtc is not null || row.PublishBarrier ||
            row.ReceiveToken is not null || row.LeaseToken is not null || row.LeaseExpiresAtUtc is not null || row.Version != 0 || row.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new ArgumentException("CONTAINER_UPLOAD_INTENT_INVALID");
        var prior = await db.ContainerFileUploadOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == row.Id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            // Concurrent HTTP retries can have a new trace and microsecond
            // creation time. Reuse the winner's sealed context/lifetime rather
            // than rejecting an identical plan or extending its validity.
            var original = ContainerFileUploadIntentValidation.ReadBinding(prior);
            var requested = ContainerFileUploadIntentValidation.ReadBinding(row);
            if (original.Id != requested.Id || original.IdempotencyKey != requested.IdempotencyKey || original.Actor != requested.Actor ||
                original.LoginBinding != requested.LoginBinding || original.PlanHash != requested.PlanHash || original.Target != requested.Target ||
                original.SizeBytes != requested.SizeBytes || original.Sha256 != requested.Sha256 || original.HistorySourceJson != requested.HistorySourceJson || original.RestoreAttributes != requested.RestoreAttributes)
                throw new InvalidOperationException("CONTAINER_UPLOAD_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        var key = row.IdempotencyKey.ToString("D");
        if (await db.OperationJobs.AnyAsync(item => item.ActorSubject == row.ActorSubject && item.IdempotencyKey == key, cancellationToken))
            throw new InvalidOperationException("CONTAINER_UPLOAD_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.EnsureBudget(db, row.ActorSubject, null, cancellationToken);
        // Same container/path hierarchy is reserved from acceptance. Different
        // containers can alias one bind: the global publish barrier handles that.
        var held = await db.ContainerFileUploadOperations.AsNoTracking().Where(item => item.ContainerId == row.ContainerId && item.CompletedAtUtc == null).Take(65).ToArrayAsync(cancellationToken);
        var directories = await db.ContainerDirectoryOperations.Where(item => item.ContainerId == row.ContainerId && item.CompletedAtUtc == null).Select(item => item.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var copies = await db.ContainerCopyIntents.Where(item => item.ContainerId == row.ContainerId && item.CompletedAtUtc == null).Select(item => new { item.SourcePath, item.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        var moves = await db.ContainerMoveIntents.Where(item => item.ContainerId == row.ContainerId && item.CompletedAtUtc == null).Select(item => new { item.SourcePath, item.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        if (held.Length + directories.Length + copies.Length + moves.Length > 64 || held.Any(item => Conflicts(item.RelativePath, row.RelativePath)) || directories.Any(path => Conflicts(path, row.RelativePath)) ||
            copies.Any(item => Conflicts(item.SourcePath, row.RelativePath) || Conflicts(item.TargetPath, row.RelativePath)) ||
            moves.Any(item => Conflicts(item.SourcePath, row.RelativePath) || Conflicts(item.TargetPath, row.RelativePath)))
            throw new InvalidOperationException("CONTAINER_UPLOAD_TARGET_BUSY");
        var resource = await db.ManagedResources.Where(item => item.ResourceType == "Container" && item.ExternalId == row.ContainerId)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
        db.OperationJobs.Add(new() { Id = row.Id, JobType = row.HistorySourceJson is null ? "containers.files.upload" : "containers.files.restore", ActorSubject = row.ActorSubject, ResourceId = resource,
            IdempotencyKey = key, Phase = row.HistorySourceJson is null ? "AwaitingContainerFileUpload" : "AwaitingContainerFileRestore", RequestJson = JsonSerializer.Serialize(new {
                containerId = row.ContainerId, pathHash = FileManagementService.Hash(row.RelativePath),
                instanceHash = FileManagementService.Hash(row.InstanceId), sizeBytes = row.SizeBytes, sha256 = row.Sha256, restoreAttributes = row.RestoreAttributes }) });
        db.ContainerFileUploadOperations.Add(row);
        db.OutboxMessages.Add(new() { MessageType = "ContainerFileUploadRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = row.Id }) });
        var job = db.OperationJobs.Local.Single(item => item.Id == row.Id);
        await Event(job, "CONTAINER_UPLOAD_ACCEPTED", cancellationToken);
        Audit(row, "accepted", "Accepted");
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);

    public Task<ContainerFileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.ContainerFileUploadOperations.FromSqlInterpolated($"SELECT * FROM container_file_upload_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("CONTAINER_UPLOAD_NOT_FOUND");
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("CONTAINER_UPLOAD_NOT_FOUND");
        Validate(row);
        if (row.HistorySourceJson is not null) throw new InvalidOperationException("CONTAINER_UPLOAD_HTTP_SOURCE_FORBIDDEN");
        var job = await db.OperationJobs.SingleAsync(item => item.Id == id, cancellationToken);
        if (row.State != "AwaitingUpload" || row.CompletedAtUtc is not null || row.ExpiresAtUtc <= now || job.CancelRequestedAtUtc is not null)
            throw new InvalidOperationException("CONTAINER_UPLOAD_CANNOT_RECEIVE");
        row.State = "Receiving"; row.ReceiveToken = Guid.NewGuid(); row.NextAttemptAtUtc = now.AddSeconds(15); row.Version++;
        job.State = "Running"; job.Phase = "ReceivingContainerFile"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "CONTAINER_UPLOAD_RECEIVING", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);

    public Task<bool> BeginRestoreReceiveAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken);
        if (current is null || current.HistorySourceJson is null || current.State != "AwaitingUpload" ||
            current.ExpiresAtUtc <= now || await CancellationRequestedAsync(row.Id, cancellationToken)) return false;
        Validate(current);
        var token = Guid.NewGuid();
        await db.ContainerFileUploadOperations.Where(item => item.Id == row.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.State, "Receiving").SetProperty(item => item.ReceiveToken, token)
            .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = await db.OperationJobs.SingleAsync(item => item.Id == row.Id, cancellationToken);
        job.State = "Running"; job.Phase = "ReadingContainerHistory"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "CONTAINER_UPLOAD_RECEIVING", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        row.State = "Receiving"; row.ReceiveToken = token; return true;
    }, cancellationToken);

    public Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => db.ContainerFileUploadOperations.AsNoTracking()
        .AnyAsync(item => item.Id == id && item.ReceiveToken == token && item.State == "Receiving" && item.CompletedAtUtc == null && item.ExpiresAtUtc > now &&
            !db.OperationJobs.Any(job => job.Id == id && job.CancelRequestedAtUtc != null), cancellationToken);

    public async Task<bool> ReadyAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) => await db.ContainerFileUploadOperations
        .Where(item => item.Id == row.Id && item.State == "Receiving" && item.ReceiveToken == row.ReceiveToken && item.CompletedAtUtc == null && item.RequestHash == row.RequestHash &&
            item.ExpiresAtUtc > DateTimeOffset.UtcNow && (row.HistorySourceJson == null || item.LeaseToken == row.LeaseToken && item.LeaseExpiresAtUtc > DateTimeOffset.UtcNow) &&
            !db.OperationJobs.Any(job => job.Id == row.Id && job.CancelRequestedAtUtc != null))
        .ExecuteUpdateAsync(update => update.SetProperty(item => item.State, "Ready").SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow)
            .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken) == 1;

    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_UPLOAD_NOT_FOUND");
        if (row.CompletedAtUtc is not null) return true;
        var job = await db.OperationJobs.SingleAsync(item => item.Id == id, cancellationToken);
        if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = DateTimeOffset.UtcNow; job.Version++;
            db.OutboxMessages.Add(new() { MessageType = "ContainerFileUploadCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
            Audit(row, "cancel-requested", "Accepted");
        }
        await db.ContainerFileUploadOperations.Where(item => item.Id == id).ExecuteUpdateAsync(update =>
            update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    public Task<ContainerFileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var token = Guid.NewGuid(); var expiry = now.AddSeconds(90);
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
              SELECT id FROM container_file_upload_operations
              WHERE completed_at_utc IS NULL AND state<>'Unknown' AND next_attempt_at_utc<={now}
                AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
              ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
            ) UPDATE container_file_upload_operations f SET lease_token={token},lease_expires_at_utc={expiry},version=f.version+1
              FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
            """).ToArrayAsync(cancellationToken);
        return ids.Length == 0 ? null : await db.ContainerFileUploadOperations.AsNoTracking().SingleAsync(item => item.Id == ids[0], cancellationToken);
    }, cancellationToken);

    public Task<bool> RenewAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken); if (current is null || current.State == "Unknown") return false;
        Validate(current);
        return await db.ContainerFileUploadOperations.Where(item => item.Id == row.Id).ExecuteUpdateAsync(update =>
            update.SetProperty(item => item.LeaseExpiresAtUtc, now.AddSeconds(90)).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken) == 1;
    }, cancellationToken);

    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) =>
        db.OperationJobs.AnyAsync(item => item.Id == id && item.CancelRequestedAtUtc != null, cancellationToken);

    public Task<ContainerFileUploadStartResult> StartedAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken);
        if (current is null || current.State != "Ready" || current.RootStartedAtUtc is not null) return ContainerFileUploadStartResult.LeaseLost;
        Validate(current);
        if (await CancellationRequestedAsync(row.Id, cancellationToken)) return ContainerFileUploadStartResult.Canceled;
        if (current.ExpiresAtUtc <= now) return ContainerFileUploadStartResult.Expired;
        if (await db.FilePathLocks.AnyAsync(cancellationToken) ||
            await db.ContainerFileUploadOperations.AnyAsync(item => item.PublishBarrier && item.Id != row.Id, cancellationToken) ||
            await db.ContainerDirectoryOperations.AnyAsync(item => item.PublishBarrier, cancellationToken) ||
            await db.ContainerCopyIntents.AnyAsync(item => item.PublishBarrier, cancellationToken) ||
            await db.ContainerMoveIntents.AnyAsync(item => item.PublishBarrier, cancellationToken)) return ContainerFileUploadStartResult.Busy;
        var started = ContainerFileUploadIntentValidation.CanonicalTime(now);
        await db.ContainerFileUploadOperations.Where(item => item.Id == row.Id).ExecuteUpdateAsync(update =>
            update.SetProperty(item => item.State, "Publishing").SetProperty(item => item.RootStartedAtUtc, started)
                .SetProperty(item => item.PublishBarrier, true).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = await db.OperationJobs.SingleAsync(item => item.Id == row.Id, cancellationToken);
        job.State = "Running"; job.Phase = "PublishingContainerFile"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "CONTAINER_UPLOAD_PUBLISHING", cancellationToken); await db.SaveChangesAsync(cancellationToken);
        row.State = "Publishing"; row.RootStartedAtUtc = started; row.PublishBarrier = true;
        return ContainerFileUploadStartResult.Started;
    }, cancellationToken);

    public Task<bool> FinishAsync(ContainerFileUploadOperation row, string state, string code, ContainerFileUploadReceiptDto? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken); if (current is null) return false;
        if (state is not ("Unknown" or "Succeeded" or "Superseded" or "Canceled" or "Failed") ||
            code.Length is < 1 or > 64 || !code.StartsWith("CONTAINER_UPLOAD_", StringComparison.Ordinal) || code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')))
            throw new ArgumentException("CONTAINER_UPLOAD_RESULT_INVALID");
        if (state != "Unknown")
        {
            Validate(current);
            if (receipt is null || receipt.OperationId != current.Id || receipt.ContainerId != current.ContainerId || receipt.InstanceId != current.InstanceId || receipt.TargetProof != current.TargetProof)
                throw new ArgumentException("CONTAINER_UPLOAD_RESULT_PROOF_REQUIRED");
            if (state is "Succeeded" or "Superseded")
            {
                if (current.RootStartedAtUtc is null || !current.PublishBarrier || receipt.Upload.State != "Published" ||
                    receipt.Upload.SizeBytes != current.SizeBytes || receipt.Upload.Sha256 != current.Sha256 || !FileManagementService.Hex(receipt.Upload.Version) ||
                    receipt.Upload.BindingCurrent != (state == "Succeeded")) throw new ArgumentException("CONTAINER_UPLOAD_PUBLICATION_PROOF_REQUIRED");
                ValidateBackup(current, receipt.Upload);
            }
            else if (receipt.Upload.State is not ("Prevented" or "Discarded" or "PublishDiscarded"))
                throw new ArgumentException("CONTAINER_UPLOAD_CANCELLATION_PROOF_REQUIRED");
        }
        var terminal = state != "Unknown"; var now = DateTimeOffset.UtcNow;
        var publishedVersion = state is "Succeeded" or "Superseded" ? receipt!.Upload.Version : null;
        var previousRetained = state is "Succeeded" or "Superseded" && receipt!.Upload.PreviousRetained;
        await db.ContainerFileUploadOperations.Where(item => item.Id == row.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.State, state).SetProperty(item => item.ResultCode, code).SetProperty(item => item.CompletedAtUtc, terminal ? now : null)
            .SetProperty(item => item.PublishBarrier, !terminal && current.PublishBarrier).SetProperty(item => item.LeaseToken, (Guid?)null)
            .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null).SetProperty(item => item.PublishedVersion, publishedVersion)
            .SetProperty(item => item.PreviousRetained, previousRetained)
            .SetProperty(item => item.NextAttemptAtUtc, now.AddSeconds(30)).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        var job = await db.OperationJobs.SingleAsync(item => item.Id == row.Id, cancellationToken);
        job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
        job.Phase = state; job.CompletedAtUtc = terminal ? now : null; job.ErrorCode = state == "Succeeded" ? null : code;
        job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
        await Event(job, code, cancellationToken); Audit(current, "result", state);
        if (terminal) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('ContainerFileUploadRequested','ContainerFileUploadCancellationRequested') AND payload_json->>'jobId'={row.Id.ToString("D")}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    public Task DeferAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken); if (current is null) return false;
        await db.ContainerFileUploadOperations.Where(item => item.Id == row.Id).ExecuteUpdateAsync(update =>
            update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddSeconds(6)).SetProperty(item => item.LeaseToken, (Guid?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTimeOffset?)null).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
        return true;
    }, cancellationToken);

    private void Validate(ContainerFileUploadOperation row)
    {
        var binding = ContainerFileUploadIntentValidation.ReadBinding(row);
        if (!FileManagementService.Hex(row.RequestHash) || row.RequestHash != plans.FingerprintContainerUpload(binding))
            throw new InvalidOperationException("CONTAINER_UPLOAD_INTENT_BINDING_CHANGED");
    }
    private static void ValidateBackup(ContainerFileUploadOperation row, FileUploadReceiptDto receipt)
    {
        if (row.ExpectedVersion == "ABSENT")
        { if (receipt.HddBackup is not null || receipt.PreviousRetained) throw new ArgumentException("CONTAINER_UPLOAD_BACKUP_INVALID"); return; }
        var proof = receipt.HddBackup;
        if (!receipt.PreviousRetained || proof is null || proof.OperationId != row.Id.ToString("N") || proof.StorageId != "database-platform.backup.hdd" ||
            proof.SourceVersion != row.ExpectedVersion || proof.SizeBytes != row.PreviousSizeBytes || !FileManagementService.Hex(proof.Sha256) ||
            proof.CreatedAtUnixMs < row.CreatedAtUtc.AddSeconds(-5).ToUnixTimeMilliseconds() || proof.CreatedAtUnixMs > DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds())
            throw new ArgumentException("CONTAINER_UPLOAD_BACKUP_INVALID");
    }
    private static bool Conflicts(string first, string second) => first == second || first.StartsWith(second + "/", StringComparison.Ordinal) || second.StartsWith(first + "/", StringComparison.Ordinal);
    private async Task<ContainerFileUploadOperation?> Lock(ContainerFileUploadOperation row, CancellationToken cancellationToken)
    {
        var current = await db.ContainerFileUploadOperations.FromSqlInterpolated($"SELECT * FROM container_file_upload_operations WHERE id={row.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row.LeaseToken is not null && current?.LeaseToken == row.LeaseToken && current.RequestHash == row.RequestHash &&
            current.LeaseExpiresAtUtc > DateTimeOffset.UtcNow && current.CompletedAtUtc is null ? current : null;
    }
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(ContainerFileUploadOperation row, string action, string result) => db.AuditEvents.Add(new() {
        ActorSubject = row.ActorSubject, Action = (row.HistorySourceJson is null ? "containers.files.upload." : "containers.files.restore.") + action, TargetType = "container", TargetId = row.ContainerId,
        JobId = row.Id, TraceId = row.TraceId, Result = result, DetailJson = JsonSerializer.Serialize(new {
            pathHash = FileManagementService.Hash(row.RelativePath), instanceHash = FileManagementService.Hash(row.InstanceId), restoreAttributes = row.RestoreAttributes }) });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
