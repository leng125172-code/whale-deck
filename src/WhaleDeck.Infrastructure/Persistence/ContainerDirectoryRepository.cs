using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>One transaction gate for all host paths and container publication barriers.</summary>
public sealed class ContainerDirectoryRepository(PlatformDbContext db, IContainerDirectoryProtector protector) : IContainerDirectoryRepository
{
    public Task<ContainerDirectoryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => db.ContainerDirectoryOperations.AsNoTracking()
        .SingleOrDefaultAsync(r => r.Id == id && r.ActorSubject == actor.Subject && r.LoginBinding == actor.LoginBinding, cancellationToken);
    public Task<ContainerDirectoryOperation?> FindAsync(Guid id, CancellationToken cancellationToken) => db.ContainerDirectoryOperations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    public async Task<IReadOnlyList<ContainerDirectoryOperation>> UnknownAsync(int take, CancellationToken cancellationToken) => await db.ContainerDirectoryOperations.AsNoTracking()
        .Where(r => r.State == "Unknown" && r.CompletedAtUtc == null).OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).Take(take).ToArrayAsync(cancellationToken);
    public Task<ContainerDirectoryOperation?> ResolveAsync(TerminalActor actor, Guid id, DateTimeOffset planExpiry, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken);
        if (row is null && planExpiry > DateTimeOffset.UtcNow) throw new InvalidOperationException("CONTAINER_DIRECTORY_REGISTRATION_STILL_POSSIBLE");
        return row;
    }, cancellationToken);
    public Task<ContainerDirectoryOperation> EnqueueAsync(ContainerDirectoryOperation row, DateTimeOffset planExpiry, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Validate(row);
        var prior = await FindAsync(row.Id, cancellationToken);
        if (prior is not null)
        {
            Validate(prior);
            if (prior.IdempotencyKey != row.IdempotencyKey || prior.ActorSubject != row.ActorSubject || prior.LoginBinding != row.LoginBinding ||
                prior.PlanHash != row.PlanHash || prior.PlanJson != row.PlanJson) throw new InvalidOperationException("CONTAINER_DIRECTORY_IDEMPOTENCY_CONFLICT");
            return prior;
        }
        var now = DateTimeOffset.UtcNow;
        if (planExpiry <= now || row.ExpiresAtUtc <= now) throw new InvalidOperationException("CONTAINER_DIRECTORY_PLAN_EXPIRED");
        if (row.State != "Pending" || row.CompletedAtUtc is not null || row.RootStartedAtUtc is not null || row.PublishBarrier || row.LeaseToken is not null || row.LeaseExpiresAtUtc is not null || row.Version != 0 ||
            row.AcknowledgedBy is not null || row.AcknowledgementJson is not null) throw new ArgumentException("CONTAINER_DIRECTORY_INTENT_INVALID");
        var key = row.IdempotencyKey.ToString("D");
        if (await db.OperationJobs.AnyAsync(r => r.ActorSubject == row.ActorSubject && r.IdempotencyKey == key, cancellationToken)) throw new InvalidOperationException("CONTAINER_DIRECTORY_IDEMPOTENCY_CONFLICT");
        await FilePathLocks.EnsureBudget(db, row.ActorSubject, null, cancellationToken);
        var directories = await db.ContainerDirectoryOperations.Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var uploads = await db.ContainerFileUploadOperations.Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var copies = await db.ContainerCopyIntents.Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        var moves = await db.ContainerMoveIntents.Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        if (directories.Length + uploads.Length + copies.Length + moves.Length > 64 || directories.Concat(uploads).Any(p => Conflicts(p, row.RelativePath)) ||
            copies.Any(r => Conflicts(r.SourcePath, row.RelativePath) || Conflicts(r.TargetPath, row.RelativePath)) ||
            moves.Any(r => Conflicts(r.SourcePath, row.RelativePath) || Conflicts(r.TargetPath, row.RelativePath))) throw new InvalidOperationException("CONTAINER_DIRECTORY_TARGET_BUSY");
        db.ContainerDirectoryOperations.Add(row);
        var job = new OperationJob { Id = row.Id, JobType = "containers.files.mkdir", ActorSubject = row.ActorSubject, IdempotencyKey = key,
            Phase = "AwaitingContainerDirectory", RequestJson = JsonSerializer.Serialize(new { containerId = row.ContainerId, pathHash = FileManagementService.Hash(row.RelativePath),
                instanceHash = FileManagementService.Hash(row.InstanceId), mode = ContainerDirectoryWorkflowValidation.ReadPlan(row).Command.Mode }) };
        db.OperationJobs.Add(job); db.OutboxMessages.Add(new() { MessageType = "ContainerDirectoryRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = row.Id }) });
        await Event(job, "CONTAINER_DIRECTORY_ACCEPTED", cancellationToken); Audit(row.ActorSubject, row.TraceId, row, "accepted", "Accepted");
        await db.SaveChangesAsync(cancellationToken); return row;
    }, cancellationToken);
    public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await OwnedAsync(actor, id, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND"); Validate(row);
        if (row.CompletedAtUtc is not null) return true;
        var job = await Job(id, cancellationToken);
        if (job.CancelRequestedAtUtc is null)
        {
            job.CancelRequestedAtUtc = DateTimeOffset.UtcNow; job.Version++;
            db.OutboxMessages.Add(new() { MessageType = "ContainerDirectoryCancellationRequested", PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
            Audit(actor.Subject, actor.TraceId, row, "cancel-requested", "Accepted");
        }
        await db.ContainerDirectoryOperations.Where(r => r.Id == id).ExecuteUpdateAsync(u => u.SetProperty(r => r.NextAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<ContainerDirectoryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var lease = Guid.NewGuid(); var expiry = now.AddSeconds(90);
        var ids = await db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
              SELECT id FROM container_directory_operations WHERE completed_at_utc IS NULL AND state<>'Unknown'
                AND next_attempt_at_utc<={now} AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc<={now})
              ORDER BY next_attempt_at_utc,id LIMIT 1 FOR UPDATE SKIP LOCKED
            ) UPDATE container_directory_operations f SET lease_token={lease},lease_expires_at_utc={expiry},version=f.version+1
              FROM candidate WHERE f.id=candidate.id RETURNING f.id AS "Value"
            """).ToArrayAsync(cancellationToken);
        return ids.Length == 0 ? null : await FindAsync(ids[0], cancellationToken);
    }, cancellationToken);
    public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => db.OperationJobs.AnyAsync(j => j.Id == id && j.CancelRequestedAtUtc != null, cancellationToken);
    public Task<ContainerFileUploadStartResult> StartedAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken);
        if (current is null || current.State != "Pending" || current.RootStartedAtUtc is not null) return ContainerFileUploadStartResult.LeaseLost;
        Validate(current);
        if (await CancellationRequestedAsync(row.Id, cancellationToken)) return ContainerFileUploadStartResult.Canceled;
        if (current.ExpiresAtUtc <= now) return ContainerFileUploadStartResult.Expired;
        if (await db.FilePathLocks.AnyAsync(cancellationToken) || await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier && r.Id != row.Id, cancellationToken) ||
            await db.ContainerCopyIntents.AnyAsync(r => r.PublishBarrier, cancellationToken) ||
            await db.ContainerMoveIntents.AnyAsync(r => r.PublishBarrier, cancellationToken)) return ContainerFileUploadStartResult.Busy;
        current.State = "Applying"; current.RootStartedAtUtc = ContainerDirectoryWorkflowValidation.Time(now); current.PublishBarrier = true; current.Version++;
        var job = await Job(row.Id, cancellationToken); job.State = "Running"; job.Phase = "CreatingContainerDirectory"; job.StartedAtUtc ??= now; job.Version++;
        await Event(job, "CONTAINER_DIRECTORY_APPLYING", cancellationToken); await db.SaveChangesAsync(cancellationToken);
        row.State = current.State; row.RootStartedAtUtc = current.RootStartedAtUtc; row.PublishBarrier = true;
        return ContainerFileUploadStartResult.Started;
    }, cancellationToken);
    public Task<bool> RenewAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken); if (current is null || current.State == "Unknown") return false;
        Validate(current); current.LeaseExpiresAtUtc = now.AddSeconds(90); current.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task<bool> FinishAsync(ContainerDirectoryOperation row, string state, string code, ContainerDirectoryReceiptDto? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        if (state is not ("Succeeded" or "Superseded" or "Canceled" or "Failed" or "Unknown") || code.Length is < 1 or > 64 ||
            !code.StartsWith("CONTAINER_DIRECTORY_", StringComparison.Ordinal) || code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_'))) throw new ArgumentException("CONTAINER_DIRECTORY_RESULT_INVALID");
        var current = await Lock(row, cancellationToken); if (current is null) return false;
        if (state != "Unknown") Validate(current);
        if (receipt is not null) ContainerDirectoryValidation.Receipt(ContainerDirectoryWorkflowValidation.Reference(current), receipt);
        if (state is "Succeeded" or "Superseded" && (receipt?.State != "Completed" || receipt.BindingCurrent != (state == "Succeeded") || current.RootStartedAtUtc is null || !current.PublishBarrier))
            throw new ArgumentException("CONTAINER_DIRECTORY_PUBLICATION_PROOF_REQUIRED");
        if (state is "Canceled" or "Failed" && receipt?.State != "Aborted") throw new ArgumentException("CONTAINER_DIRECTORY_CANCELLATION_PROOF_REQUIRED");
        var terminal = state != "Unknown"; var now = DateTimeOffset.UtcNow;
        current.State = state; current.ResultCode = code; current.CompletedAtUtc = terminal ? now : null;
        current.PublishBarrier = !terminal && current.PublishBarrier; current.LeaseToken = null; current.LeaseExpiresAtUtc = null;
        current.PublishedVersion = state is "Succeeded" or "Superseded" ? receipt!.Version : null; current.NextAttemptAtUtc = now.AddSeconds(30); current.Version++;
        var job = await Job(row.Id, cancellationToken); job.State = state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : terminal ? "Failed" : "Running";
        job.Phase = state; job.CompletedAtUtc = terminal ? now : null; job.ErrorCode = state == "Succeeded" ? null : code; job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++;
        await Event(job, code, cancellationToken); Audit(current.ActorSubject, current.TraceId, current, "result", state);
        if (terminal) await CompleteOutbox(current.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task DeferAsync(ContainerDirectoryOperation row, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var current = await Lock(row, cancellationToken); if (current is null) return false;
        current.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(6); current.LeaseToken = null; current.LeaseExpiresAtUtc = null; current.Version++;
        await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerDirectoryReceiptDto receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await db.ContainerDirectoryOperations.FromSqlInterpolated($"SELECT * FROM container_directory_operations WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("CONTAINER_DIRECTORY_NOT_FOUND");
        Validate(row);
        if (row.State != "Unknown" || row.CompletedAtUtc is not null || row.Version != version || row.LeaseExpiresAtUtc > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("CONTAINER_DIRECTORY_ACKNOWLEDGEMENT_CONFLICT");
        ContainerDirectoryValidation.Receipt(ContainerDirectoryWorkflowValidation.Reference(row), receipt);
        var now = DateTimeOffset.UtcNow; row.State = "Acknowledged"; row.CompletedAtUtc = now; row.PublishBarrier = false;
        row.ResultCode = "CONTAINER_DIRECTORY_UNKNOWN_ACKNOWLEDGED"; row.AcknowledgedBy = actor.Subject;
        row.AcknowledgementJson = JsonSerializer.Serialize(receipt, ContainerDirectoryWorkflowValidation.Json); row.Version++;
        row.LeaseToken = null; row.LeaseExpiresAtUtc = null;
        var job = await Job(id, cancellationToken); job.State = "Failed"; job.Phase = "Acknowledged"; job.ErrorCode = row.ResultCode; job.CompletedAtUtc = now; job.Version++;
        await Event(job, row.ResultCode, cancellationToken); Audit(actor.Subject, actor.TraceId, row, "unknown-acknowledged", receipt.State);
        await CompleteOutbox(id, now, cancellationToken); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private void Validate(ContainerDirectoryOperation row)
    { if (!ContainerDirectoryWorkflowService.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_DIRECTORY_BINDING_CHANGED"); }
    private async Task<ContainerDirectoryOperation?> Lock(ContainerDirectoryOperation row, CancellationToken cancellationToken)
    {
        var current = await db.ContainerDirectoryOperations.FromSqlInterpolated($"SELECT * FROM container_directory_operations WHERE id={row.Id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        return row.LeaseToken is not null && current?.LeaseToken == row.LeaseToken && current.CompletedAtUtc is null && current.LeaseExpiresAtUtc > DateTimeOffset.UtcNow ? current : null;
    }
    private Task<OperationJob> Job(Guid id, CancellationToken cancellationToken) => db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
    private Task<int> CompleteOutbox(Guid id, DateTimeOffset now, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('ContainerDirectoryRequested','ContainerDirectoryCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var next = (await db.OperationJobEvents.Where(e => e.JobId == job.Id).MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = next, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private void Audit(string actor, string trace, ContainerDirectoryOperation row, string action, string result) => db.AuditEvents.Add(new()
    {
        ActorSubject = actor, TraceId = trace, JobId = row.Id, Action = "containers.files.mkdir." + action, TargetType = "container", TargetId = row.ContainerId,
        Result = result, DetailJson = JsonSerializer.Serialize(new { pathHash = FileManagementService.Hash(row.RelativePath), instanceHash = FileManagementService.Hash(row.InstanceId) })
    });
    private static bool Conflicts(string a, string b) => a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var result = await action(); await transaction.CommitAsync(cancellationToken); return result;
    });
}
