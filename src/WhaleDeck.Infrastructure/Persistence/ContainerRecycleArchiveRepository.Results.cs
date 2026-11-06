using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class ContainerRecycleArchiveRepository
{
    public Task<bool> FinishAsync(ContainerRecycleArchiveIntent claimed, string state, string code, ContainerRecycleArchiveReceiptDto? receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        Code(code); if (state is not ("Succeeded" or "Canceled" or "Failed" or "Unknown")) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RESULT_INVALID");
        var row = await Lease(claimed, cancellationToken); if (row is null || row.ExecutionJobId is null) return false;
        if (state != "Unknown" || receipt is not null) Validate(row);
        if (receipt is not null) ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        if(state=="Succeeded"&&(receipt?.State!="Completed"||receipt.ArchiveCopied!=true||row.RootStartedAtUtc is null||row.ApplyStartedAtUtc is null||row.ReservationJson is null||!row.PublishBarrier))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_PUBLICATION_PROOF_REQUIRED");
        if (state is "Canceled" or "Failed" && receipt?.State != "Aborted") throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CANCELLATION_PROOF_REQUIRED");
        if (state == "Unknown" && receipt is not null && receipt.State != "Unknown") throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RESULT_INVALID");
        var now = Now(); var job = await Job(row, true, cancellationToken); var terminal = state != "Unknown";
        row.State = state; row.ResultCode = code; row.CompletedAtUtc = terminal ? now : null;
        row.PublishBarrier = !terminal && row.PublishBarrier;
        Facts(row, receipt);
        row.NextAttemptAtUtc = now.AddSeconds(30); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        if (terminal) { await FinishJob(job, row, state == "Succeeded" ? "Succeeded" : state == "Canceled" ? "Canceled" : "Failed", state, code, cancellationToken); await Processed(job.Id, now, cancellationToken); }
        else { job.State = "Running"; job.Phase = "AwaitingContainerRecycleArchiveReceipt"; job.ErrorCode = code; job.ResultJson=await JobResult(row,job,cancellationToken); job.Version++; await Event(job, code, cancellationToken); }
        Audit(row, job.Id, row.ActorSubject, row.TraceId, "result", state); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleArchiveReceiptDto receipt, CancellationToken cancellationToken) => Transaction(async () =>
    {
        var row = await Row(id, cancellationToken); Validate(row);
        if (row.State != "Unknown" || row.CompletedAtUtc is not null || row.Version != version || row.ExecutionJobId is null || row.LeaseExpiresAtUtc > Now())
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_ACKNOWLEDGEMENT_CONFLICT");
        ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        var job = await Job(row, true, cancellationToken); var now = Now();
        row.State = "Acknowledged"; row.CompletedAtUtc = now; row.PublishBarrier = false; row.ResultCode = "CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED";
        Facts(row, receipt); row.AcknowledgedBy = actor.Subject;
        row.AcknowledgementJson = ContainerRecycleArchiveWorkflowValidation.Serialize(receipt); row.LeaseToken = null; row.LeaseExpiresAtUtc = null; row.Version++;
        await FinishJob(job, row, "Failed", "Acknowledged", row.ResultCode, cancellationToken); await Processed(job.Id, now, cancellationToken);
        Audit(row, job.Id, actor.Subject, actor.TraceId, "unknown-acknowledged", receipt.State); await db.SaveChangesAsync(cancellationToken); return true;
    }, cancellationToken);
    private async Task EnsurePaths(ContainerRecycleArchiveIntent row, CancellationToken cancellationToken)
    {
        var purges = await db.ContainerRecyclePurgeIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var restorations = await db.ContainerRecycleRestoreIntents.AsNoTracking().Where(r=>r.ContainerId==row.ContainerId&&r.CompletedAtUtc==null).Select(r=>r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var recycles = await db.ContainerRecycleIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var recursiveAclRestores = await db.ContainerRecursiveAclRestoreIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var ordinaryPermissions = await db.ContainerPermissionIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var recursivePermissions = await db.ContainerRecursivePermissionIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var acls = await db.ContainerAclIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var otherRestores = await db.ContainerRecycleArchiveIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null && r.Id != row.Id).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var originalAcls = await db.ContainerRecursiveAclIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var recursiveOwnerships = await db.ContainerRecursiveOwnershipIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var ownerships = await db.ContainerOwnershipIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var directories = await db.ContainerDirectoryOperations.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var uploads = await db.ContainerFileUploadOperations.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null).Select(r => r.RelativePath).Take(65).ToArrayAsync(cancellationToken);
        var copies = await db.ContainerCopyIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null)
            .Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        var moves = await db.ContainerMoveIntents.AsNoTracking().Where(r => r.ContainerId == row.ContainerId && r.CompletedAtUtc == null)
            .Select(r => new { r.SourcePath, r.TargetPath }).Take(65).ToArrayAsync(cancellationToken);
        if (purges.Length + restorations.Length + recycles.Length + recursiveAclRestores.Length + recursiveOwnerships.Length + ownerships.Length + acls.Length + otherRestores.Length + originalAcls.Length + recursivePermissions.Length + ordinaryPermissions.Length + directories.Length + uploads.Length + copies.Length + moves.Length > 64 ||
            purges.Concat(restorations).Concat(recycles).Concat(recursiveAclRestores).Concat(ownerships).Concat(recursiveOwnerships).Concat(recursivePermissions).Concat(otherRestores).Concat(originalAcls).Concat(acls).Concat(ordinaryPermissions).Concat(directories).Concat(uploads).Any(p => Conflicts(p, row.RelativePath)) ||
            copies.Any(r => Conflicts(r.SourcePath, row.RelativePath) || Conflicts(r.TargetPath, row.RelativePath)) ||
            moves.Any(r => Conflicts(r.SourcePath, row.RelativePath) || Conflicts(r.TargetPath, row.RelativePath)))
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_TARGET_BUSY");
    }
    private void Facts(ContainerRecycleArchiveIntent row, ContainerRecycleArchiveReceiptDto? receipt)
    {
        if (receipt is null) return;
        ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(row, receipt);
        row.ReceiptHash = protector.FingerprintRecycleArchiveReceipt(new(ContainerRecycleArchiveWorkflowValidation.Reference(row), receipt));
        row.ReceiptJson = ContainerRecycleArchiveWorkflowValidation.Serialize(receipt); row.RootState = receipt.State;
        row.ArchiveCopied=receipt.ArchiveCopied;row.SourceFenced=receipt.SourceFenced;row.OwnedFence=receipt.OwnedFence;row.BindingCurrent=receipt.BindingCurrent;
    }
    private async Task EnsureSource(ContainerRecycleArchiveIntent row, CancellationToken cancellationToken)
    {
        if(await db.ContainerRecycleArchiveIntents.AnyAsync(x=>x.SourceIntentId==row.SourceIntentId&&x.Id!=row.Id&&(x.CompletedAtUtc==null||x.ArchiveCopied==true||x.RootState=="Unknown"),cancellationToken))
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
        var purge=await db.ContainerRecyclePurgeIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_purge_intents WHERE id={row.SourceIntentId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
        var original=await db.ContainerRecycleIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_intents WHERE id={purge.SourceIntentId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
        var current=ContainerRecycleArchiveSourceResolver.FromRows(purge,original,purgeProtector,sourceProtector,protector);
        if(current.ProofHash!=row.SourceProofHash||!ContainerRecycleArchiveWorkflowValidation.SameCommand(current.Command,ContainerRecycleArchiveWorkflowValidation.ReadCommand(row)))
            throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED");
    }
    private static bool Conflicts(string a, string b) => a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);
    private DateTimeOffset Now() => ContainerRecycleArchiveWorkflowValidation.Time(clock.GetUtcNow());
    private void Validate(ContainerRecycleArchiveIntent row)
    { if (!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(row, protector)) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED"); }
    private static void Owner(ContainerRecycleArchiveIntent row, TerminalActor actor)
    { if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND"); }
    private static void Code(string code)
    {
        if (code.Length is < 1 or > 64 || !code.StartsWith("CONTAINER_RECYCLE_ARCHIVE_", StringComparison.Ordinal) || code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')))
            throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RESULT_INVALID");
    }
    private async Task<ContainerRecycleArchiveIntent> Row(Guid id, CancellationToken cancellationToken) => await db.ContainerRecycleArchiveIntents
        .FromSqlInterpolated($"SELECT * FROM container_recycle_archive_intents WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND");
    private async Task<ContainerRecycleArchiveIntent?> Lease(ContainerRecycleArchiveIntent claimed, CancellationToken cancellationToken)
    {
        var row = await Row(claimed.Id, cancellationToken);
        return claimed.LeaseToken is not null && row.LeaseToken == claimed.LeaseToken && row.LeaseExpiresAtUtc > Now() && row.CompletedAtUtc is null ? row : null;
    }
    private async Task<OperationJob> Job(ContainerRecycleArchiveIntent row, bool execution, CancellationToken cancellationToken)
    {
        var id = execution ? ContainerRecycleArchiveWorkflowValidation.ExecutionId(row.Id) : row.Id;
        if (execution && id != row.ExecutionJobId) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_BINDING_CHANGED");
        var job = await db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(cancellationToken);
        if (job.ActorSubject != row.ActorSubject || job.JobType != (execution ? "containers.files.recycle-archive" : "containers.files.recycle-archive-plan") ||
            job.IdempotencyKey != (execution ? id : row.IdempotencyKey).ToString("D")) throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_JOB_CHANGED");
        return job;
    }
    private static string Metadata(ContainerRecycleArchiveIntent row) => JsonSerializer.Serialize(new { intentId = row.Id, containerId = row.ContainerId,
        instanceHash = FileManagementService.Hash(row.InstanceId), pathHash = FileManagementService.Hash(row.RelativePath) });
    private void Message(Guid id, string type) => db.OutboxMessages.Add(new() { MessageType = type, PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });
    private Task<int> Processed(Guid id, DateTimeOffset now, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE outbox_messages SET processed_at_utc={now} WHERE message_type IN ('ContainerRecycleArchiveRequested','ContainerRecycleArchiveCancellationRequested') AND payload_json->>'jobId'={id.ToString("D")}", cancellationToken);
    private async Task Event(OperationJob job, string code, CancellationToken cancellationToken)
    {
        var sequence = (await db.OperationJobEvents.Where(e => e.JobId == job.Id).MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = job.State, Phase = job.Phase, MessageCode = code });
    }
    private async Task<string?> JobResult(ContainerRecycleArchiveIntent row,OperationJob job,CancellationToken token)
    {
        if(!ContainerRecycleArchiveWorkflowValidation.HasValidBinding(row,protector))return null;
        var source=await db.ContainerRecyclePurgeIntents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.SourceIntentId,token);
        if(source is null||!ContainerRecyclePurgeWorkflowValidation.HasValidBinding(source,purgeProtector))return null;
        var json=ContainerRecycleArchiveJobResults.Snapshot(row,job.Id==row.ExecutionJobId,source.SourceIntentId);
        return ContainerRecycleArchiveJobResults.ForJob(new OperationJob{Id=job.Id,JobType=job.JobType,State=job.State,ActorSubject=job.ActorSubject,IdempotencyKey=job.IdempotencyKey,ResultJson=json});
    }
    private async Task FinishJob(OperationJob job, ContainerRecycleArchiveIntent row, string state, string phase, string code, CancellationToken cancellationToken)
    {
        job.State = state; job.Phase = phase; job.CompletedAtUtc = Now(); job.ErrorCode = state == "Succeeded" ? null : code;
        job.ResultJson=await JobResult(row,job,cancellationToken);

        job.ProgressPercent = state == "Succeeded" ? (short)100 : null; job.Version++; await Event(job, code, cancellationToken);
    }
    private void Audit(ContainerRecycleArchiveIntent row, Guid jobId, string actor, string trace, string action, string result) => db.AuditEvents.Add(new()
    { ActorSubject = actor, TraceId = trace, JobId = jobId, Action = "containers.files.recycle-archive." + action, TargetType = "container", TargetId = row.ContainerId, Result = result, DetailJson = Metadata(row) });
    private Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken cancellationToken) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", cancellationToken);
        var value = await action(); await transaction.CommitAsync(cancellationToken); return value;
    });
}
