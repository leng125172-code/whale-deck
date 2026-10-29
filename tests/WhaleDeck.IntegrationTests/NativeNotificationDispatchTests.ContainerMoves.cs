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
    private static async Task ContainerMoveBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options); await using var copyDb = new PlatformDbContext(options);
        var keyPath = Path.GetTempFileName(); await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32));
        var clock = new MoveClock();
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyPath }).Build());
        var repo = new ContainerMoveRepository(db, protector, clock); var other = new ContainerMoveRepository(second, protector, clock);
        var peerCopies = new ContainerCopyRepository(copyDb, protector, clock);
        var actor = new TerminalActor("native-move-admin", null, [], new('A', 64), "native-move-trace", null);
        var ids = new List<Guid>(); var directoryIds = new List<Guid>(); var uploadIds = new List<Guid>();
        ContainerMoveIntent Row(string? source = null, string? target = null, string? container = null)
        {
            var key = Guid.NewGuid(); var id = ContainerMoveWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, key); ids.Add(id);
            var now = ContainerMoveWorkflowValidation.Time(clock.GetUtcNow());
            var command = new ContainerMoveCommandDto(new(container ?? new('c', 64), "777:2026-10-28T01:02:03.123456789Z", source ?? "work/source-" + id.ToString("N")), target ?? "work/move-" + id.ToString("N"), new('B', 64));
            var binding = new ContainerMoveRequestBinding(id, key, actor.Subject, actor.LoginBinding, actor.TraceId, ContainerMoveWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = key, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId, SourcePath = command.Source.Path, TargetPath = command.TargetPath,
                CommandJson = ContainerMoveWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintContainerMoveRequest(binding), CreatedAtUtc = now,
                PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerMovePlanDto Plan(ContainerMoveIntent r, bool tree = false)
        {
            var source = new FileObjectIdentityDto(ulong.MaxValue, 101, tree ? 16872U : 33184U, 1000, 1001, tree ? 2U : 1U, tree ? 4096 : 7, 1700000000123456789);
            var p = new ContainerMovePlanDto(1, ContainerMoveWorkflowValidation.ReadCommand(r), "Move", tree ? "Directory" : "File", new('C', 64), source, 1700000000987654321,
                new(ulong.MaxValue, 33), new(44, 55), new('D', 64), 1000, 1001, new('E', 64), null, null);
            var host = ContainerMoveValidation.HostVersion(p);
            return tree ? p with { TreePlan = new(new(new(new(ContainerMoveValidation.SourceRoot, r.SourcePath), host, new(ContainerMoveValidation.TargetRoot, r.TargetPath)), new('A', 64), 3, 7, p.TargetParent, new('B', 64), new('C', 64), new('D', 64)), p.SourceParent, new('E', 64)) } :
                p with { FilePlan = new(new("move", new(ContainerMoveValidation.SourceRoot, r.SourcePath), new(ContainerMoveValidation.TargetRoot, r.TargetPath), host, ""), source, p.SourceParent, p.TargetParent, 7, new('F', 64), new('A', 64)) };
        }
        ContainerMoveReceiptDto Receipt(ContainerMoveIntent r, string state, bool current = true) => new(r.ExecutionJobId!.Value, r.ContainerId, r.InstanceId,
            "Move", ContainerMoveWorkflowValidation.ReadPlan(r).Kind, ContainerMoveWorkflowValidation.ReadPlan(r).Fingerprint, ContainerMoveWorkflowValidation.ReadPlan(r).TargetProof,
            state, state == "Completed" ? new('F', 64) : "", state == "Completed" && current, state == "Unknown", false,
            state == "Unknown" ? null : state == "Completed", state == "Unknown" ? null : state == "Completed", state == "Unknown" ? null : state == "Completed");
        ContainerMoveConfirmation Claims(ContainerMoveIntent r) => new(r.Id, r.ActorSubject, r.LoginBinding, r.PlanHash!, r.ReadyExpiresAtUtc!.Value);
        async Task<ContainerMoveIntent> Ready(ContainerMoveIntent r, bool tree = false)
        {
            await repo.EnqueueAsync(r, default);
            var claim = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(r.Id, claim.Id);
            Assert.True(await repo.MarkPlanningAsync(claim, default)); await repo.FinishPlanningAsync(claim, Plan(r, tree), "CONTAINER_MOVE_READY", default);
            return (await repo.FindAsync(r.Id, default))!;
        }
        async Task<ContainerMoveIntent> Applying(ContainerMoveIntent ready)
        {
            await repo.ConfirmAsync(actor, Claims(ready), ready.Version, default);
            var claim = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(ready.Id, claim.Id);
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(claim, clock.GetUtcNow(), default)); return claim;
        }
        async Task RawRejected(FormattableString sql, string message)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(sql)); Assert.Equal(message, error.MessageText);
        }
        async Task<ContainerFileUploadOperation> Upload()
        {
            var now = ContainerFileUploadIntentValidation.CanonicalTime(clock.GetUtcNow()); var id = Guid.NewGuid(); uploadIds.Add(id);
            const string subject = "native-move-upload-peer";
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
            var now = ContainerDirectoryWorkflowValidation.Time(clock.GetUtcNow()); var id = Guid.NewGuid(); directoryIds.Add(id); const string subject = "native-move-directory-peer";
            var plan = new ContainerDirectoryPlanDto(1, new(new(new('e', 64), "999:2026-10-28T01:02:03.123456789Z", "work/directory-" + id.ToString("N")), "0750"), new('B', 64), new(11, 22), new('C', 64), 1000, 1000, new('D', 64));
            var binding = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), subject, actor.LoginBinding, actor.TraceId, ContainerDirectoryWorkflowValidation.Owner(subject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            var row = new ContainerDirectoryOperation { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path, PlanJson = System.Text.Json.JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json),
                PlanHash = binding.PlanHash, RequestHash = protector.FingerprintDirectory(binding), CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
            await new ContainerDirectoryRepository(second, protector).EnqueueAsync(row, now.AddMinutes(5), default); return row;
        }
        async Task<ContainerCopyIntent> CopyPeer()
        {
            const string subject = "native-move-copy-peer"; var key = Guid.NewGuid(); var now = ContainerCopyWorkflowValidation.Time(clock.GetUtcNow());
            var id = ContainerCopyWorkflowValidation.IntentId(subject, actor.LoginBinding, key);
            var command = new ContainerCopyCommandDto(new(new('f', 64), "555:2026-10-29T01:02:03.123456789Z", "work/source-" + id.ToString("N")), "work/target-" + id.ToString("N"), new('B', 64));
            var binding = new ContainerCopyRequestBinding(id, key, subject, actor.LoginBinding, actor.TraceId, ContainerCopyWorkflowValidation.Owner(subject, actor.LoginBinding), command, now, now.AddMinutes(15));
            var row = new ContainerCopyIntent { Id = id, IdempotencyKey = key, ActorSubject = subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = binding.Owner,
                ContainerId = command.Source.ContainerId, InstanceId = command.Source.InstanceId, SourcePath = command.Source.Path, TargetPath = command.TargetPath,
                CommandJson = ContainerCopyWorkflowValidation.Serialize(command), RequestHash = protector.FingerprintCopyRequest(binding), CreatedAtUtc = now, PlanningExpiresAtUtc = binding.PlanningExpiresAtUtc, NextAttemptAtUtc = now };
            await peerCopies.EnqueueAsync(row, default);
            var source = new FileObjectIdentityDto(1, 2, 33184, 1000, 1000, 1, 7, 1700000000123456789);
            var plan = new ContainerCopyPlanDto(1, command, "File", new('C', 64), source, 1700000000987654321, new(1, 3), new(1, 4), new('D', 64), 1000, 1000, new('E', 64), null, null);
            plan = plan with { FilePlan = new(new("copy", new(ContainerCopyValidation.SourceRoot, row.SourcePath), new(ContainerCopyValidation.TargetRoot, row.TargetPath), ContainerCopyValidation.HostVersion(plan), ""), source, plan.SourceParent, plan.TargetParent, 7, new('F', 64), new('A', 64)) };
            var planning = Assert.IsType<ContainerCopyIntent>(await peerCopies.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(id, planning.Id);
            Assert.True(await peerCopies.MarkPlanningAsync(planning, default)); await peerCopies.FinishPlanningAsync(planning, plan, "CONTAINER_COPY_READY", default);
            var ready = (await peerCopies.FindAsync(id, default))!;
            await peerCopies.ConfirmAsync(actor with { Subject = subject }, new(id, subject, actor.LoginBinding, ready.PlanHash!, ready.ReadyExpiresAtUtc!.Value), ready.Version, default);
            var pending = Assert.IsType<ContainerCopyIntent>(await peerCopies.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(id, pending.Id); return pending;
        }
        static ContainerCopyReceiptDto CopyAborted(ContainerCopyIntent row)
        {
            var plan = ContainerCopyWorkflowValidation.ReadPlan(row);
            return new(row.ExecutionJobId!.Value, row.ContainerId, row.InstanceId, "File", plan.Fingerprint, plan.TargetProof, "Aborted", "", false, false, false);
        }
        try
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.FilePathLocks.ToArrayAsync());
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(r => r.PublishBarrier));
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.PublishBarrier));
            var original = Row(); var duplicate = await Task.WhenAll(repo.EnqueueAsync(original, default), other.EnqueueAsync(original, default));
            Assert.All(duplicate, r => Assert.Equal(original.Id, r.Id)); Assert.Equal(1, await db.ContainerMoveIntents.CountAsync(r => r.Id == original.Id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(r => r.JobId == original.Id && r.Action == "containers.files.move.plan-accepted"));
            var conflict = Row(original.TargetPath + "/nested");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.EnqueueAsync(conflict, default)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == conflict.Id));
            var sourceConflict = Row(original.SourcePath + "/nested");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.EnqueueAsync(sourceConflict, default)); Assert.False(await second.OperationJobs.AnyAsync(j => j.Id == sourceConflict.Id));
            var firstLease = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(original.Id, firstLease.Id);
            Assert.Null(await other.ClaimAsync(clock.GetUtcNow(), default));
            Assert.True(await repo.MarkPlanningAsync(firstLease, default));
            await repo.FinishPlanningAsync(firstLease, Plan(original), "CONTAINER_MOVE_READY", default);
            var ready = (await repo.FindAsync(original.Id, default))!; Assert.Equal("Ready", ready.State);
            Assert.Null(await other.ClaimAsync(clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET trace_id='changed' WHERE id={ready.Id}", "container_move_request_immutable");
            await RawRejected($"UPDATE container_move_intents SET plan_hash=repeat('F',64) WHERE id={ready.Id}", "container_move_plan_immutable");
            var confirms = await Task.WhenAll(repo.ConfirmAsync(actor, Claims(ready), ready.Version, default), other.ConfirmAsync(actor, Claims(ready), ready.Version, default));
            Assert.Equal(confirms[0].ExecutionJobId, confirms[1].ExecutionJobId); var execution = confirms[0].ExecutionJobId!.Value;
            Assert.Equal(1, await db.OperationJobs.CountAsync(j => j.Id == execution));
            await RawRejected($"UPDATE container_move_intents SET execution_expires_at_utc=execution_expires_at_utc+INTERVAL '1 minute' WHERE id={ready.Id}", "container_move_execution_immutable");
            var apply = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repo.StartedAsync(apply, clock.GetUtcNow(), default));
            var hash = FileManagementService.Hash("native-move-host-path:" + original.Id);
            await RawRejected($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{original.Id},'Legacy')", "FILES_TARGET_BUSY");
            var constraint = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_move_intents SET publish_barrier=false WHERE id={original.Id}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, constraint.SqlState);
            Assert.True(await repo.FinishAsync(apply, "Unknown", "CONTAINER_MOVE_RESULT_UNKNOWN", Receipt(apply, "Unknown") with { DestinationPublished = true }, default));
            var unknown = (await other.FindAsync(original.Id, default))!; Assert.True(unknown.PublishBarrier); Assert.True(unknown.PrivateMayExist); Assert.Null(await repo.ClaimAsync(clock.GetUtcNow(), default));
            Assert.True(unknown.DestinationPublished); Assert.Null(unknown.SourceRemoved); Assert.Null(unknown.SourceRetained);
            await RawRejected($"UPDATE container_move_intents SET destination_published=false WHERE id={unknown.Id}", "container_move_fact_immutable");
            await RawRejected($"UPDATE container_move_intents SET destination_published=NULL WHERE id={unknown.Id}", "container_move_fact_immutable");
            var currentAdmin = actor with { Subject = "native-current-move-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(currentAdmin, unknown.Id, unknown.Version, Receipt(unknown, "Unknown"), default);
            var ack = (await repo.FindAsync(unknown.Id, default))!; Assert.Equal("Acknowledged", ack.State); Assert.True(ack.PrivateMayExist); Assert.False(ack.PublishBarrier);
            Assert.True(ack.DestinationPublished); Assert.Null(ack.SourceRemoved); Assert.Null(ack.SourceRetained);
            Assert.Equal(actor.Subject, ack.ActorSubject); Assert.Equal(currentAdmin.Subject, ack.AcknowledgedBy); Assert.NotNull(ack.AcknowledgementJson);
            await RawRejected($"UPDATE container_move_intents SET version=version+1 WHERE id={ack.Id}", "container_move_terminal_immutable");
            await RawRejected($"DELETE FROM container_move_intents WHERE id={ack.Id}", "container_move_permanent_intent");
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AcknowledgeAsync(currentAdmin, ack.Id, unknown.Version, Receipt(unknown, "Unknown"), default));
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == execution)).State);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.JobId == execution && a.ActorSubject == currentAdmin.Subject && a.Action == "containers.files.move.unknown-acknowledged"));

            // Five-way DB exclusion, including older peers unaware of moves.
            var fenceReady = await Ready(Row()); var fence = await Applying(fenceReady);
            var peerUpload = await Upload(); var peerDirectory = await Directory();
            var peerCopy = await CopyPeer();
            Assert.Equal(ContainerFileUploadStartResult.Busy, await peerCopies.StartedAsync(peerCopy, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerCopy.Id}", "CONTAINER_COPY_TARGET_BUSY");
            await RawRejected($"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={peerUpload.Id}", "CONTAINER_UPLOAD_TARGET_BUSY");
            await RawRejected($"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerDirectory.Id}", "CONTAINER_DIRECTORY_TARGET_BUSY");
            var waiting = await Ready(Row()); await repo.ConfirmAsync(actor, Claims(waiting), waiting.Version, default);
            var waitingClaim = Assert.IsType<ContainerMoveIntent>(await other.ClaimAsync(clock.GetUtcNow(), default)); Assert.Equal(waiting.Id, waitingClaim.Id);
            Assert.Equal(ContainerFileUploadStartResult.Busy, await other.StartedAsync(waitingClaim, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={waiting.Id}", "CONTAINER_MOVE_TARGET_BUSY");
            await repo.CancelAsync(actor, waiting.Id, default); Assert.True(await other.FinishAsync(waitingClaim, "Canceled", "CONTAINER_MOVE_CANCELED", Receipt(waitingClaim, "Aborted"), default));
            Assert.True(await repo.FinishAsync(fence, "Failed", "CONTAINER_MOVE_INTERRUPTED", Receipt(fence, "Aborted"), default));

            // Inverse gates: already held host/other-container protection blocks a move in both repository and raw SQL.
            var inverseReady = await Ready(Row()); await repo.ConfirmAsync(actor, Claims(inverseReady), inverseReady.Version, default);
            var inverse = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await copyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_copy_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerCopy.Id}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_MOVE_TARGET_BUSY");
            Assert.True(await peerCopies.FinishAsync(peerCopy, "Failed", "CONTAINER_COPY_INTERRUPTED", CopyAborted(peerCopy), default));
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{inverse.Id},'Legacy')");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_MOVE_TARGET_BUSY");
            await second.FilePathLocks.Where(r => r.PathHash == hash).ExecuteDeleteAsync();
            await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={peerUpload.Id}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_MOVE_TARGET_BUSY");
            await second.ContainerFileUploadOperations.Where(r => r.Id == peerUpload.Id).ExecuteDeleteAsync();
            await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={peerDirectory.Id}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repo.StartedAsync(inverse, clock.GetUtcNow(), default));
            await RawRejected($"UPDATE container_move_intents SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={inverse.Id}", "CONTAINER_MOVE_TARGET_BUSY");
            await second.ContainerDirectoryOperations.Where(r => r.Id == peerDirectory.Id).ExecuteDeleteAsync();
            await repo.CancelAsync(actor, inverse.Id, default); Assert.True(await repo.FinishAsync(inverse, "Canceled", "CONTAINER_MOVE_CANCELED", Receipt(inverse, "Aborted"), default));

            foreach (var tree in new[] { false, true })
            {
                var good = await Ready(Row(), tree); var claimed = await Applying(good);
                await Assert.ThrowsAsync<InvalidOperationException>(() => repo.FinishAsync(claimed, "Succeeded", "CONTAINER_MOVE_COMPLETED", Receipt(claimed, "Completed") with { SourceRetained = null }, default));
                Assert.True((await other.FindAsync(good.Id, default))!.PublishBarrier);
                var missingFact = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_move_intents SET state='Succeeded',completed_at_utc=clock_timestamp(),publish_barrier=false,published_version=repeat('F',64) WHERE id={good.Id}"));
                Assert.Equal(PostgresErrorCodes.CheckViolation, missingFact.SqlState);
                Assert.True(await repo.FinishAsync(claimed, "Succeeded", "CONTAINER_MOVE_COMPLETED", Receipt(claimed, "Completed"), default));
                var terminal = (await repo.FindAsync(good.Id, default))!; Assert.False(terminal.PublishBarrier); Assert.Equal(new string('F', 64), terminal.PublishedVersion);
                Assert.True(terminal.DestinationPublished); Assert.True(terminal.SourceRemoved); Assert.True(terminal.SourceRetained);
                Assert.True(ContainerMoveWorkflowValidation.HasValidBinding(terminal, protector));
                Assert.Equal(terminal.ExecutionJobId, (await other.ConfirmAsync(actor, Claims(terminal), good.Version, default)).ExecutionJobId);
                Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == good.Id)).State);
                Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == terminal.ExecutionJobId)).State);
                var pending = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_messages WHERE payload_json->>'jobId' IN ({good.Id.ToString("D")},{terminal.ExecutionJobId!.Value.ToString("D")}) AND processed_at_utc IS NULL").SingleAsync(); Assert.Equal(0, pending);
            }
            var canceling = Row(); await repo.EnqueueAsync(canceling, default); var scan = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await repo.MarkPlanningAsync(scan, default); await other.CancelAsync(actor, canceling.Id, default);
            await repo.FinishPlanningAsync(scan, Plan(canceling), "CONTAINER_MOVE_READY", default);
            Assert.Equal("Canceled", (await repo.FindAsync(canceling.Id, default))!.State); Assert.Null((await repo.FindAsync(canceling.Id, default))!.PlanJson);
            var canceledReady = await Ready(Row()); await other.CancelAsync(actor, canceledReady.Id, default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ConfirmAsync(actor, Claims(canceledReady), canceledReady.Version, default));
            Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(j => j.Id == canceledReady.Id)).State);

            var restarted = await Ready(Row()); var started = await Applying(restarted); await repo.DeferAsync(started, default);
            var later = Assert.IsType<ContainerMoveIntent>(await other.ClaimAsync(clock.GetUtcNow().AddSeconds(7), default)); Assert.Equal(started.Id, later.Id);
            Assert.NotNull(later.RootStartedAtUtc); Assert.Equal(ContainerFileUploadStartResult.LeaseLost, await other.StartedAsync(later, clock.GetUtcNow(), default));
            Assert.True(await other.FinishAsync(later, "Failed", "CONTAINER_MOVE_INTERRUPTED", Receipt(later, "Aborted"), default));

            var leaseReady = await Ready(Row()); var expiredLease = await Applying(leaseReady); clock.Offset = TimeSpan.FromSeconds(91);
            var recovered = Assert.IsType<ContainerMoveIntent>(await other.ClaimAsync(clock.GetUtcNow(), default)); Assert.NotEqual(expiredLease.LeaseToken, recovered.LeaseToken);
            Assert.False(await repo.FinishAsync(expiredLease, "Succeeded", "CONTAINER_MOVE_COMPLETED", Receipt(expiredLease, "Completed"), default));
            Assert.True(await other.FinishAsync(recovered, "Failed", "CONTAINER_MOVE_INTERRUPTED", Receipt(recovered, "Aborted"), default)); clock.Offset = TimeSpan.Zero;

            var expiredReady = await Ready(Row()); clock.Offset = TimeSpan.FromMinutes(6);
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.ConfirmAsync(actor, Claims(expiredReady), expiredReady.Version, default));
            var expireClaim = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await repo.FinishPlanningAsync(expireClaim, null, "CONTAINER_MOVE_PLAN_EXPIRED", default); Assert.Equal("Expired", (await repo.FindAsync(expiredReady.Id, default))!.State); clock.Offset = TimeSpan.Zero;

            var falseFactReady = await Ready(Row()); var falseFactClaim = await Applying(falseFactReady);
            Assert.True(await repo.FinishAsync(falseFactClaim, "Unknown", "CONTAINER_MOVE_RESULT_UNKNOWN", Receipt(falseFactClaim, "Unknown") with { DestinationPublished = false }, default));
            var falseFact = (await repo.FindAsync(falseFactReady.Id, default))!;
            Assert.False(falseFact.DestinationPublished); Assert.Null(falseFact.SourceRemoved); Assert.Null(falseFact.SourceRetained);
            await RawRejected($"UPDATE container_move_intents SET destination_published=true WHERE id={falseFact.Id}", "container_move_fact_immutable");
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.AcknowledgeAsync(currentAdmin, falseFact.Id, falseFact.Version, Receipt(falseFact, "Unknown") with { DestinationPublished = true }, default));
            Assert.True((await repo.FindAsync(falseFact.Id, default))!.PublishBarrier);
            await other.AcknowledgeAsync(currentAdmin, falseFact.Id, falseFact.Version, Receipt(falseFact, "Unknown"), default);
            Assert.False((await repo.FindAsync(falseFact.Id, default))!.DestinationPublished);

            // Audit failure must roll back both the new intent and all job/outbox rows.
            await second.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wdv_container_move_audit_reject() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN RAISE EXCEPTION 'wdv_container_move_audit_failed' USING ERRCODE='23514'; END $f$;
                CREATE TRIGGER wdv_container_move_audit_reject BEFORE INSERT ON audit_events
                  FOR EACH ROW WHEN (NEW.action LIKE 'containers.files.move.%') EXECUTE FUNCTION wdv_container_move_audit_reject();
                """);
            try
            {
                var rejected = Row();
                var failure = await Assert.ThrowsAsync<DbUpdateException>(() => repo.EnqueueAsync(rejected, default));
                Assert.Equal("wdv_container_move_audit_failed", Assert.IsType<PostgresException>(failure.InnerException).MessageText);
                Assert.False(await second.ContainerMoveIntents.AnyAsync(r => r.Id == rejected.Id));
                Assert.False(await second.OperationJobs.AnyAsync(r => r.Id == rejected.Id));
                var outboxCount = await second.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_messages WHERE payload_json->>'jobId'={rejected.Id.ToString("D")}").SingleAsync();
                Assert.Equal(0, outboxCount);
                Assert.False(await second.OperationJobEvents.AnyAsync(r => r.JobId == rejected.Id));
            }
            finally
            {
                await second.Database.ExecuteSqlRawAsync("DROP TRIGGER wdv_container_move_audit_reject ON audit_events; DROP FUNCTION wdv_container_move_audit_reject();");
            }

            // A real competing connection owns the gate; no expired lease can be renewed after waiting.
            var waitReady = await Ready(Row()); await repo.ConfirmAsync(actor, Claims(waitReady), waitReady.Version, default);
            var gateWaitClaim = Assert.IsType<ContainerMoveIntent>(await repo.ClaimAsync(clock.GetUtcNow(), default));
            await using (var gate = await second.Database.BeginTransactionAsync())
            {
                await second.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(41307341);");
                var blocked = await Assert.ThrowsAsync<PostgresException>(() => repo.StartedAsync(gateWaitClaim, clock.GetUtcNow(), default));
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, blocked.SqlState);
                await gate.RollbackAsync();
            }
            clock.Offset = TimeSpan.FromSeconds(91);
            Assert.False(await repo.RenewAsync(gateWaitClaim, clock.GetUtcNow(), default));
            var waited = Assert.IsType<ContainerMoveIntent>(await other.ClaimAsync(clock.GetUtcNow(), default));
            Assert.NotEqual(gateWaitClaim.LeaseToken, waited.LeaseToken);
            await other.CancelAsync(actor, waited.Id, default);
            Assert.True(await other.FinishAsync(waited, "Canceled", "CONTAINER_MOVE_CANCELED", Receipt(waited, "Aborted"), default));
            clock.Offset = TimeSpan.Zero;

            var budget = new List<ContainerMoveIntent>();
            for (var i = 0; i < 4; i++) { var r = Row(); await repo.EnqueueAsync(r, default); budget.Add(r); }
            var over = Row(); await Assert.ThrowsAsync<InvalidOperationException>(() => repo.EnqueueAsync(over, default)); Assert.False(await db.OperationJobs.AnyAsync(j => j.Id == over.Id));
            foreach (var r in budget) await repo.CancelAsync(actor, r.Id, default);
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261028083811_ContainerCopyIntents"));
            Assert.Equal("container_move_downgrade_requires_empty_table", downgrade.MessageText); await db.Database.MigrateAsync();
            Assert.False(await db.ContainerMoveIntents.AnyAsync(r => r.CompletedAtUtc == null || r.PublishBarrier || r.LeaseToken != null));
            Assert.DoesNotContain(await db.OperationJobs.Where(j => ids.Contains(j.Id) || db.ContainerMoveIntents.Any(r => r.ExecutionJobId == j.Id && ids.Contains(r.Id))).ToArrayAsync(), j => j.State is "Queued" or "Running");
            Console.WriteLine("INBOX_PHASE=container-move-persistent-dual-jobs-fences-and-recovery");
        }
        finally
        {
            // These two pre-existing entity types permit precise synthetic fixture cleanup.
            // New permanent move intents are deliberately retained until the isolated database is dropped.
            await second.ContainerFileUploadOperations.Where(r => uploadIds.Contains(r.Id)).ExecuteDeleteAsync();
            await second.ContainerDirectoryOperations.Where(r => directoryIds.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(keyPath);
        }
    }
    private sealed class MoveClock : TimeProvider { public TimeSpan Offset { get; set; } public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset); }
}
