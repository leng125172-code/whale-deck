using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class ContainerMoveRepository
{
    public Task<bool> FinishAsync(ContainerMoveIntent claimed, string state, string code, ContainerMoveReceiptDto? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Code(code); if (state is not ("Succeeded" or "Superseded" or "Canceled" or "Failed" or "Unknown")) throw new ArgumentException("CONTAINER_MOVE_RESULT_INVALID");
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is null) return false;
        if (state != "Unknown" || receipt is not null) Validate(row);
        if (receipt is not null) ContainerMoveValidation.Receipt(ContainerMoveWorkflowValidation.Reference(row), receipt);
        if (state is "Succeeded" or "Superseded" && (receipt?.State != "Completed" || receipt.BindingCurrent != (state == "Succeeded") || row.RootStartedAtUtc is null || !row.PublishBarrier))
            throw new ArgumentException("CONTAINER_MOVE_PUBLICATION_PROOF_REQUIRED");
        if (state is "Canceled" or "Failed" && receipt?.State != "Aborted") throw new ArgumentException("CONTAINER_MOVE_CANCELLATION_PROOF_REQUIRED");
        if (state == "Unknown" && receipt is not null && receipt.State != "Unknown") throw new ArgumentException("CONTAINER_MOVE_RESULT_INVALID");
        MergeFacts(row, receipt);
        var now = Now(); var job = await Job(row, true, cancellationToken); var terminal = state != "Unknown";
        row.State = state; row.ResultCode = code; row.CompletedAtUtc = terminal ? now : null;
        row.PublishBarrier = !terminal && row.PublishBarrier; row.PrivateMayExist = state == "Unknown" || receipt?.PrivateMayExist == true;
        row.PublishedVersion = state is "Succeeded" or "Superseded" ? receipt!.Version : null;
        row.NextAttemptAtUtc = now.AddSeconds(30); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (terminal) { await FinishJob(job, state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : "Failed", state, code, cancellationToken); await Processed(job.Id, now, cancellationToken); }
        else { job.State = "Running"; job.Phase = "AwaitingContainerMoveReceipt"; job.ErrorCode = code; job.Version++; await Event(job, code, cancellationToken); }
        Audit(row, job.Id, row.ActorSubject, row.TraceId, "result", state); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerMoveReceiptDto receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); Validate(row);
        if (row.State != "Unknown" || row.CompletedAtUtc is not null || row.Version != version || row.ExecutionJobId is null || row.LeaseExpiresAtUtc > Now())
            throw new InvalidOperationException("CONTAINER_MOVE_ACKNOWLEDGEMENT_CONFLICT");
        ContainerMoveValidation.Receipt(ContainerMoveWorkflowValidation.Reference(row), receipt);
        MergeFacts(row, receipt);
        var job = await Job(row, true, cancellationToken); var now = Now();
        row.State = "Acknowledged"; row.CompletedAtUtc = now; row.PublishBarrier = false; row.ResultCode = "CONTAINER_MOVE_UNKNOWN_ACKNOWLEDGED";
        row.PrivateMayExist = receipt.PrivateMayExist; row.AcknowledgedBy = actor.Subject;
        row.AcknowledgementJson = ContainerMoveWorkflowValidation.Serialize(receipt); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await FinishJob(job, "Failed", "Acknowledged", row.ResultCode, cancellationToken); await Processed(job.Id, now, cancellationToken);
        Audit(row, job.Id, actor.Subject, actor.TraceId, "unknown-acknowledged", receipt.State); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task EnsurePaths(ContainerMoveIntent row, CancellationToken cancellationToken)
    {
        var moves = await db.ContainerMoveIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null && r.Id != row.Id)
            .Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        var directories = await db.ContainerDirectoryOperations.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var uploads = await db.ContainerFileUploadOperations.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var copies = await db.ContainerCopyIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null)
            .Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        var permissions = await db.ContainerPermissionIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        if (moves.Length + directories.Length + uploads.Length + copies.Length + permissions.Length > 64 || moves.Any(r => Conflicts(r.SourcePath, row.SourcePath) || Conflicts(r.TargetPath, row.SourcePath) || Conflicts(r.TargetPath, row.TargetPath) || Conflicts(r.SourcePath, row.TargetPath)) ||
            copies.Any(r => Conflicts(r.SourcePath, row.SourcePath) || Conflicts(r.TargetPath, row.SourcePath) || Conflicts(r.SourcePath, row.TargetPath) || Conflicts(r.TargetPath, row.TargetPath)) ||
            directories.Concat(uploads).Any(p => Conflicts(p, row.SourcePath) || Conflicts(p, row.TargetPath)) || permissions.Any(path => Conflicts(path, row.SourcePath) || Conflicts(path, row.TargetPath))) throw new InvalidOperationException("CONTAINER_MOVE_TARGET_BUSY");
    }
    private static void MergeFacts(ContainerMoveIntent row, ContainerMoveReceiptDto? receipt)
    {
        // A missing reply cannot erase an observed fact. False is known too.
        static bool? Merge(bool? prior, bool? current)
        {
            if (prior is not null && current is not null && prior != current) throw new InvalidOperationException("CONTAINER_MOVE_FACT_CHANGED");
            return current ?? prior;
        }
        row.DestinationPublished = Merge(row.DestinationPublished, receipt?.DestinationPublished);
        row.SourceRemoved = Merge(row.SourceRemoved, receipt?.SourceRemoved);
        row.SourceRetained = Merge(row.SourceRetained, receipt?.SourceRetained);
    }
    private static bool Conflicts(string a, string b) => a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);
    private DateTimeOffset Now() => ContainerMoveWorkflowValidation.Time(clock.GetUtcNow());
    private void Validate(ContainerMoveIntent row)
    { if (!ContainerMoveWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_MOVE_BINDING_CHANGED"); }
    private static void Owner(ContainerMoveIntent row, TerminalActor actor)
    { if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND"); }
    private static void Code(string code)
    {
        if (code.Length is < 1 or > 64 || !code.StartsWith("CONTAINER_MOVE_", StringComparison.Ordinal) || code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')))
            throw new ArgumentException("CONTAINER_MOVE_RESULT_INVALID");
    }
    private async Task<ContainerMoveIntent> Row(Guid id, CancellationToken cancellationToken) => await db.ContainerMoveIntents
        .FromSqlInterpolated($"SELECT * FROM container_move_intents WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("CONTAINER_MOVE_NOT_FOUND");
    private async Task<ContainerMoveIntent?> Lease(ContainerMoveIntent claimed, CancellationToken cancellationToken)
    {
        var row = await Row(claimed.Id, cancellationToken);
        return claimed.LeaseToken is not null && row.LeaseToken == claimed.LeaseToken && row.LeaseExpiresAtUtc > Now() && row.CompletedAtUtc is null ? row : null;
    }
    private async Task<OperationJob> Job(ContainerMoveIntent row, bool execution, CancellationToken cancellationToken)
    {
        var id = execution ? ContainerMoveWorkflowValidation.ExecutionId(row.Id) : row.Id;
        if (execution && id != row.ExecutionJobId) throw new InvalidOperationException("CONTAINER_MOVE_BINDING_CHANGED");
        var job = await db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
        if (job.ActorSubject != row.ActorSubject || job.JobType != (execution ? "containers.files.move" : "containers.files.move-plan") ||
            job.IdempotencyKey != (execution ? id : row.IdempotencyKey).ToString("D")) throw new InvalidOperationException("CONTAINER_MOVE_JOB_CHANGED");
        return job;
    }
    private static string Metadata(ContainerMoveIntent row) => JsonSerializer.Serialize(new { intentId = row.Id, containerId = row.ContainerId,
        instanceHash = FileManagementService.Hash(row.InstanceId), sourceHash = FileManagementService.Hash(row.SourcePath), targetHash = FileManagementService.Hash(row.TargetPath) });
    private void Message(Guid id, string type) => db.OutboxMessages.Add(new() { MessageType = type, PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
    private Task<int> Processed(Guid id, DateTimeOffset now, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('ContainerMoveRequested','ContainerMoveCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(e => e.JobId == job.Id).MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private async Task FinishJob(OperationJob job, string state, string phase, string code, CancellationToken cancellationToken)
    {
        job.State = state; job.Phase = phase; job.CompletedAtUtc = Now(); job.ErrorCode = state == "Succeeded" ? null : code;
        job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++; await Event(job, code, cancellationToken);
    }
    private void Audit(ContainerMoveIntent row, Guid jobId, string actor, string trace, string action, string result) => db.AuditEvents.Add(new()
    { ActorSubject = actor, TraceId = trace, JobId = jobId, Action = "containers.files.move." + action, TargetType = "container", TargetId = row.ContainerId, Result = result, DetailJson = Metadata(row) });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var value = await action(); await transaction.CommitAsync(cancellationToken); return value;
    });
}
