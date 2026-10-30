using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static readonly string[] PermissionFenceTables = ["container_file_upload_operations", "container_directory_operations", "container_copy_intents", "container_move_intents"];
    /// <summary>Real isolated PostgreSQL/MAC/transactions/triggers. Receipts are synthetic, not Root or UI evidence.</summary>
    private static async Task ContainerPermissionBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var clock = new PermissionClock(); var repo = new ContainerPermissionRepository(db, protector, clock); var other = new ContainerPermissionRepository(second, protector, clock);
        var actor = new TerminalActor("native-permission-admin", null, [], new('A', 64), "native-permission-trace", null);
        var ids = new List<Guid>(); var uploadIds = new List<Guid>(); var directoryIds = new List<Guid>();
        ContainerPermissionIntent Row(string? path = null, string? subject = null)
        {
            var owner = subject ?? actor.Subject; var key = Guid.NewGuid(); var id = ContainerPermissionWorkflowValidation.IntentId(owner, actor.LoginBinding, key); ids.Add(id);
            var now = ContainerPermissionWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerPermissionCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path ?? "work/permission-" + id.ToString("N")), "0600", new('B', 64));
            var binding = new ContainerPermissionRequestBinding(id, key, owner, actor.LoginBinding, actor.TraceId, ContainerPermissionWorkflowValidation.Owner(owner, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = owner, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId, RelativePath = command.Location.Path,
                CommandJson = ContainerPermissionWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintPermissionRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerPermissionPlanDto Plan(ContainerPermissionIntent row) => new(1, ContainerPermissionWorkflowValidation.ReadCommand(row), new('C', 64), new(ulong.MaxValue, 33), new('D', 64),
            1000, 1001, new('E', 64), "File", new(ulong.MaxValue, 101, 33184, 1000, 1001, 1, 10737418247, 1700000000123456789), 1700000000987654321, new('F', 64), false);
        ContainerPermissionConfirmation Claims(ContainerPermissionIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value);
        ContainerPermissionReceiptDto Receipt(ContainerPermissionIntent row, string state, bool current = true) => new(row.ExecutionJobId!.Value, row.ContainerId, row.InstanceId,
            new('E', 64), new('C', 64), state, state == "Completed" ? new('9', 64) : "", state == "Completed" && current, false, "File",
            state == "Unknown" ? null : state == "Completed", false, state != "Aborted", state == "Completed", state == "Completed" ? new('8', 64) : "");
        async Task<ContainerPermissionIntent> Ready(ContainerPermissionIntent row)
        {
            await repo.EnqueueAsync(row, default); var claimed = Assert.IsType<ContainerPermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed, default)); await repo.FinishPlanningAsync(claimed, Plan(row), "CONTAINER_PERMISSION_READY", default);
            return (await repo.FindAsync(row.Id, default))!;
        }
        async Task<ContainerPermissionIntent> Pending(ContainerPermissionIntent row)
        {
            await repo.ConfirmAsync(actor, Claims(row), row.Version, default);
            var claimed = Assert.IsType<ContainerPermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id); return claimed;
        }
        async Task<ContainerPermissionIntent> Applying(ContainerPermissionIntent row)
        { var claimed = await Pending(row); Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claimed, clock.GetUtcNow(), default)); return claimed; }
        async Task Rejected(FormattableString sql, string message)
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, failure.MessageText); }
        async Task PeerSeeds()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id); const string subject = "native-permission-upload-peer";
            var target = new ContainerFileUploadTargetDto(new(new('d', 64), "888:2026-10-30T01:02:03Z", "work/upload-" + id.ToString("N")), new('B', 64), new('C', 64), "ABSENT", 0);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, target, 0, Convert.ToHexString(SHA256.HashData([])), new('F', 64), now, now.AddMinutes(15));
            var upload = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(subject, actor.LoginBinding), ContainerId = target.Location.ContainerId, InstanceId = target.Location.InstanceId,
                RelativePath = target.Location.Path, TargetProof = target.TargetProof, ExpectedParentVersion = target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                Sha256 = binding.Sha256, PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(upload, default);
            id = Guid.NewGuid(); directoryIds.Add(id); const string directorySubject = "native-permission-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-30T01:02:03Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(1, 2), new('C', 64), 1000, 1001, new('D', 64));
            var frozen = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), directorySubject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(directorySubject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var directory = new ContainerDirectoryOperation { Id = id, IdempotencyKey = frozen.IdempotencyKey, ActorSubject = directorySubject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = frozen.Owner, ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path,
                PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json), PlanHash = frozen.PlanHash, RequestHash = protector.FingerprintDirectory(frozen),
                CreatedAtUtc = now, ExpiresAtUtc = frozen.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(directory, now.AddMinutes(5), default);
        }
        // These raw clones exercise ONLY SQL exclusion, not application MAC acceptance.
        // Every clone lives in an explicit rolled-back transaction with its own jobs.
        async Task ClonePeer(string table, Guid seed)
        {
            Assert.Contains(table, PermissionFenceTables);
            var id = Guid.NewGuid(); var execution = Guid.NewGuid(); var key = Guid.NewGuid(); var now = clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob { Id = id, ActorSubject = "native-permission-fence", IdempotencyKey = key.ToString("D"), JobType = "native.permission.fence" },
                new OperationJob { Id = execution, ActorSubject = "native-permission-fence", IdempotencyKey = execution.ToString("D"), JobType = "native.permission.fence" });
            await second.SaveChangesAsync();
            // Table identifiers are from the fixed local whitelist above; all values are parameters.
            var executionPatch = table is "container_copy_intents" or "container_move_intents" ? " || jsonb_build_object('execution_job_id',@execution)" : "";
            var sql = "INSERT INTO " + table + " SELECT (jsonb_populate_record(NULL::" + table + ",to_jsonb(t) || jsonb_build_object('id',@id,'idempotency_key',@key,'completed_at_utc',NULL,'lease_token',NULL,'lease_expires_at_utc',NULL,'publish_barrier',TRUE,'root_started_at_utc',@now,'state',@state,'published_version',NULL,'acknowledged_by',NULL,'acknowledgement_json',NULL)" + executionPatch + ")).* FROM " + table + " t WHERE id=@seed";
            Assert.Equal(1, await second.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", id), new NpgsqlParameter("key", key), new NpgsqlParameter("now", now), new NpgsqlParameter("execution", execution), new NpgsqlParameter("seed", seed), new NpgsqlParameter("state", table == "container_file_upload_operations" ? "Ready" : "Pending")));
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.False(await db.FilePathLocks.AnyAsync());
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerCopyIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerMoveIntents.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerPermissionIntents.AnyAsync(r => r.PublishBarrier));
            await PeerSeeds();
            var original = Row(); var ready = await Ready(original);
            await Rejected($"UPDATE container_permission_intents SET relative_path='elsewhere' WHERE id={ready.Id}", "container_permission_request_immutable");
            await Rejected($"UPDATE container_permission_intents SET plan_hash={new string('A', 64)} WHERE id={ready.Id}", "container_permission_plan_immutable");
            var confirmed = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirmed[0].ExecutionJobId, confirmed[1].ExecutionJobId); Assert.Equal(2, await db.OperationJobs.CountAsync(j => j.Id == ready.Id || j.Id == confirmed[0].ExecutionJobId));
            var apply = Assert.IsType<ContainerPermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_permission_intents SET root_started_at_utc=NULL WHERE id={apply.Id}", "container_permission_started_immutable");
            Assert.True(await repo.FinishAsync(apply, "Succeeded", "CONTAINER_PERMISSION_COMPLETED", Receipt(apply, "Completed"), default));
            var complete = (await other.FindAsync(ready.Id, default))!; Assert.True(complete.ModeConfirmed); Assert.True(complete.HistoryMayExist); Assert.True(complete.HistoryVerified); Assert.Equal(new string('8', 64), complete.HistoryDigest);
            await Rejected($"UPDATE container_permission_intents SET mode_confirmed=false WHERE id={ready.Id}", "container_permission_fact_immutable");
            await Rejected($"UPDATE container_permission_intents SET history_verified=false WHERE id={ready.Id}", "container_permission_fact_immutable");
            await Rejected($"UPDATE container_permission_intents SET history_digest=NULL WHERE id={ready.Id}", "container_permission_fact_immutable");
            await Rejected($"UPDATE container_permission_intents SET version=version+1 WHERE id={ready.Id}", "container_permission_terminal_immutable");
            await Rejected($"DELETE FROM container_permission_intents WHERE id={ready.Id}", "container_permission_permanent_intent");
            // Six-way SQL gates: no raw write bypasses the application checks.
            var seeds = new Dictionary<string, Guid> { ["container_file_upload_operations"] = uploadIds.Single(), ["container_directory_operations"] = directoryIds.Single(),
                ["container_copy_intents"] = await db.ContainerCopyIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_move_intents"] = await db.ContainerMoveIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync() };
            var blocked = await Applying(await Ready(Row())); var pathHash = FileManagementService.Hash("native-permission-host:" + blocked.Id);
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{blocked.Id},'Legacy')", "FILES_TARGET_BUSY");
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync();
                var error = await Record.ExceptionAsync(() => ClonePeer(pair.Key, pair.Value));
                Assert.NotNull(error); Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
                await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            var uncertain = Receipt(blocked, "Unknown") with { HistoryVerified = true, HistoryDigest = new('7', 64) };
            Assert.True(await repo.FinishAsync(blocked, "Unknown", "CONTAINER_PERMISSION_RESULT_UNKNOWN", uncertain, default));
            var unknown = (await other.FindAsync(blocked.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.Null(unknown.ModeConfirmed); Assert.True(unknown.HistoryVerified);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_permission_intents SET history_may_exist=false WHERE id={unknown.Id}", "container_permission_fact_immutable");
            var admin = actor with { Subject = "native-current-permission-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(admin, unknown.Id, unknown.Version, uncertain, default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.False(ack.PublishBarrier); Assert.Null(ack.PublishedVersion);
            Assert.Null(ack.ModeConfirmed); Assert.True(ack.HistoryVerified); Assert.Equal(admin.Subject, ack.AcknowledgedBy);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == ack.ExecutionJobId)).State);
            var inverse = await Pending(await Ready(Row()));
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync(); await ClonePeer(pair.Key, pair.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_permission_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}"));
                Assert.Equal("CONTAINER_PERMISSION_TARGET_BUSY", error.MessageText); await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_permission_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}", "CONTAINER_PERMISSION_TARGET_BUSY");
            await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE operation_id={inverse.Id}");
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_PERMISSION_CANCELED", Receipt(inverse, "Aborted"), default));
            var canceled = (await other.FindAsync(inverse.Id, default))!; Assert.False(canceled.ModeConfirmed); Assert.False(canceled.HistoryMayExist); Assert.Null(canceled.HistoryDigest);
            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            await repo.CancelAsync(actor, expiredReady.Id, default); clock.Offset = TimeSpan.Zero;
            // Audit failure is part of the same SQL transaction as the intent/jobs/outbox.
            var rejected = Row();
            await second.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wdv_permission_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN RAISE EXCEPTION 'wdv_permission_audit_failed' USING ERRCODE='23514'; END $f$;
                CREATE TRIGGER wdv_permission_audit_reject BEFORE INSERT ON audit_events
                  FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.permission.%') EXECUTE FUNCTION wdv_permission_audit_reject();
                """);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(rejected, default)); }
            finally { await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_permission_audit_reject ON audit_events; DROP FUNCTION wdv_permission_audit_reject();"); }
            Assert.False(await second.ContainerPermissionIntents.AnyAsync(r => r.Id == rejected.Id)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == rejected.Id));
            var waiting = new List<ContainerPermissionIntent>();
            for (var n = 0; n < 4; n++) { var row = Row(); await repo.EnqueueAsync(row, default); waiting.Add(row); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default));
            foreach (var row in waiting) await repo.CancelAsync(actor, row.Id, default);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261029080510_ContainerMoveIntents"));
            Assert.Equal("container_permission_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerPermissionIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));
            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerPermissionIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-permission-persistent-jobs-facts-and-six-way-fences");
        }
        finally
        {
            // Only exact synthetic legacy fixtures are deleted; permanent new rows remain until isolated DB removal.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class PermissionClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
