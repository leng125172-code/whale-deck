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
    /// <summary>Real PostgreSQL rows, transactions and triggers; synthetic receipts, no Agent/Root/UI claim.</summary>
    private static async Task ContainerCopyBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var clock = new CopyClock();
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var repo = new ContainerCopyRepository(db, protector, clock); var other = new ContainerCopyRepository(second, protector, clock);
        var actor = new TerminalActor("native-copy-admin", null, [], new('A', 64), "native-copy-trace", null);
        var ids = new List<Guid>(); var directoryIds = new List<Guid>(); var uploadIds = new List<Guid>();
        ContainerCopyIntent Row(string? source = null, string? target = null, string? container = null)
        {
            var key = Guid.NewGuid(); var id = ContainerCopyWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, key); ids.Add(id);
            var now = ContainerCopyWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerCopyCommandDto(new(container ?? new('c', 64), "777:2026-10-28T01:02:03.123456789Z", source ?? "work/source-" + id.ToString("N")), target ?? "work/copy-" + id.ToString("N"), new('B', 64));
            var binding = new ContainerCopyRequestBinding(id, key, actor.Subject, actor.LoginBinding, actor.TraceId, ContainerCopyWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId, SourcePath = command.Source.Path, TargetPath = command.TargetPath,
                CommandJson = ContainerCopyWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintCopyRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerCopyPlanDto Plan(ContainerCopyIntent r, bool tree = false)
        {
            var source = new FileObjectIdentityDto(ulong.MaxValue, 101, tree ? 16872U : 33184U, 1000, 1001, tree ? 2U : 1U, tree ? 4096 : 7, 1700000000123456789);
            var p = new ContainerCopyPlanDto(1, ContainerCopyWorkflowValidation.ReadCommand(r), tree ? "Directory" : "File", new('C', 64), source, 1700000000987654321,
                new(ulong.MaxValue, 33), new(44, 55), new('D', 64), 1000, 1001, new('E', 64), null, null);
            var host = ContainerCopyValidation.HostVersion(p);
            return tree ? p with { TreePlan = new(new(new(ContainerCopyValidation.SourceRoot, r.SourcePath), host, new(ContainerCopyValidation.TargetRoot, r.TargetPath)), new('A', 64), 3, 7, p.TargetParent, new('B', 64), new('C', 64), new('D', 64)) } :
                p with { FilePlan = new(new("copy", new(ContainerCopyValidation.SourceRoot, r.SourcePath), new(ContainerCopyValidation.TargetRoot, r.TargetPath), host, ""), source, p.SourceParent, p.TargetParent, 7, new('F', 64), new('A', 64)) };
        }
        ContainerCopyReceiptDto Receipt(ContainerCopyIntent r, string state, bool current = true) => new(r.ExecutionJobId!.Value, r.ContainerId, r.InstanceId,
            ContainerCopyWorkflowValidation.ReadPlan(r).Kind, ContainerCopyWorkflowValidation.ReadPlan(r).Fingerprint, ContainerCopyWorkflowValidation.ReadPlan(r).TargetProof,
            state, state == "Completed" ? new('F', 64) : "", state == "Completed" && current, state == "Unknown", false);
        ContainerCopyConfirmation Claims(ContainerCopyIntent r) => new(r.Id, r.ActorSubject, r.LoginBinding, r.PlanHash!, r.ReadyExpiresAtUtc!.Value);
        async Task<ContainerCopyIntent> Ready(ContainerCopyIntent r, bool tree = false)
        {
            await repo.EnqueueAsync(r, default);
            var claim = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(r.Id, claim.Id);
            Assert.True(await repo.MarkPlanningAsync(claim, default)); await repo.FinishPlanningAsync(claim, Plan(r, tree), "CONTAINER_COPY_READY", default);
            return (await repo.FindAsync(r.Id, default))!;
        }
        async Task<ContainerCopyIntent> Applying(ContainerCopyIntent ready)
        {
            await repo.ConfirmAsync(actor, Claims(ready), ready.Version, default);
            var claim = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(ready.Id, claim.Id);
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claim, clock.GetUtcNow(), default)); return claim;
        }
        async Task RawRejected(FormattableString sql, string message)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, error.MessageText);
        }
        async Task<ContainerFileUploadOperation> Upload()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id);
            const string subject = "native-copy-upload-peer";
            var target = new ContainerFileUploadTargetDto(new(new('d', 64), "888:2026-10-28T01:02:03.123456789Z", "work/upload-" + id.ToString("N")), new('B', 64), new('C', 64), "ABSENT", 0);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, target, 0,
                Convert.ToHexString(SHA256.HashData([])), new('F', 64), now, now.AddMinutes(15));
            var row = new ContainerFileUploadOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = ContainerFileUploadIntentValidation.Owner(subject, actor.LoginBinding), ContainerId = target.Location.ContainerId, InstanceId = target.Location.InstanceId,
                RelativePath = target.Location.Path, TargetProof = target.TargetProof, ExpectedParentVersion = target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                Sha256 = binding.Sha256, PlanHash = binding.PlanHash, RequestHash = protector.FingerprintContainerUpload(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(row, default); return row;
        }
        async Task<ContainerDirectoryOperation> Directory()
        {
            var now = ContainerDirectoryWorkflowValidation.Time(clock.GetUtcNow()); var id = Guid.NewGuid(); directoryIds.Add(id); const string subject = "native-copy-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-28T01:02:03.123456789Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(11, 22), new('C', 64), 1000, 1000, new('D', 64));
            var binding = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(subject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var row = new ContainerDirectoryOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path, PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json),
                PlanHash = binding.PlanHash, RequestHash = protector.FingerprintDirectory(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(row, now.AddMinutes(5), default); return row;
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.FilePathLocks.ToArrayAsync());
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier));
            var original = Row(); var duplicate = await Task.WhenAll(repo.EnqueueAsync(original, default), other.EnqueueAsync(original, default));
            Assert.All(duplicate, r => Assert.Equal(original.Id, r.Id)); Assert.Equal(1, await db.ContainerCopyIntents.CountAsync(r => r.Id == original.Id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(r => r.JobId == original.Id && r.Action == "containers.files.copy.plan-accepted"));
            var conflict = Row(original.TargetPath + "/nested");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.EnqueueAsync(conflict, default)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == conflict.Id));
            var firstLease = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(original.Id, firstLease.Id);
            Assert.Null(await other.ClaimAsync(clock.GetUtcNow(), default));
            Assert.True(await repo.MarkPlanningAsync(firstLease, default));
            await repo.FinishPlanningAsync(firstLease, Plan(original), "CONTAINER_COPY_READY", default);
            var ready = (await repo.FindAsync(original.Id, default))!; Assert.Equal("Ready", ready.State);
            Assert.Null(await other.ClaimAsync(clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET trace_id='changed' WHERE id={ready.Id}", "container_copy_request_immutable");
            await RawRejected($"UPDATE container_copy_intents SET plan_hash=repeat('F',64) WHERE id={ready.Id}", "container_copy_plan_immutable");
            var confirms = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirms[0].ExecutionJobId, confirms[1].ExecutionJobId); var execution = confirms[0].ExecutionJobId!.Value;
            Assert.Equal(1, await db.OperationJobs.CountAsync(j => j.Id == execution));
            await RawRejected($"UPDATE container_copy_intents SET execution_expires_at_utc=execution_expires_at_utc+INTERVAL '1 minute' WHERE id={ready.Id}", "container_copy_execution_immutable");
            var apply = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            var hash = FileManagementService.Hash("native-copy-host-path:" + original.Id);
            await RawRejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{original.Id},'Legacy')", "FILES_TARGET_BUSY");
            var constraint = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_copy_intents SET publish_barrier=false WHERE id={original.Id}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, constraint.SqlState);
            Assert.True(await repo.FinishAsync(apply, "Unknown", "CONTAINER_COPY_RESULT_UNKNOWN", Receipt(apply, "Unknown"), default));
            var unknown = (await other.FindAsync(original.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.True(unknown.PrivateMayExist); Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            var currentAdmin = actor with { Subject = "native-current-copy-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(currentAdmin, unknown.Id, unknown.Version, Receipt(unknown, "Unknown"), default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.True(ack.PrivateMayExist); Assert.False(ack.PublishBarrier);
            Assert.Equal(actor.Subject, ack.ActorSubject); Assert.Equal(currentAdmin.Subject, ack.AcknowledgedBy); Assert.NotNull(ack.AcknowledgementJson);
            await RawRejected($"UPDATE container_copy_intents SET version=version+1 WHERE id={ack.Id}", "container_copy_terminal_immutable");
            await RawRejected($"DELETE FROM container_copy_intents WHERE id={ack.Id}", "container_copy_permanent_intent");
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AcknowledgeAsync(currentAdmin, ack.Id, unknown.Version, Receipt(unknown, "Unknown"), default));
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == execution)).State);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.JobId == execution && a.ActorSubject == currentAdmin.Subject && a.Action == "containers.files.copy.unknown-acknowledged"));

            // Four-way, DB-level exclusion, including peers whose SQL does not know this new table.
            var fenceReady = await Ready(Row()); var fence = await Applying(fenceReady);
            var peerUpload = await Upload(); var peerDirectory = await Directory();
            await RawRejected($"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={peerUpload.Id}", "CONTAINER_UPLOAD_TARGET_BUSY");
            await RawRejected($"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerDirectory.Id}", "CONTAINER_DIRECTORY_TARGET_BUSY");
            var waiting = await Ready(Row()); await repo.ConfirmAsync(actor, Claims(waiting), waiting.Version, default);
            var waitingClaim = Assert.IsType<ContainerCopyIntent>(await other.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(waiting.Id, waitingClaim.Id);
            Assert.Equal(ContainerFileUploadStartResult.Busy, await other.StartedAsync(waitingClaim, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={waiting.Id}", "CONTAINER_COPY_TARGET_BUSY");
            await repo.CancelAsync(actor, waiting.Id, default); Assert.True(await other.FinishAsync(waitingClaim, "Canceled", "CONTAINER_COPY_CANCELED", Receipt(waitingClaim, "Aborted"), default));
            Assert.True(await repo.FinishAsync(fence, "Failed", "CONTAINER_COPY_INTERRUPTED", Receipt(fence, "Aborted"), default));

            // Inverse gates: already held host/other-container protection blocks a copy in both repository and raw SQL.
            var inverseReady = await Ready(Row()); await repo.ConfirmAsync(actor, Claims(inverseReady), inverseReady.Version, default);
            var inverse = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_COPY_TARGET_BUSY");
            await second.FilePathLocks.Where(r => r.PathHash == hash).ExecuteDeleteAsync();
            await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={peerUpload.Id}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_COPY_TARGET_BUSY");
            await second.ContainerFileUploadOperations.Where(r => r.Id == peerUpload.Id).ExecuteDeleteAsync();
            await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerDirectory.Id}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_COPY_TARGET_BUSY");
            await second.ContainerDirectoryOperations.Where(r => r.Id == peerDirectory.Id).ExecuteDeleteAsync();
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_COPY_CANCELED", Receipt(inverse, "Aborted"), default));

            foreach (var tree in new[] { false, true })
            {
                var good = await Ready(Row(), tree); var claimed = await Applying(good);
                Assert.True(await repo.FinishAsync(claimed, "Succeeded", "CONTAINER_COPY_COMPLETED", Receipt(claimed, "Completed"), default));
                var terminal = (await repo.FindAsync(good.Id, default))!; Assert.False(terminal.PublishBarrier); Assert.Equal(new string('F', 64), terminal.PublishedVersion);
                Assert.True(ContainerCopyWorkflowValidation.HasValidBinding(terminal, protector));
                Assert.Equal(terminal.ExecutionJobId, (await other.ConfirmAsync(actor, Claims(terminal), good.Version, default)).ExecutionJobId);
                Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == good.Id)).State);
                Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == terminal.ExecutionJobId)).State);
                var pending = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_messages WHERE payload_json->>'jobId' IN ({good.Id.ToString("D")},{terminal.ExecutionJobId!.Value.ToString("D")}) AND processed_at_utc IS NULL").SingleAsync(); Assert.Equal(0, pending);
            }
            var canceling = Row(); await repo.EnqueueAsync(canceling, default); var scan = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await repo.MarkPlanningAsync(scan, default); await other.CancelAsync(actor, canceling.Id, default);
            await repo.FinishPlanningAsync(scan, Plan(canceling), "CONTAINER_COPY_READY", default);
            Assert.Equal("Canceled", (await repo.FindAsync(canceling.Id, default))!.State); Assert.Null((await repo.FindAsync(canceling.Id, default))!.PlanJson);
            var canceledReady = await Ready(Row()); await other.CancelAsync(actor, canceledReady.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(canceledReady), canceledReady.Version, default));
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == canceledReady.Id)).State);

            var restarted = await Ready(Row()); var started = await Applying(restarted); await repo.DeferAsync(started, default);
            var later = Assert.IsType<ContainerCopyIntent>(await other.ClaimAsync(clock.GetUtcNow().AddSeconds(7), default)); Assert.Equal(started.Id, later.Id);
            Assert.NotNull(later.RootStartedAtUtc); Assert.Equal(ContainerFileUploadStartResult.LeaseLost, await other.StartedAsync(later, clock.GetUtcNow(), default));
            Assert.True(await other.FinishAsync(later, "Failed", "CONTAINER_COPY_INTERRUPTED", Receipt(later, "Aborted"), default));

            var leaseReady = await Ready(Row()); var expiredLease = await Applying(leaseReady); clock.Offset = TimeSpan.FromSeconds(91);
            var recovered = Assert.IsType<ContainerCopyIntent>(await other.ClaimAsync(clock.GetUtcNow(), default)); Assert.NotEqual(expiredLease.LeaseToken, recovered.LeaseToken);
            Assert.False(await repo.FinishAsync(expiredLease, "Succeeded", "CONTAINER_COPY_COMPLETED", Receipt(expiredLease, "Completed"), default));
            Assert.True(await other.FinishAsync(recovered, "Failed", "CONTAINER_COPY_INTERRUPTED", Receipt(recovered, "Aborted"), default)); clock.Offset = TimeSpan.Zero;

            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            var expireClaim = Assert.IsType<ContainerCopyIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await repo.FinishPlanningAsync(expireClaim, null, "CONTAINER_COPY_PLAN_EXPIRED", default); Assert.Equal("Expired", (await repo.FindAsync(expiredReady.Id, default))!.State); clock.Offset = TimeSpan.Zero;

            var budget = new List<ContainerCopyIntent>();
            for (var i = 0; i < 4; i++) { var r = Row(); await repo.EnqueueAsync(r, default); budget.Add(r); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default)); Assert.False(await db.OperationJobs.AnyAsync(j => j.Id == over.Id));
            foreach (var r in budget) await repo.CancelAsync(actor, r.Id, default);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261027153646_ContainerDirectoryIntents"));
            Assert.Equal("container_copy_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerCopyIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));
            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerCopyIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-copy-persistent-dual-jobs-fences-and-recovery");
        }
        finally
        {
            // These two pre-existing entity types permit precise synthetic fixture cleanup.
            // New permanent copy intents are deliberately retained until the isolated database is dropped.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class CopyClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
