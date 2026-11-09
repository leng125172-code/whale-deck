using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class ContainerRecycleArchiveRetirementRepository
{
    public Task<bool> RenewAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null) return false;
        row.LeaseExpiresAtUtc = Now().AddSeconds(90);
        row.Version++;
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task DeferAsync(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token) => Transaction(async () =>
    {
        var row = await Lease(claimed, token);
        if (row is null) return false;
        row.NextAttemptAtUtc = Now().AddSeconds(6);
        ReleaseLease(row);
        row.Version++;
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task<bool> FinishAsync(ContainerRecycleArchiveRetirementIntent claimed, string code, ContainerRecycleArchiveRetirementReceiptDto? receipt,
        CancellationToken token) => Transaction(async () =>
    {
        Code(code);
        var row = await Lease(claimed, token);
        if (row is null || row.ExecutionJobId is null) return false;
        var job = await Job(row, token);
        var now = Now();
        if (row.LeaseExpiresAtUtc <= now) return false;
        if (receipt is not null)
        {
            if (row.RootStartedAtUtc is null || receipt.State == "Reserved" || receipt.State == "Completed" && row.ApplyStartedAtUtc is null)
                throw Retirement.Invalid();
            RecordReceipt(row, receipt);
        }
        var state = receipt?.State == "Completed" ? "Succeeded"
            : receipt?.State == "Unknown" || receipt is null && row.RootStartedAtUtc is not null ? "Unknown"
            : Canceled(row, job) ? "Canceled"
            : row.ExecutionExpiresAtUtc <= now ? "Expired" : "Failed";
        row.State = state;
        row.ResultCode = state switch
        {
            "Succeeded" => Prefix + "COMPLETED",
            "Unknown" => Prefix + "RESULT_UNKNOWN",
            "Canceled" => Prefix + "CANCELED",
            "Expired" => Prefix + "EXECUTION_EXPIRED",
            _ => code
        };
        row.PublishBarrier = state == "Unknown";
        row.CompletedAtUtc = row.PublishBarrier ? null : now;
        ReleaseLease(row);
        row.Version++;
        await AdvanceJob(job, state is "Succeeded" or "Canceled" ? state : state == "Unknown" ? "Running" : "Failed", state, row.ResultCode, now, token);
        if (!row.PublishBarrier) await Processed(job.Id, now, token);
        Audit(row, job.Id, row.ActorSubject, row.TraceId, "execution-result", state);
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleArchiveRetirementReceiptDto observation,
        CancellationToken token) => Transaction(async () =>
    {
        // The application entry point revalidates the acknowledging administrator.
        var row = await Row(id, token);
        Validate(row);
        if (row.State != "Unknown" || row.Version != version || !row.PublishBarrier || row.LeaseToken is not null)
            throw Failure("ACKNOWLEDGEMENT_CONFLICT");
        var op = Retirement.Operation(row);
        ContainerRecycleArchiveRetirementValidation.Receipt(op, observation);
        if (observation.State == "Reserved" || observation.State == "Completed" && row.ApplyStartedAtUtc is null)
            throw Failure("RESULT_UNRESOLVED");
        if (row.ReceiptJson is null) RecordReceipt(row, observation);
        else if (row.RootState != observation.State || row.RetirementApplied != observation.RetirementApplied)
            throw Failure("FACT_CHANGED");
        var job = await Job(row, token);
        var now = Now();
        row.AcknowledgedBy = actor.Subject;
        row.AcknowledgementJson = Retirement.Write(observation);
        row.CompletedAtUtc = now;
        row.PublishBarrier = false;
        row.State = "Acknowledged";
        row.ResultCode = Prefix + "UNKNOWN_ACKNOWLEDGED";
        row.Version++;
        await AdvanceJob(job, "Failed", row.State, row.ResultCode, now, token);
        await Processed(job.Id, now, token);
        Audit(row, job.Id, actor.Subject, actor.TraceId, "unknown-acknowledged", observation.State);
        await db.SaveChangesAsync(token);
        return true;
    }, token);

    private void RecordReceipt(ContainerRecycleArchiveRetirementIntent row, ContainerRecycleArchiveRetirementReceiptDto receipt)
    {
        row.ReceiptHash = protector.FingerprintRetirementReceipt(Retirement.Operation(row), receipt);
        row.ReceiptJson = Retirement.Write(receipt);
        row.RootState = receipt.State;
        row.RetirementApplied = receipt.RetirementApplied;
    }

    private async Task ClosePlanning(ContainerRecycleArchiveRetirementIntent row, OperationJob job, string state, string code, DateTimeOffset now, CancellationToken token)
    {
        row.State = state;
        row.ResultCode = code;
        row.CompletedAtUtc = now;
        ReleaseLease(row);
        if (job.State != "Succeeded") await AdvanceJob(job, state == "Canceled" ? state : "Failed", state, code, now, token);
        await Processed(job.Id, now, token);
    }

    private async Task<ResolvedContainerRecycleArchiveRetirementSource> LockSource(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        var archive = await db.ContainerRecycleArchiveIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_archive_intents WHERE id={row.ArchiveIntentId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Failure("SOURCE_CHANGED");
        var purge = await db.ContainerRecyclePurgeIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_purge_intents WHERE id={archive.SourceIntentId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Failure("SOURCE_CHANGED");
        var original = await db.ContainerRecycleIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_intents WHERE id={purge.SourceIntentId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Failure("SOURCE_CHANGED");
        var ancestor = ContainerRecycleArchiveSourceResolver.FromRows(purge, original, purgeProtector, recycleProtector, archiveProtector);
        var source = ContainerRecycleArchiveRetirementSourceResolver.FromVerifiedRows(archive, ancestor, checked((uint)row.RetainDays), archiveProtector, protector);
        if (!Retirement.EqualMac(row.SourceProofHash, source.ProofHash)) throw Failure("SOURCE_CHANGED");
        if (row.PlanJson is not null) ContainerRecycleArchiveRetirementSourceResolver.RequireCurrentPlan(source, Retirement.ReadPlan(row));
        return source;
    }

    private Task<bool> WritersBusy(CancellationToken token) => db.Database.SqlQueryRaw<bool>("""
        SELECT (
          EXISTS (SELECT 1 FROM file_path_locks)
          OR EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recycle_purge_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recycle_archive_intents WHERE publish_barrier)
          OR EXISTS (SELECT 1 FROM container_recycle_archive_retirement_intents WHERE publish_barrier)
        ) AS "Value"
        """).SingleAsync(token);

    private Task<T> Transaction<T>(Func<Task<T>> body, CancellationToken token) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='10s'; SELECT pg_advisory_xact_lock(41307341);", token);
        var result = await body();
        await transaction.CommitAsync(token);
        return result;
    });

    private async Task<ContainerRecycleArchiveRetirementIntent> Row(Guid id, CancellationToken token) =>
        await db.ContainerRecycleArchiveRetirementIntents.FromSqlInterpolated($"SELECT * FROM container_recycle_archive_retirement_intents WHERE id={id} FOR UPDATE")
            .SingleOrDefaultAsync(token) ?? throw Missing();

    private async Task<ContainerRecycleArchiveRetirementIntent?> Lease(ContainerRecycleArchiveRetirementIntent claimed, CancellationToken token)
    {
        var row = await Row(claimed.Id, token);
        if (claimed.LeaseToken is null || row.LeaseToken != claimed.LeaseToken || row.LeaseExpiresAtUtc <= Now() || row.CompletedAtUtc is not null || row.State == "Unknown")
            return null;
        Validate(row);
        return row;
    }

    private async Task<OperationJob> Job(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
    {
        var id = row.ExecutionJobId ?? row.Id;
        var job = await db.OperationJobs.FromSqlInterpolated($"SELECT * FROM operation_jobs WHERE id={id} FOR UPDATE").SingleAsync(token);
        if (job.ActorSubject != row.ActorSubject || job.IdempotencyKey != id.ToString("D") ||
            job.JobType != (row.ExecutionJobId is null ? PlanJobType : ExecutionJobType))
            throw Failure("JOB_CHANGED");
        return job;
    }

    private void AddJob(ContainerRecycleArchiveRetirementIntent row, Guid id, string type, string code, DateTimeOffset now)
    {
        var job = new OperationJob
        {
            Id = id, JobType = type, ActorSubject = row.ActorSubject, IdempotencyKey = id.ToString("D"), CreatedAtUtc = now,
            RequestJson = Metadata(row)
        };
        db.OperationJobs.Add(job);
        Message(id, Requested);
        db.OperationJobEvents.Add(new() { JobId = id, Sequence = 1, State = job.State, Phase = job.Phase, MessageCode = Prefix + code });
        Audit(row, id, row.ActorSubject, row.TraceId, code == "CONFIRMED" ? "confirmed" : "plan-accepted", "Accepted");
    }

    private async Task AdvanceJob(OperationJob job, string state, string phase, string code, DateTimeOffset now, CancellationToken token)
    {
        job.State = state;
        job.Phase = phase;
        job.StartedAtUtc ??= now;
        job.CompletedAtUtc = state == "Running" ? null : now;
        job.ProgressPercent = state == "Succeeded" ? (short)100 : null;
        job.ErrorCode = state is "Succeeded" or "Running" ? null : code;
        job.Version++;
        var sequence = (await db.OperationJobEvents.Where(e => e.JobId == job.Id).MaxAsync(e => (long?)e.Sequence, token) ?? 0) + 1;
        db.OperationJobEvents.Add(new() { JobId = job.Id, Sequence = sequence, State = state, Phase = phase, MessageCode = code });
    }

    private void Message(Guid id, string type) => db.OutboxMessages.Add(new() { MessageType = type, PayloadJson = JsonSerializer.Serialize(new { jobId = id }) });

    private Task<int> Processed(Guid id, DateTimeOffset now, CancellationToken token) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE outbox_messages SET processed_at_utc={now} WHERE processed_at_utc IS NULL AND message_type IN ({Requested},{CancellationRequested})
          AND payload_json->>'jobId'={id.ToString("D")}
        """, token);

    private static string Metadata(ContainerRecycleArchiveRetirementIntent row) =>
        JsonSerializer.Serialize(new { intentId = row.Id, archiveIntentId = row.ArchiveIntentId, retainDays = row.RetainDays });

    private void Audit(ContainerRecycleArchiveRetirementIntent row, Guid jobId, string actor, string trace, string action, string result) =>
        db.AuditEvents.Add(new()
        {
            ActorSubject = actor, TraceId = trace, JobId = jobId, Action = "containers.files.recycle-archive-retirement." + action,
            TargetType = "container-recycle-archive", TargetId = row.ArchiveIntentId.ToString("D"), Result = result, DetailJson = Metadata(row)
        });

    private void Validate(ContainerRecycleArchiveRetirementIntent row) => Retirement.Verify(row, protector);
    private DateTimeOffset Now() => Retirement.Time(clock.GetUtcNow());
    private static bool Canceled(ContainerRecycleArchiveRetirementIntent row, OperationJob job) => row.CancelRequestedAtUtc is not null || job.CancelRequestedAtUtc is not null;
    private static void ReleaseLease(ContainerRecycleArchiveRetirementIntent row) { row.LeaseToken = null; row.LeaseExpiresAtUtc = null; }
    private static bool Due(ContainerRecycleArchiveRetirementIntent row, DateTimeOffset now)
    {
        var plan = Retirement.ReadPlan(row);
        return plan.RetirementEligible && plan.NotBeforeUnixMs <= now.ToUnixTimeMilliseconds();
    }
    private static void Own(ContainerRecycleArchiveRetirementIntent row, TerminalActor actor)
    {
        if (row.ActorSubject != actor.Subject || row.LoginBinding != actor.LoginBinding) throw Missing();
    }
    private static KeyNotFoundException Missing() => new(Prefix + "NOT_FOUND");
    private static InvalidOperationException Failure(string code) => new(Prefix + code);
    private static void Code(string code)
    {
        if (code.Length is < 1 or > 64 || !code.StartsWith(Prefix, StringComparison.Ordinal) ||
            code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_'))) throw Retirement.Invalid();
    }
}
