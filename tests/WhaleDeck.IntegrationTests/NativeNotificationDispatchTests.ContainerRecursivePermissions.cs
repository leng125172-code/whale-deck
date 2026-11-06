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
    private static readonly string[] RecursivePermissionFenceTables = ["container_file_upload_operations", "container_directory_operations", "container_copy_intents", "container_move_intents", "container_permission_intents", "container_ownership_intents"];
    private static async Task ContainerRecursivePermissionIsolatedBoundaries(DbContextOptions<PlatformDbContext> originalOptions)
    {
        await using var original = new PlatformDbContext(originalOptions);
        // Earlier host tests intentionally keep Unknown/tampered locks. Never release them to pass this gate.
        const string locksSql = "SELECT COALESCE(jsonb_agg(to_jsonb(l) ORDER BY path_hash),'[]'::jsonb)::text AS \"Value\" FROM file_path_locks l";
        var beforeLocks = await original.Database.SqlQueryRaw<string>(locksSql).SingleAsync();
        var primary = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString());
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_RECURSIVE_TEST_CONNECTION"));
        if (connection.Database is null || !System.Text.RegularExpressions.Regex.IsMatch(connection.Database, "^wdv_dispatch_[a-f0-9]{16}$") ||
            connection.Database == primary.Database || connection.Host != primary.Host || connection.Port != primary.Port || connection.Username != primary.Username)
            throw new InvalidOperationException("NATIVE_RECURSIVE_DATABASE_INVALID");
        connection.SearchPath = "public";
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var isolated = new PlatformDbContext(options);
        Assert.False(await isolated.Database.SqlQueryRaw<bool>("SELECT rolsuper AS \"Value\" FROM pg_roles WHERE rolname=current_user").SingleAsync());
        Assert.Equal("public", await isolated.Database.SqlQueryRaw<string>("SELECT current_schema()::text AS \"Value\"").SingleAsync());
        await isolated.Database.MigrateAsync();
        // Actual peer repository regressions supply permanent completed fixtures for raw fence probes.
        await ContainerCopyBoundaries(options);
        await ContainerMoveBoundaries(options);
        await ContainerPermissionBoundaries(options);
        await ContainerOwnershipBoundaries(options);
        await ContainerRecursivePermissionBoundaries(options);
        await ContainerRecursiveOwnershipBoundaries(options);
        await ContainerAclBoundaries(options);
        await ContainerAclRestoreBoundaries(options);
        await ContainerRecursiveAclBoundaries(options);
        await ContainerRecursiveAclRestoreBoundaries(options);
        await ContainerRecycleBoundaries(options);
        await ContainerRecycleRestoreBoundaries(options);
        await ContainerRecyclePurgeBoundaries(options);
        Assert.Equal(beforeLocks, await original.Database.SqlQueryRaw<string>(locksSql).SingleAsync());
        // Keep both databases, including every Unknown/terminal row, until the runner removes its owned resources.
    }
    /// <summary>Real isolated PostgreSQL/MAC/transactions/triggers. Receipts are synthetic, not Root or UI evidence.</summary>
    private static async Task ContainerRecursivePermissionBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var clock = new RecursivePermissionClock(); var repo = new ContainerRecursivePermissionRepository(db, protector, clock); var other = new ContainerRecursivePermissionRepository(second, protector, clock);
        var actor = new TerminalActor("native-recursive-admin", null, [], new('A', 64), "native-recursive-trace", null);
        var ids = new List<Guid>(); var uploadIds = new List<Guid>(); var directoryIds = new List<Guid>();
        ContainerRecursivePermissionIntent Row(string? path = null, string? subject = null)
        {
            var owner = subject ?? actor.Subject; var key = Guid.NewGuid(); var id = ContainerRecursivePermissionWorkflowValidation.IntentId(owner, actor.LoginBinding, key); ids.Add(id);
            var now = ContainerRecursivePermissionWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerRecursivePermissionCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path ?? "work/ownership-" + id.ToString("N")), "0600", "0750", new('B', 64));
            var binding = new ContainerRecursivePermissionRequestBinding(id, key, owner, actor.LoginBinding, actor.TraceId, ContainerRecursivePermissionWorkflowValidation.Owner(owner, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = owner, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId, RelativePath = command.Location.Path,
                CommandJson = ContainerRecursivePermissionWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintRecursivePermissionRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerRecursivePermissionPlanDto Plan(ContainerRecursivePermissionIntent row) => new(1, ContainerRecursivePermissionWorkflowValidation.ReadCommand(row), new('C', 64), new(ulong.MaxValue, 33), new('D', 64),
            1000, 1001, new('E', 64), new(ulong.MaxValue, 101, 16872, 1000, 1001, 2, 4096, 1700000000123456789), 1700000000987654321, new('F', 64), 4, 2, 2, 2);
        ContainerRecursivePermissionConfirmation Claims(ContainerRecursivePermissionIntent row) => new(row.Id, row.ActorSubject, row.LoginBinding, row.PlanHash!, row.ReadyExpiresAtUtc!.Value);
        ContainerRecursivePermissionReceiptDto Receipt(ContainerRecursivePermissionIntent row, string state, bool current = true) => new(row.ExecutionJobId!.Value, row.ContainerId, row.InstanceId,
            new('E', 64), new('C', 64), state, state == "Completed" && current, false,
            state == "Completed" ? 4U : state == "Partial" ? 1U : 0U, state == "Completed" ? 2U : state == "Partial" ? 1U : 0U, 0,
            state == "Aborted" ? 4U : state == "Partial" ? 3U : 0U, state == "Unknown" ? 4U : 0U,
            state != "Aborted", state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('8', 64) : "",
            state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('9', 64) : "");
        async Task<ContainerRecursivePermissionIntent> Ready(ContainerRecursivePermissionIntent row)
        {
            await repo.EnqueueAsync(row, default); var claimed = Assert.IsType<ContainerRecursivePermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id);
            Assert.True(await repo.MarkPlanningAsync(claimed, default)); await repo.FinishPlanningAsync(claimed, Plan(row), "CONTAINER_RECURSIVE_PERMISSION_READY", default);
            return (await repo.FindAsync(row.Id, default))!;
        }
        async Task<ContainerRecursivePermissionIntent> Pending(ContainerRecursivePermissionIntent row)
        {
            await repo.ConfirmAsync(actor, Claims(row), row.Version, default);
            var claimed = Assert.IsType<ContainerRecursivePermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(row.Id, claimed.Id); return claimed;
        }
        async Task<ContainerRecursivePermissionIntent> Applying(ContainerRecursivePermissionIntent row)
        { var claimed = await Pending(row); Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claimed, clock.GetUtcNow(), default)); return claimed; }
        async Task Rejected(FormattableString sql, string message)
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, failure.MessageText); }
        async Task PeerSeeds()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id); const string subject = "native-recursive-upload-peer";
            var target = new ContainerFileUploadTargetDto(new(new('d', 64), "888:2026-10-30T01:02:03Z", "work/upload-" + id.ToString("N")), new('B', 64), new('C', 64), "ABSENT", 0);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, target, 0, Convert.ToHexString(SHA256.HashData([])), new('F', 64), now, now.AddMinutes(15));
            var upload = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(subject, actor.LoginBinding), ContainerId = target.Location.ContainerId, InstanceId = target.Location.InstanceId,
                RelativePath = target.Location.Path, TargetProof = target.TargetProof, ExpectedParentVersion = target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                Sha256 = binding.Sha256, PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(upload, default);
            id = Guid.NewGuid(); directoryIds.Add(id); const string directorySubject = "native-recursive-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-30T01:02:03Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(1, 2), new('C', 64), 1000, 1001, new('D', 64));
            var frozen = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), directorySubject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(directorySubject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var directory = new ContainerDirectoryOperation { Id = id, IdempotencyKey = frozen.IdempotencyKey, ActorSubject = directorySubject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = frozen.Owner, ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path,
                PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json), PlanHash = frozen.PlanHash, RequestHash = protector.FingerprintDirectory(frozen),
                CreatedAtUtc = now, ExpiresAtUtc = frozen.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(directory, now.AddMinutes(5), default);
        }
        ContainerPermissionIntent PermissionPeer(string path, string? subjectOverride = null)
        {
            var subject = subjectOverride ?? "native-recursive-chmod-reservation"; var key = Guid.NewGuid();
            var id = ContainerPermissionWorkflowValidation.IntentId(subject, actor.LoginBinding, key);
            var now = ContainerPermissionWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerPermissionCommandDto(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", path), "0600", new('B', 64));
            var binding = new ContainerPermissionRequestBinding(id, key, subject, actor.LoginBinding, actor.TraceId,
                ContainerPermissionWorkflowValidation.Owner(subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = subject, LoginBinding = actor.LoginBinding,
                TraceId = actor.TraceId, OwnerHash = binding.Owner, ContainerId = command.Location.ContainerId, InstanceId = command.Location.InstanceId,
                RelativePath = path, CommandJson = ContainerPermissionWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintPermissionRequest(binding),
                CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        // These raw clones exercise ONLY SQL exclusion, not application MAC acceptance.
        // Every clone lives in an explicit rolled-back transaction with its own jobs.
        async Task ClonePeer(string table, Guid seed)
        {
            Assert.Contains(table, RecursivePermissionFenceTables);
            var id = Guid.NewGuid(); var execution = Guid.NewGuid(); var key = Guid.NewGuid(); var now = clock.GetUtcNow();
            second.OperationJobs.AddRange(new OperationJob { Id = id, ActorSubject = "native-recursive-fence", IdempotencyKey = key.ToString("D"), JobType = "native.permission.fence" },
                new OperationJob { Id = execution, ActorSubject = "native-recursive-fence", IdempotencyKey = execution.ToString("D"), JobType = "native.permission.fence" });
            await second.SaveChangesAsync();
            // Table identifiers are from the fixed local whitelist above; all values are parameters.
            var executionPatch = table is "container_copy_intents" or "container_move_intents" or "container_permission_intents" or "container_ownership_intents" ? " || jsonb_build_object('execution_job_id',@execution)" : "";
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
            Assert.False(await db.ContainerRecursivePermissionIntents.AnyAsync(r => r.PublishBarrier));
            // No root call: actual repositories must reserve ancestor/descendant paths in both directions.
            foreach (var relation in new[] { "same", "ancestor", "descendant" })
            {
                var prefix = "work/ownership-reservation-" + Guid.NewGuid().ToString("N"); var path = prefix + "/item";
                var peerPath = relation == "ancestor" ? prefix : relation == "descendant" ? path + "/child" : path;
                var owner = Row(path); await repo.EnqueueAsync(owner, default);
                var permission = PermissionPeer(peerPath); var modes = new ContainerPermissionRepository(second, protector, clock);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => modes.EnqueueAsync(permission, default));
                Assert.Equal("CONTAINER_PERMISSION_TARGET_BUSY", denied.Message);
                await repo.CancelAsync(actor, owner.Id, default);
                await modes.EnqueueAsync(permission, default);
                var inverseOwner = Row(path);
                denied = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(inverseOwner, default));
                Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY", denied.Message);
                var modeActor = actor with { Subject = permission.ActorSubject };
                await modes.CancelAsync(modeActor, permission.Id, default);
            }
            await PeerSeeds();
            var original = Row(); var ready = await Ready(original);
            await Rejected($"UPDATE container_recursive_permission_intents SET relative_path='elsewhere' WHERE id={ready.Id}", "container_recursive_permission_request_immutable");
            await Rejected($"UPDATE container_recursive_permission_intents SET plan_hash={new string('A', 64)} WHERE id={ready.Id}", "container_recursive_permission_plan_immutable");
            var confirmed = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirmed[0].ExecutionJobId, confirmed[1].ExecutionJobId); Assert.Equal(2, await db.OperationJobs.CountAsync(j => j.Id == ready.Id || j.Id == confirmed[0].ExecutionJobId));
            var apply = Assert.IsType<ContainerRecursivePermissionIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_permission_intents SET root_started_at_utc=NULL WHERE id={apply.Id}", "container_recursive_permission_started_immutable");
            var missingProof = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_recursive_permission_intents SET state='Succeeded',completed_at_utc={clock.GetUtcNow()},publish_barrier=false WHERE id={apply.Id}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, missingProof.SqlState);
            Assert.Equal("ck_container_recursive_permission_result", missingProof.ConstraintName);
            Assert.True(await repo.FinishAsync(apply, "Succeeded", "CONTAINER_RECURSIVE_PERMISSION_COMPLETED", Receipt(apply, "Completed"), default));
            var complete = (await other.FindAsync(ready.Id, default))!; Assert.Equal("Completed", complete.RootState); Assert.Equal(4, complete.CompletedCount); Assert.Equal(2, complete.ChangedCount); Assert.True(complete.HistoryMayExist); Assert.True(complete.HistoryVerified); Assert.Equal(new string('8', 64), complete.HistoryDigest);
            await Rejected($"UPDATE container_recursive_permission_intents SET root_state='Aborted' WHERE id={ready.Id}", "container_recursive_permission_fact_immutable");
            await Rejected($"UPDATE container_recursive_permission_intents SET history_verified=false WHERE id={ready.Id}", "container_recursive_permission_fact_immutable");
            await Rejected($"UPDATE container_recursive_permission_intents SET history_digest=NULL WHERE id={ready.Id}", "container_recursive_permission_fact_immutable");
            await Rejected($"UPDATE container_recursive_permission_intents SET version=version+1 WHERE id={ready.Id}", "container_recursive_permission_terminal_immutable");
            await Rejected($"DELETE FROM container_recursive_permission_intents WHERE id={ready.Id}", "container_recursive_permission_permanent_intent");
            // Eight-way SQL gates: no raw write bypasses the application checks.
            var seeds = new Dictionary<string, Guid> { ["container_file_upload_operations"] = uploadIds.Single(), ["container_directory_operations"] = directoryIds.Single(),
                ["container_copy_intents"] = await db.ContainerCopyIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_move_intents"] = await db.ContainerMoveIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_permission_intents"] = await db.ContainerPermissionIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync(),
                ["container_ownership_intents"] = await db.ContainerOwnershipIntents.Where(r => r.ExecutionJobId != null).Select(r => r.Id).FirstAsync() };
            var blocked = await Applying(await Ready(Row())); var pathHash = FileManagementService.Hash("native-recursive-host:" + blocked.Id);
            await Rejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{blocked.Id},'Legacy')", "FILES_TARGET_BUSY");
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync();
                var error = await Record.ExceptionAsync(() => ClonePeer(pair.Key, pair.Value));
                Assert.NotNull(error); Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
                await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            var uncertain = Receipt(blocked, "Unknown") with { HistoryVerified = true, HistoryDigest = new('7', 64) };
            Assert.True(await repo.FinishAsync(blocked, "Unknown", "CONTAINER_RECURSIVE_PERMISSION_RESULT_UNKNOWN", uncertain, default));
            var unknown = (await other.FindAsync(blocked.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.Equal("Unknown", unknown.RootState); Assert.Equal(4, unknown.UnknownCount); Assert.True(unknown.HistoryVerified);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_permission_intents SET history_may_exist=false WHERE id={unknown.Id}", "container_recursive_permission_fact_immutable");
            var admin = actor with { Subject = "native-current-permission-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(admin, unknown.Id, unknown.Version, uncertain, default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.False(ack.PublishBarrier); Assert.Equal("Unknown", ack.RootState);
            Assert.Equal(4, ack.UnknownCount); Assert.True(ack.HistoryVerified); Assert.Equal(admin.Subject, ack.AcknowledgedBy);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == ack.ExecutionJobId)).State);
            var partial = await Applying(await Ready(Row())); var partialReceipt = Receipt(partial, "Partial");
            Assert.True(await repo.FinishAsync(partial, "Partial", "CONTAINER_RECURSIVE_PERMISSION_PARTIAL_REQUIRES_ACKNOWLEDGEMENT", partialReceipt, default));
            var paused = (await other.FindAsync(partial.Id, default))!;
            Assert.Equal("Partial", paused.State); Assert.Null(paused.CompletedAtUtc); Assert.True(paused.PublishBarrier);
            Assert.Equal(1, paused.ChangedCount); Assert.Equal(3, paused.NotStartedCount);
            Assert.Equal(partialReceipt, ContainerRecursivePermissionWorkflowValidation.ReadReceipt(paused));
            Assert.Equal("Running", (await second.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == paused.ExecutionJobId)).State);
            Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_permission_intents SET changed_count=0 WHERE id={paused.Id}", "container_recursive_permission_fact_immutable");
            await Rejected($"UPDATE container_recursive_permission_intents SET state='Pending' WHERE id={paused.Id}", "container_recursive_permission_resolution_required");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.AcknowledgeAsync(admin, paused.Id, paused.Version, Receipt(partial, "Aborted"), default));
            await new JobRepository(second).RequestCancellationAsync(paused.ExecutionJobId!.Value, actor.Subject, default);
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM outbox_messages WHERE message_type='ContainerRecursivePermissionCancellationRequested' AND payload_json->>'jobId'={paused.ExecutionJobId.Value.ToString("D")}) AS \"Value\"").SingleAsync());
            await other.AcknowledgeAsync(admin, paused.Id, paused.Version, partialReceipt with { HistoryAvailable = false, Replayed = true }, default);
            var resolvedPartial = (await repo.FindAsync(paused.Id, default))!;
            Assert.Equal("Acknowledged", resolvedPartial.State); Assert.Equal("Partial", resolvedPartial.RootState); Assert.Equal(1, resolvedPartial.ChangedCount);
            Assert.True(resolvedPartial.HistoryVerified); Assert.False(resolvedPartial.HistoryAvailable); Assert.False(resolvedPartial.PublishBarrier);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == resolvedPartial.ExecutionJobId)).State);
            // Original natural lease expires on the synthetic test clock, not SQL edits.
            var abandoned = await Applying(await Ready(Row())); var oldToken = abandoned.LeaseToken;
            clock.Offset = TimeSpan.FromSeconds(91); var reclaimed = Assert.IsType<ContainerRecursivePermissionIntent>(await other.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(abandoned.Id, reclaimed.Id); Assert.NotEqual(oldToken, reclaimed.LeaseToken); Assert.NotNull(reclaimed.RootStartedAtUtc); Assert.True(reclaimed.PublishBarrier);
            Assert.False(await repo.RenewAsync(abandoned, clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost, await other.StartedAsync(reclaimed, clock.GetUtcNow(), default));
            Assert.True(await other.FinishAsync(reclaimed, "Succeeded", "CONTAINER_RECURSIVE_PERMISSION_COMPLETED", Receipt(reclaimed, "Completed"), default));
            clock.Offset = TimeSpan.Zero;
            var inverse = await Pending(await Ready(Row()));
            foreach (var pair in seeds)
            {
                await using var transaction = await second.Database.BeginTransactionAsync(); await ClonePeer(pair.Key, pair.Value);
                var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_recursive_permission_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}"));
                Assert.Equal("CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY", error.MessageText); await transaction.RollbackAsync(); second.ChangeTracker.Clear();
            }
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({pathHash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await Rejected($"UPDATE container_recursive_permission_intents SET state='Applying',root_started_at_utc={clock.GetUtcNow()},publish_barrier=true WHERE id={inverse.Id}", "CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY");
            await second.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM file_path_locks WHERE operation_id={inverse.Id}");
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_RECURSIVE_PERMISSION_CANCELED", Receipt(inverse, "Aborted"), default));
            var canceled = (await other.FindAsync(inverse.Id, default))!; Assert.Equal("Aborted", canceled.RootState); Assert.Equal(0, canceled.ChangedCount); Assert.False(canceled.HistoryMayExist); Assert.Null(canceled.HistoryDigest);
            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            await repo.CancelAsync(actor, expiredReady.Id, default); clock.Offset = TimeSpan.Zero;
            // Audit failure is part of the same SQL transaction as the intent/jobs/outbox.
            var rejected = Row();
            await second.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wdv_recursive_permission_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN RAISE EXCEPTION 'wdv_recursive_permission_audit_failed' USING ERRCODE='23514'; END $f$;
                CREATE TRIGGER wdv_recursive_permission_audit_reject BEFORE INSERT ON audit_events
                  FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.recursive-permission.%') EXECUTE FUNCTION wdv_recursive_permission_audit_reject();
                """);
            try { await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(rejected, default)); }
            finally { await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_recursive_permission_audit_reject ON audit_events; DROP FUNCTION wdv_recursive_permission_audit_reject();"); }
            Assert.False(await second.ContainerRecursivePermissionIntents.AnyAsync(r => r.Id == rejected.Id)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == rejected.Id));
            var waiting = new List<ContainerRecursivePermissionIntent>();
            for (var n = 0; n < 4; n++) { var row = Row(); await repo.EnqueueAsync(row, default); waiting.Add(row); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default));
            var peerBudget = PermissionPeer("work/nonoverlap-budget-" + Guid.NewGuid().ToString("N"), actor.Subject);
            var budgetError = await Assert.ThrowsAsync<InvalidOperationException>(() => new ContainerPermissionRepository(second, protector, clock).EnqueueAsync(peerBudget, default));
            Assert.Equal("FILES_OPERATION_BUDGET_EXHAUSTED", budgetError.Message);

            foreach (var row in waiting) await repo.CancelAsync(actor, row.Id, default);
            var lockRejected = Row();
            await using (var holding = await second.Database.BeginTransactionAsync())
            {
                await second.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341)");
                var timeout = await Record.ExceptionAsync(() => repo.EnqueueAsync(lockRejected, default));
                Assert.NotNull(timeout); Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(timeout.GetBaseException()).SqlState);
                await holding.RollbackAsync(); second.ChangeTracker.Clear();
            }
            Assert.False(await db.ContainerRecursivePermissionIntents.AnyAsync(r => r.Id == lockRejected.Id));
            Assert.False(await db.OperationJobs.AnyAsync(j => j.Id == lockRejected.Id));
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261030143305_ContainerOwnershipIntents"));
            Assert.Equal("container_recursive_permission_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerRecursivePermissionIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));
            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerRecursivePermissionIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-recursive-permission-persistent-jobs-facts-and-eight-way-fences");
        }
        finally
        {
            // Only exact synthetic legacy fixtures are deleted; permanent new rows remain until isolated DB removal.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class RecursivePermissionClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
