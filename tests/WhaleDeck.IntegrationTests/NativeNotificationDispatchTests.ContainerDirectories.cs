using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    // Actual PostgreSQL transactions/constraints/triggers. Synthetic Agent
    // receipts here are deliberately not called installed Root/UI evidence.
    private static async Task ContainerDirectoryBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); await using var second = new PlatformDbContext(options);
        var key = Path.GetTempFileName(); await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
        var repository = new ContainerDirectoryRepository(db, protector); var other = new ContainerDirectoryRepository(second, protector);
        var actor = new TerminalActor("native-directory-admin", null, [], new('A', 64), "native-directory-trace", null);
        var ids = new List<Guid>();
        var uploadId = Guid.NewGuid();
        ContainerDirectoryOperation Row(string path = "work/新目录", string? container = null)
        {
            var id = Guid.NewGuid(); ids.Add(id); var now = ContainerDirectoryWorkflowValidation.Time(DateTimeOffset.UtcNow);
            var plan = new ContainerDirectoryPlanDto(1, new(new(container ?? new('d', 64), "777:2026-10-28T01:02:03.123456789Z", path), "0750"), new('B', 64), new(101, 202), new('C', 64), 1000, 1000, new('D', 64));
            var binding = new ContainerDirectoryBindingDto(id, Guid.NewGuid(), actor.Subject, actor.LoginBinding, actor.TraceId,
                ContainerDirectoryWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), plan, new('E', 64), now, now.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, TraceId = actor.TraceId,
                OwnerHash = binding.Owner, ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = path,
                PlanJson = JsonSerializer.Serialize(plan, ContainerDirectoryWorkflowValidation.Json), PlanHash = binding.PlanHash, RequestHash = protector.FingerprintDirectory(binding),
                CreatedAtUtc = now, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = now };
        }
        ContainerDirectoryReceiptDto Receipt(ContainerDirectoryOperation row, string state) => new(row.Id, row.ContainerId, row.InstanceId,
            ContainerDirectoryWorkflowValidation.ReadPlan(row).Fingerprint, ContainerDirectoryWorkflowValidation.ReadPlan(row).TargetProof,
            state, state == "Completed" ? new('F', 64) : "", state == "Completed", false);
        try
        {
            var row = Row(); await repository.EnqueueAsync(row, row.CreatedAtUtc.AddMinutes(5), default);
            var winner = await other.EnqueueAsync(row, row.CreatedAtUtc.AddMinutes(5), default);
            Assert.Equal(row.Id, winner.Id); Assert.Equal(1, await db.ContainerDirectoryOperations.CountAsync(r => r.Id == row.Id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(r => r.JobId == row.Id && r.Action == "containers.files.mkdir.accepted"));
            Assert.Equal(1, await db.OperationJobEvents.CountAsync(r => r.JobId == row.Id));
            var conflict = Row("work/新目录/child"); await Assert.ThrowsAsync<InvalidOperationException>(() => other.EnqueueAsync(conflict, conflict.CreatedAtUtc.AddMinutes(5), default));
            Assert.False(await second.OperationJobs.AnyAsync(r => r.Id == conflict.Id));
            var claim = Assert.IsType<ContainerDirectoryOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Null(await other.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repository.StartedAsync(claim, DateTimeOffset.UtcNow, default));
            Assert.True((await other.FindAsync(row.Id, default))!.PublishBarrier);
            // Raw legacy host insertion has no knowledge of the new table.
            var hash = FileManagementService.Hash("native-directory-legacy-lock:" + row.Id);
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{row.Id},'Legacy')"));
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, blocked.SqlState); Assert.Equal("FILES_TARGET_BUSY", blocked.MessageText);
            var uploadNow = ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow);
            var uploadActor = "native-directory-other-upload";
            var uploadBinding = new ContainerFileUploadBinding(uploadId, Guid.NewGuid(), uploadActor, actor.LoginBinding, actor.TraceId,
                new(new(new('e', 64), "888:2026-10-28T01:02:03.123456789Z", "work/upload.bin"), new('B', 64), new('C', 64), "ABSENT", 0),
                0, Convert.ToHexString(SHA256.HashData([])), new('F', 64), uploadNow, uploadNow.AddMinutes(15));
            var upload = new ContainerFileUploadOperation { Id = uploadId, IdempotencyKey = uploadBinding.IdempotencyKey, ActorSubject = uploadActor,
                LoginBinding = actor.LoginBinding, TraceId = actor.TraceId, OwnerHash = ContainerFileUploadIntentValidation.Owner(uploadActor, actor.LoginBinding),
                ContainerId = uploadBinding.Target.Location.ContainerId, InstanceId = uploadBinding.Target.Location.InstanceId, RelativePath = uploadBinding.Target.Location.Path,
                TargetProof = uploadBinding.Target.TargetProof, ExpectedParentVersion = uploadBinding.Target.ExpectedParentVersion, ExpectedVersion = "ABSENT", SizeBytes = 0,
                PlanHash = uploadBinding.PlanHash, RequestHash = protector.FingerprintContainerUpload(uploadBinding), Sha256 = uploadBinding.Sha256,
                CreatedAtUtc = uploadNow, ExpiresAtUtc = uploadBinding.ExpiresAtUtc, NextAttemptAtUtc = uploadNow };
            await new ContainerFileUploadRepository(second, protector).EnqueueAsync(upload, default);
            var blockedUpload = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={uploadId}"));
            Assert.Equal("CONTAINER_UPLOAD_TARGET_BUSY", blockedUpload.MessageText);
            Assert.True(await repository.FinishAsync(claim, "Unknown", "CONTAINER_DIRECTORY_RESULT_UNKNOWN", Receipt(claim, "Unknown"), default));
            Assert.True((await other.FindAsync(row.Id, default))!.PublishBarrier); Assert.Null(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            var original = (await other.FindAsync(row.Id, default))!;
            var admin = actor with { Subject = "native-other-directory-admin", LoginBinding = new('F', 64) };
            await other.AcknowledgeAsync(admin, row.Id, original.Version, Receipt(original, "Unknown"), default);
            var ack = (await repository.FindAsync(row.Id, default))!;
            Assert.Equal("Acknowledged", ack.State); Assert.Equal(admin.Subject, ack.AcknowledgedBy); Assert.False(ack.PublishBarrier);
            Assert.Equal(row.RequestHash, ack.RequestHash); Assert.Equal(row.PlanJson, ack.PlanJson); Assert.NotNull(ack.AcknowledgementJson);
            Assert.Equal("Failed", (await db.OperationJobs.AsNoTracking().SingleAsync(r => r.Id == row.Id)).State);
            Assert.True(await db.AuditEvents.AnyAsync(r => r.JobId == row.Id && r.ActorSubject == admin.Subject && r.Action == "containers.files.mkdir.unknown-acknowledged"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AcknowledgeAsync(admin, row.Id, original.Version, Receipt(original, "Unknown"), default));

            var created = Row(); await repository.EnqueueAsync(created, created.CreatedAtUtc.AddMinutes(5), default);
            var createClaim = Assert.IsType<ContainerDirectoryOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repository.StartedAsync(createClaim, DateTimeOffset.UtcNow, default));
            Assert.True(await repository.FinishAsync(createClaim, "Succeeded", "CONTAINER_DIRECTORY_CREATED", Receipt(createClaim, "Completed"), default));
            Assert.False((await repository.FindAsync(created.Id, default))!.PublishBarrier);
            Assert.True(await db.OperationJobEvents.AnyAsync(e => e.JobId == created.Id && e.MessageCode == "CONTAINER_DIRECTORY_CREATED"));
            var pendingOutbox = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_messages WHERE payload_json->>'jobId'={created.Id.ToString("D")} AND processed_at_utc IS NULL").SingleAsync();
            Assert.Equal(0, pendingOutbox);

            // Prove inverse DB protection independently of current repository
            // checks: a legacy host reservation blocks even a raw barrier flip.
            await second.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES ({hash},{row.Id},'Legacy')");
            var queued = Row("work/another"); await repository.EnqueueAsync(queued, queued.CreatedAtUtc.AddMinutes(5), default);
            var queuedClaim = Assert.IsType<ContainerDirectoryOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repository.StartedAsync(queuedClaim, DateTimeOffset.UtcNow, default));
            var blockedRaw = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={queued.Id}"));
            Assert.Equal("CONTAINER_DIRECTORY_TARGET_BUSY", blockedRaw.MessageText);
            await second.FilePathLocks.Where(r => r.PathHash == hash).ExecuteDeleteAsync();
            await repository.CancelAsync(actor, queued.Id, default);
            Assert.Equal(ContainerFileUploadStartResult.Canceled, await repository.StartedAsync(queuedClaim, DateTimeOffset.UtcNow, default));
            Assert.True(await repository.FinishAsync(queuedClaim, "Canceled", "CONTAINER_DIRECTORY_CANCELED", Receipt(queuedClaim, "Aborted"), default));

            var alias = Row("work/upload-alias", new('f', 64)); await repository.EnqueueAsync(alias, alias.CreatedAtUtc.AddMinutes(5), default);
            var aliasClaim = Assert.IsType<ContainerDirectoryOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            await second.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Publishing' WHERE id={uploadId}");
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repository.StartedAsync(aliasClaim, DateTimeOffset.UtcNow, default));
            var blockedAlias = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE container_directory_operations SET publish_barrier=true,root_started_at_utc=created_at_utc,state='Applying' WHERE id={alias.Id}"));
            Assert.Equal("CONTAINER_DIRECTORY_TARGET_BUSY", blockedAlias.MessageText);
            // Exact synthetic fixture teardown in this isolated database only.
            await second.ContainerFileUploadOperations.Where(r => r.Id == uploadId).ExecuteDeleteAsync();
            await repository.CancelAsync(actor, alias.Id, default);
            Assert.True(await repository.FinishAsync(aliasClaim, "Canceled", "CONTAINER_DIRECTORY_CANCELED", Receipt(aliasClaim, "Aborted"), default));

            var fail = Row("work/audit-failure");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_directory_audit_failure CHECK (action <> 'containers.files.mkdir.accepted')");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => repository.EnqueueAsync(fail, fail.CreatedAtUtc.AddMinutes(5), default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_directory_audit_failure"); }
            Assert.False(await db.ContainerDirectoryOperations.AnyAsync(r => r.Id == fail.Id)); Assert.False(await db.OperationJobs.AnyAsync(r => r.Id == fail.Id));
            Assert.False(await db.OperationJobEvents.AnyAsync(r => r.JobId == fail.Id));
            var audits = await db.AuditEvents.Where(r => ids.Contains(r.JobId ?? Guid.Empty)).ToArrayAsync();
            Assert.All(audits, audit => { Assert.DoesNotContain(actor.LoginBinding, audit.DetailJson); Assert.DoesNotContain("work/", audit.DetailJson); });
            Console.WriteLine("INBOX_PHASE=container-directory-permanent-intent-lease-db-fence-unknown-current-admin-atomic-audit");
        }
        finally
        {
            db.ChangeTracker.Clear(); await db.FilePathLocks.Where(r => ids.Contains(r.OperationId)).ExecuteDeleteAsync();
            await db.ContainerFileUploadOperations.Where(r => r.Id == uploadId).ExecuteDeleteAsync();
            await db.ContainerDirectoryOperations.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(); File.Delete(key);
        }
    }
}
