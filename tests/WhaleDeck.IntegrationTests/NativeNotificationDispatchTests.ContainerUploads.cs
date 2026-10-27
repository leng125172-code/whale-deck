using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Security.Cryptography;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    // Real isolated PostgreSQL/migrations/transactions; Root receipts below are
    // explicit synthetic fixtures, not proof of a physical container publication.
    private static async Task ContainerFileUploadBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var folder = Path.Combine(Path.GetTempPath(), "wd-container-intent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var keyFile = Path.Combine(folder, "key");
        await File.WriteAllBytesAsync(keyFile, RandomNumberGenerator.GetBytes(32));
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        var plans = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyFile }).Build());
        var repository = new ContainerFileUploadRepository(db, plans);
        var ids = new List<Guid>(); var hostIds = new List<Guid>();
        var actor = new TerminalActor("native-container-upload-admin", null, [], new('A',64), "native-container-upload-trace", null);
        var cid = new string('a',64); const string instance = "777:2026-10-27T01:02:03.123456789Z";
        ContainerFileUploadOperation Row(string path, string? container = null, bool overwrite = false)
        {
            var created = ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow);
            var id = Guid.NewGuid(); ids.Add(id);
            var binding = new ContainerFileUploadBinding(id, Guid.NewGuid(), actor.Subject, actor.LoginBinding, actor.TraceId,
                new(new(container ?? cid, instance, path), new('B',64), new('C',64), overwrite ? new('A',64) : "ABSENT", overwrite ? 5 : 0), 3, new('D',64), new('E',64), created, created.AddMinutes(15));
            return new() { Id = id, IdempotencyKey = binding.IdempotencyKey, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding,
                TraceId = actor.TraceId, OwnerHash = ContainerFileUploadIntentValidation.Owner(actor.Subject, actor.LoginBinding), ContainerId = binding.Target.Location.ContainerId,
                InstanceId = instance, RelativePath = path, TargetProof = binding.Target.TargetProof, ExpectedParentVersion = binding.Target.ExpectedParentVersion,
                ExpectedVersion = binding.Target.ExpectedVersion, PreviousSizeBytes = binding.Target.CurrentSizeBytes,
                PlanHash = binding.PlanHash, RequestHash = plans.FingerprintContainerUpload(binding), Sha256 = binding.Sha256,
                SizeBytes = binding.SizeBytes, CreatedAtUtc = created, ExpiresAtUtc = binding.ExpiresAtUtc, NextAttemptAtUtc = created.AddSeconds(6) };
        }
        async Task Due(Guid id) => await db.ContainerFileUploadOperations.Where(item => item.Id == id).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        async Task<ContainerFileUploadOperation> Claim(Guid id)
        { await Due(id); var result = Assert.IsType<ContainerFileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(id, result.Id); return result; }
        static ContainerFileUploadReceiptDto Proof(ContainerFileUploadOperation row, string state) => new(row.ContainerId, row.InstanceId, row.Id, row.TargetProof,
            new(state, state == "Prevented" ? 0 : row.SizeBytes, state == "Prevented" ? Convert.ToHexString(SHA256.HashData([])) : row.Sha256,
                state == "Published" ? new('F',64) : "", false, state == "Published", false), false);
        try
        {
            var first = await repository.EnqueueAsync(Row("资料/first.bin"), default);
            Assert.Equal(first.Id, (await repository.EnqueueAsync(first, default)).Id);
            Assert.Equal(first.RequestHash, plans.FingerprintContainerUpload(ContainerFileUploadIntentValidation.ReadBinding((await repository.OwnedAsync(actor, first.Id, default))!)));
            Assert.Null(await repository.OwnedAsync(actor with { LoginBinding = new('F',64) }, first.Id, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.BeginReceiveAsync(actor with { Subject = "other" }, first.Id, DateTimeOffset.UtcNow, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.EnqueueAsync(Row("资料/first.bin/child"), default));
            await db.ContainerFileUploadOperations.Where(item => item.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Sha256, new string('F',64)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginReceiveAsync(actor, first.Id, DateTimeOffset.UtcNow, default));
            await db.ContainerFileUploadOperations.Where(item => item.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Sha256, first.Sha256));
            await new JobRepository(db).RequestCancellationAsync(first.Id, actor.Subject, default);
            Assert.True(await db.OutboxMessages.AnyAsync(item => item.MessageType == "ContainerFileUploadCancellationRequested" && item.PayloadJson.Contains(first.Id.ToString("D"))));
            var canceled = await Claim(first.Id);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.FinishAsync(canceled, "Canceled", "CONTAINER_UPLOAD_CANCELED", null, default));
            Assert.True(await repository.FinishAsync(canceled, "Canceled", "CONTAINER_UPLOAD_CANCELED", Proof(canceled, "Prevented"), default));
            var terminal = (await repository.OwnedAsync(actor, first.Id, default))!;
            Assert.NotNull(terminal.CompletedAtUtc); Assert.False(terminal.PublishBarrier); Assert.Null(terminal.PublishedVersion);
            Assert.False(await repository.FinishAsync(canceled, "Canceled", "CONTAINER_UPLOAD_CANCELED", Proof(canceled, "Prevented"), default));
            Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == first.Id && item.Action == "containers.files.upload.result"));

            foreach (var overwrite in new[] { false, true })
            {
                var successful = await repository.EnqueueAsync(Row("资料/success-" + overwrite + ".bin", overwrite: overwrite), default);
                var received = await repository.BeginReceiveAsync(actor, successful.Id, DateTimeOffset.UtcNow, default);
                Assert.True(await repository.ReadyAsync(received, default)); var publishing = await Claim(successful.Id);
                Assert.Equal(ContainerFileUploadStartResult.Started, await repository.StartedAsync(publishing, DateTimeOffset.UtcNow, default));
                var proof = Proof(publishing, "Published");
                if (overwrite)
                {
                    await Assert.ThrowsAsync<ArgumentException>(() => repository.FinishAsync(publishing, "Succeeded", "CONTAINER_UPLOAD_PUBLISHED", proof, default));
                    var backup = new FileHddBackupDto(publishing.Id.ToString("N"), "database-platform.backup.hdd", publishing.ExpectedVersion,
                        publishing.PreviousSizeBytes, new('B',64), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
                    proof = proof with { Upload = proof.Upload with { PreviousRetained = true, HddBackup = backup } };
                    var wrongBackup = proof with { Upload = proof.Upload with { HddBackup = backup with { SizeBytes = 6 } } };
                    await Assert.ThrowsAsync<ArgumentException>(() => repository.FinishAsync(publishing, "Succeeded", "CONTAINER_UPLOAD_PUBLISHED", wrongBackup, default));
                }
                Assert.True(await repository.FinishAsync(publishing, "Succeeded", "CONTAINER_UPLOAD_PUBLISHED", proof, default));
                var published = (await repository.OwnedAsync(actor, publishing.Id, default))!;
                Assert.Equal("Succeeded", published.State); Assert.Equal(new string('F',64), published.PublishedVersion);
                Assert.Equal(overwrite, published.PreviousRetained); Assert.False(published.PublishBarrier); Assert.NotNull(published.CompletedAtUtc);
                Assert.Equal("Succeeded", (await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == publishing.Id)).State);
                Assert.False(await db.OutboxMessages.AnyAsync(item => item.ProcessedAtUtc == null && item.PayloadJson.Contains(publishing.Id.ToString("D"))));
            }

            // A host path reservation blocks publication, not the container's
            // browser waiting period. Existing host concurrency is unchanged.
            var second = await repository.EnqueueAsync(Row("资料/second.bin"), default);
            var host = new FileUploadRepository(db); var hostId = Guid.NewGuid(); hostIds.Add(hostId);
            var hostActor = actor with { Subject = "native-container-host-lock" };
            var hostClaims = new FileUploadPlanClaims(hostActor.Subject, hostActor.LoginBinding, new("test.container.host", "alias", 3, new('D',64), "ABSENT"), DateTimeOffset.UtcNow.AddMinutes(5));
            await host.EnqueueAsync(hostActor, hostId, Guid.NewGuid().ToString("D"), new('E',64), hostClaims, DateTimeOffset.UtcNow.AddMinutes(15), default);
            var receiving = await repository.BeginReceiveAsync(actor, second.Id, DateTimeOffset.UtcNow, default);
            Assert.True(await repository.MayReceiveAsync(second.Id, receiving.ReceiveToken!.Value, DateTimeOffset.UtcNow, default));
            Assert.True(await repository.ReadyAsync(receiving, default)); Assert.False(await repository.ReadyAsync(receiving, default));
            var pending = await Claim(second.Id);
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repository.StartedAsync(pending, DateTimeOffset.UtcNow, default));
            Assert.False((await repository.OwnedAsync(actor, second.Id, default))!.PublishBarrier);
            await db.FileUploadOperations.Where(item => item.Id == hostId).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            var hostClaim = Assert.IsType<FileUploadOperation>(await host.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(hostId, hostClaim.Id);
            Assert.True(await host.FinishAsync(hostClaim, "Canceled", "FILES_CANCELED", new("Aborted", 0, new('D',64), "", false, false, false), default));
            Assert.Equal(ContainerFileUploadStartResult.Started, await repository.StartedAsync(pending, DateTimeOffset.UtcNow, default));
            Assert.True((await repository.OwnedAsync(actor, second.Id, default))!.PublishBarrier);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnqueueAsync(hostActor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('E',64), hostClaims, DateTimeOffset.UtcNow.AddMinutes(15), default));
            await Assert.ThrowsAsync<ArgumentException>(() => repository.FinishAsync(pending, "Succeeded", "CONTAINER_UPLOAD_PUBLISHED", Proof(pending, "Published") with { TargetProof = new('F',64) }, default));

            // A different CID may mount the same physical file. It can stage,
            // but only one container can own publication at a time.
            var alias = await repository.EnqueueAsync(Row("資料/alias.bin", new('b',64)), default);
            var aliasReceive = await repository.BeginReceiveAsync(actor, alias.Id, DateTimeOffset.UtcNow, default); Assert.True(await repository.ReadyAsync(aliasReceive, default));
            var aliasClaim = await Claim(alias.Id);
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repository.StartedAsync(aliasClaim, DateTimeOffset.UtcNow, default));
            await db.ContainerFileUploadOperations.Where(item => item.Id == second.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.False(await repository.RenewAsync(pending, DateTimeOffset.UtcNow, default));
            Assert.False(await repository.FinishAsync(pending, "Succeeded", "CONTAINER_UPLOAD_PUBLISHED", Proof(pending, "Published"), default));
            var recovered = await Claim(second.Id); Assert.NotEqual(pending.LeaseToken, recovered.LeaseToken);
            Assert.Equal(ContainerFileUploadStartResult.LeaseLost, await repository.StartedAsync(recovered, DateTimeOffset.UtcNow, default));
            Assert.True(await repository.FinishAsync(recovered, "Unknown", "CONTAINER_UPLOAD_RESULT_UNKNOWN", null, default));
            var unknown = (await repository.OwnedAsync(actor, second.Id, default))!;
            Assert.True(unknown.PublishBarrier); Assert.NotNull(unknown.RootStartedAtUtc); Assert.Null(unknown.CompletedAtUtc);
            await Due(second.Id); Assert.Null(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
            Assert.Equal(ContainerFileUploadStartResult.Busy, await repository.StartedAsync(aliasClaim, DateTimeOffset.UtcNow, default));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET publish_barrier=false WHERE id={second.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET size_bytes=1073741825 WHERE id={alias.Id}"));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE container_file_upload_operations SET expires_at_utc=expires_at_utc+INTERVAL '1 second' WHERE id={alias.Id}"));
            Assert.True(await repository.FinishAsync(aliasClaim, "Canceled", "CONTAINER_UPLOAD_CANCELED", Proof(aliasClaim, "Discarded"), default));
            var details = await db.AuditEvents.Where(item => item.ActorSubject == actor.Subject).Select(item => item.DetailJson).ToArrayAsync();
            Assert.All(details, detail => { Assert.DoesNotContain("资料", detail!); Assert.DoesNotContain(instance, detail!); Assert.DoesNotContain(actor.LoginBinding, detail!); });

            // A failed immutable audit rolls back intent + job + event + Outbox.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_container_audit_failure CHECK (action NOT LIKE 'containers.files.upload.%') NOT VALID");
            var failed = Row("audit-failure.bin");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => repository.EnqueueAsync(failed, default)); }
            finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_container_audit_failure"); }
            Assert.False(await db.ContainerFileUploadOperations.AnyAsync(item => item.Id == failed.Id));
            Assert.False(await db.OperationJobs.AnyAsync(item => item.Id == failed.Id));
            Assert.False(await db.OutboxMessages.AnyAsync(item => item.PayloadJson.Contains(failed.Id.ToString("D"))));
            Assert.False(await db.OperationJobEvents.AnyAsync(item => item.JobId == failed.Id));
            Console.WriteLine("INBOX_PHASE=container-upload-binding-transaction-lease-alias-barrier-unknown");
        }
        finally
        {
            // These exact synthetic IDs live only in the isolated native test
            // database. This is fixture teardown, never a product recovery path.
            db.ChangeTracker.Clear();
            await db.ContainerFileUploadOperations.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
            await db.FilePathLocks.Where(item => hostIds.Contains(item.OperationId)).ExecuteDeleteAsync();
            await db.FileUploadOperations.Where(item => hostIds.Contains(item.Id)).ExecuteDeleteAsync();
            File.Delete(keyFile); Directory.Delete(folder);
        }
    }
}
