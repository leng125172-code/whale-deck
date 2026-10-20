using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class FileTreePurgingRepository
{
    internal void DisposeRecovery(FileTreePurgingOperation row, FileTreePurgingReceipt receipt, DateTimeOffset now, bool complete)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("FILES_RECOVERY_TRANSACTION_REQUIRED");
        if (row.State != "Unknown" || row.CompletedAtUtc is not null || !FileTreePurgingValidation.HasValidBinding(row, protector))
            throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (complete != (receipt.State == "Completed") || !complete && receipt.State is not ("Unknown" or "Aborted"))
            throw new InvalidOperationException("FILES_RECOVERY_RECEIPT_CHANGED");
        StoreReceipt(row, receipt, FileTreePurgingValidation.Timestamp(now));
        row.State = complete ? "Succeeded" : "Acknowledged"; row.CompletedAtUtc = FileTreePurgingValidation.Timestamp(now);
        row.ResultCode = "FILES_ADMINISTRATOR_RECOVERY_DISPOSED"; row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (!FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
    }

    public Task<bool> FinishAsync(FileTreePurgingOperation claimed, string state, string code, FileTreePurgingReceipt? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Unknown" or "Canceled" or "Failed")) throw new ArgumentException("FILES_RESULT_INVALID");
        var row = await Lease(claimed, cancellationToken); if (row?.ExecutionJobId is not { } id) return false;
        var valid = FileTreePurgingValidation.HasValidBinding(row, protector);
        // Quarantine altered material without signing it or releasing its locks.
        if (!valid && (state != "Unknown" || receipt is not null)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (receipt is not null) FileTreePurgingValidation.Receipt(id, FileTreePurgingValidation.ReadPlan(row), receipt);
        if (state == "Succeeded" && receipt?.State != "Completed" || state is "Canceled" or "Failed" && receipt?.State != "Aborted" ||
            state == "Unknown" && receipt is not null && receipt.State != "Unknown") throw new InvalidOperationException("FILES_RECEIPT_REQUIRED");
        var job = await Job(id, cancellationToken); var now = Now(); if (row.LeaseExpiresAtUtc <= now) return false;
        var terminal = state != "Unknown"; var changed = row.State != state || receipt is not null && row.ReceiptJson is null;
        if (receipt is not null) StoreReceipt(row, receipt, now);
        row.State = state; row.ResultCode = code; row.CompletedAtUtc = terminal ? now : null;
        row.NextAttemptAtUtc = now.AddSeconds(30); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (valid && !FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        if (changed || terminal)
        {
            if (terminal) await FinishJob(job, state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : "Failed", state, code, cancellationToken);
            else { job.State = "Running"; job.Phase = "AwaitingTreeReceipt"; job.ErrorCode = code; job.Version++; await Event(job, code, cancellationToken); }
            Audit(row, id, "files.tree-purge.result", state, row.Id.ToString("D"), null);
        }
        if (terminal) { await FilePathLocks.Release(db, id, cancellationToken); await Processed(id, now, cancellationToken); }
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePurgingReceipt receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); RequireOwner(row, actor);
        if (row.State != "Unknown" || row.Version != version || row.CompletedAtUtc is not null || row.LeaseExpiresAtUtc > Now() || row.ExecutionJobId is not { } execution ||
            !FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_ACKNOWLEDGMENT_CONFLICT");
        FileTreePurgingValidation.Receipt(execution, FileTreePurgingValidation.ReadPlan(row), receipt);
        if (receipt.State == "Completed") throw new InvalidOperationException("FILES_RECEIPT_AVAILABLE");
        var job = await Job(execution, cancellationToken); var now = Now();
        StoreReceipt(row, receipt, now);
        row.State = "Acknowledged"; row.CompletedAtUtc = now; row.ResultCode = "FILES_UNKNOWN_ACKNOWLEDGED";
        row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (!FileTreePurgingValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("FILES_INTENT_BINDING_CHANGED");
        await FinishJob(job, "Failed", "Acknowledged", row.ResultCode, cancellationToken);
        Audit(row, execution, "files.tree-purge.unknown-acknowledged", receipt.State, actor.TraceId, actor.SourceIp);
        await FilePathLocks.Release(db, execution, cancellationToken); await Processed(execution, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);

    private DateTimeOffset Now() => FileTreePurgingValidation.Timestamp(clock.GetUtcNow());
    private static void RequireOwner(FileTreePurgingOperation row, TerminalActor actor)
    { if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND"); }
    private async Task<FileTreePurgingOperation> Row(Guid id, CancellationToken cancellationToken) => await db.FileTreePurgingOperations
        .FromSqlInterpolated($"SELECT * FROM file_tree_purging_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("FILES_OPERATION_NOT_FOUND");
    private async Task<FileTreePurgingOperation?> Lease(FileTreePurgingOperation claimed, CancellationToken cancellationToken)
    {
        var row = await Row(claimed.Id, cancellationToken);
        return claimed.LeaseToken is not null && row.LeaseToken == claimed.LeaseToken && row.LeaseExpiresAtUtc > Now() && row.CompletedAtUtc is null ? row : null;
    }
    private Task<OperationJob> Job(Guid id, CancellationToken cancellationToken) => db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private void Message(Guid id) => db.OutboxMessages.Add(new() { MessageType = "FileTreePurgingRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
    private Task<int> Processed(Guid id, DateTimeOffset now, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('FileTreePurgingRequested','FileTreePurgingCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    private async Task FinishJob(OperationJob job, string state, string phase, string code, CancellationToken cancellationToken)
    {
        job.State = state; job.Phase = phase; job.ErrorCode = state == "Succeeded" ? null : code; job.CompletedAtUtc = Now();
        job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++; await Event(job, code, cancellationToken);
    }
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(item => item.JobId == job.Id).MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(FileTreePurgingOperation row, Guid job, string action, string result, string trace, string? source)
    {
        db.AuditEvents.Add(new() { ActorSubject = row.ActorSubject, Action = action, TargetType = "file-tree", TargetId = row.Id.ToString("D"),
            JobId = job, TraceId = trace, SourceIp = source, Result = result,
            DetailJson = JsonSerializer.Serialize(new { intentId = row.Id, captureId = row.CaptureId, sourceHash = row.SourceHash,
                row.DeletionStarted, row.ArchiveRemoved, row.OriginalsRemoved }) });
    }
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
