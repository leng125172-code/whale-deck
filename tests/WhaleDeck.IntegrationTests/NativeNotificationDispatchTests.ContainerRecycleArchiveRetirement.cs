using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task RetirementSchemaBoundaries(PlatformDbContext db, PlatformDbContext contender, ContainerRecycleArchiveIntent archive,
        ContainerRecycleArchiveIntent historicalCopy, DateTimeOffset now, string previousMigration, Func<string, Task> clonePeer)
    {
        var id = Guid.NewGuid();
        var execution = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        var invalid = Guid.NewGuid();
        var hash = new string('F', 64);
        foreach (var job in new[] { id, execution, duplicate, invalid })
            db.OperationJobs.Add(new() { Id = job, ActorSubject = "native-retirement", IdempotencyKey = job.ToString("D"), JobType = "native.retirement.schema" });
        await db.SaveChangesAsync();
        var archiveOp = ContainerRecycleArchiveWorkflowValidation.Reference(archive);
        var archiveReceipt = ContainerRecycleArchiveWorkflowValidation.ReadReceipt(archive)!;
        var a = archiveOp.Plan;
        var command = new ContainerRecycleArchiveRetirementCommandDto(new(archiveOp.Id, archiveOp.Owner, a), 30, "ArchiveData");
        var created = a.Command.Purge.Plan.HistoryCreatedAtUnixMs;
        var due = created + 30 * 86400000L;
        var plan = new ContainerRecycleArchiveRetirementPlanDto(1, command, hash, a.SourceRecordDigest, a.PurgeRecordDigest,
            a.PurgeMarkerDigest, hash, a.HistoryMetadataDigest, created, due, false, true, false, a.StorageHash, a.StoreIdentity,
            a.ArchiveIdentity, a.ArchiveChangedAtUnixNs, a.ArchiveBytes, a.ArchiveSha256, archiveReceipt.PublishedIdentity!, archiveReceipt.PublishedChangedAtUnixNs!.Value, hash);
        Assert.True(now.ToUnixTimeMilliseconds() < due);
        var planJson = ContainerRecycleArchiveWorkflowValidation.Serialize(plan);
        var reserved = new ContainerRecycleArchiveRetirementReceiptDto(execution, archiveReceipt.SourceOperationId, archiveOp.Id,
            hash, "Reserved", true, true, false, due);

        Task<int> Insert(Guid intent, Guid source) => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO container_recycle_archive_retirement_intents
              (id,archive_intent_id,retain_days,source_proof_hash,idempotency_key,actor_subject,login_binding,trace_id,owner_hash,request_hash,
              created_at_utc,planning_expires_at_utc,next_attempt_at_utc,state,publish_barrier,version)
            VALUES ({intent},{source},30,{hash},{intent},'native-retirement',{hash},'native-retirement-trace',{hash},{hash},
              {now},{now.AddMinutes(15)},{now},'Queued',false,0)
            """);
        Task<int> SetReady(string json) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents
            SET state='Ready',plan_json={json},plan_hash={hash},ready_expires_at_utc={now.AddMinutes(5)} WHERE id={id}
            """);
        Task<int> Start() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET state='Reserving',publish_barrier=true,root_started_at_utc={now} WHERE id={id}
            """);
        Task<int> Apply(DateTimeOffset at) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET state='Applying',apply_started_at_utc={at} WHERE id={id}
            """);
        Task<int> LockHost(string key) => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({FileManagementService.Hash(key)},{id},'Legacy')
            """);
        static async Task<PostgresException> Reject(string code, Func<Task> action)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(action);
            Assert.Equal(code, error.SqlState);
            return error;
        }

        await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => Insert(invalid, historicalCopy.Id));
        Assert.Equal(1, await Insert(id, archive.Id));
        await Reject(PostgresErrorCodes.UniqueViolation, () => Insert(duplicate, archive.Id));
        await Reject(PostgresErrorCodes.CheckViolation, () => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET retain_days=1 WHERE id={id}
            """));
        await Reject(PostgresErrorCodes.CheckViolation, () => SetReady(planJson));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recycle_archive_retirement_intents SET state='Planning' WHERE id={id}");
        await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => SetReady(ContainerRecycleArchiveWorkflowValidation.Serialize(plan with
        {
            PublishedIdentity = plan.PublishedIdentity with { Inode = plan.PublishedIdentity.Inode + 1 }
        })));
        await Reject(PostgresErrorCodes.CheckViolation, () => SetReady(ContainerRecycleArchiveWorkflowValidation.Serialize(plan with { NotBeforeUnixMs = due - 1 })));
        Assert.Equal(1, await SetReady(planJson));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET state='Pending',execution_job_id={execution},confirmed_at_utc={now},
              execution_expires_at_utc={now.AddMinutes(15)},execution_hash={hash} WHERE id={id}
            """);

        await using (var hostTransaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await LockHost("retirement-advisory-contention"));
            await using var writerTransaction = await contender.Database.BeginTransactionAsync();
            await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='300ms'");
            await Reject(PostgresErrorCodes.LockNotAvailable, () => contender.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE container_recycle_archive_retirement_intents SET state='Reserving',publish_barrier=true,root_started_at_utc={now} WHERE id={id}
                """));
            await writerTransaction.RollbackAsync();
            await hostTransaction.RollbackAsync();
        }
        foreach (var peer in new[] { "file_path_locks", "container_directory_operations" })
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (peer == "file_path_locks") Assert.Equal(1, await LockHost("before-retirement"));
            else await clonePeer(peer);
            await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => Start());
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
        Assert.Equal(1, await Start());
        await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => LockHost("retirement-reserving"));
        await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => clonePeer("container_directory_operations"));
        db.ChangeTracker.Clear();
        await Reject(PostgresErrorCodes.CheckViolation, () => Apply(DateTimeOffset.FromUnixTimeMilliseconds(due)));
        var reservedJson = ContainerRecycleArchiveWorkflowValidation.Serialize(reserved);
        var collapsed = await Reject(PostgresErrorCodes.CheckViolation, () => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET reservation_json={reservedJson},reservation_hash={hash},
              state='Applying',apply_started_at_utc={DateTimeOffset.FromUnixTimeMilliseconds(due)} WHERE id={id}
            """));
        Assert.Equal("archive_retirement_stage_order", collapsed.MessageText);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET reservation_json={reservedJson},reservation_hash={hash} WHERE id={id}
            """);
        await Reject(PostgresErrorCodes.CheckViolation, () => Apply(DateTimeOffset.FromUnixTimeMilliseconds(due - 1)));
        Assert.Equal(1, await Apply(DateTimeOffset.FromUnixTimeMilliseconds(due)));
        var unknown = ContainerRecycleArchiveWorkflowValidation.Serialize(reserved with { State = "Unknown", RetirementApplied = null });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET state='Unknown',root_state='Unknown',receipt_json={unknown},receipt_hash={hash} WHERE id={id}
            """);
        await Reject(PostgresErrorCodes.ObjectNotInPrerequisiteState, () => LockHost("retirement-unknown"));
        var changedReceipt = ContainerRecycleArchiveWorkflowValidation.Serialize(reserved with { State = "Unknown", RetirementApplied = null, OwnedFence = false });
        var factChanged = await Reject(PostgresErrorCodes.CheckViolation, () => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET receipt_json={changedReceipt},receipt_hash={new string('E', 64)} WHERE id={id}
            """));
        Assert.Equal("archive_retirement_fact_immutable", factChanged.MessageText);
        await Reject(PostgresErrorCodes.CheckViolation, () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM container_recycle_archive_retirement_intents WHERE id={id}"));
        await Reject(PostgresErrorCodes.CheckViolation, () => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET publish_barrier=false WHERE id={id}
            """));
        const string acknowledgement = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE container_recycle_archive_retirement_intents SET state='Acknowledged',acknowledged_by='other-admin',acknowledgement_json={acknowledgement},
              completed_at_utc={now.AddMinutes(3)},publish_barrier=false WHERE id={id}
            """);
        var final = await db.ContainerRecycleArchiveRetirementIntents.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("Acknowledged", final.State);
        Assert.Null(final.RetirementApplied);
        Assert.Equal(unknown, final.ReceiptJson);
        await Reject(PostgresErrorCodes.UniqueViolation, () => Insert(duplicate, archive.Id));
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await LockHost("after-acknowledgement"));
            await transaction.RollbackAsync();
        }
        var downgrade = await Reject(PostgresErrorCodes.CheckViolation, () => db.GetService<IMigrator>().MigrateAsync(previousMigration));
        Assert.Equal("archive_retirement_permanent_intent", downgrade.MessageText);
        var triggerCount = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_trigger WHERE tgname='archive_retirement_fence' AND NOT tgisinternal
            """).SingleAsync();
        Assert.Equal(17, triggerCount);
        Console.WriteLine("INBOX_PHASE=archive-retirement-plan-due-two-phase-bidirectional-fences-unknown-ack-history");
    }
}
