using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task FileUploadBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options); var repository = new FileUploadRepository(db);
        var actor = new TerminalActor("native-files-admin", null, [], new('A', 64), "native-file-trace", null);
        var now = DateTimeOffset.UtcNow;
        FileUploadPlanClaims Claims(string path) => new(actor.Subject, actor.LoginBinding, new("test.files", path, 3, new('B', 64), "ABSENT"), DateTimeOffset.UtcNow.AddMinutes(5));
        async Task<WhaleDeck.Domain.Entities.FileUploadOperation> Create(string path) => await repository.EnqueueAsync(actor, Guid.NewGuid(), Guid.NewGuid().ToString("D"), new('C', 64), Claims(path), DateTimeOffset.UtcNow.AddMinutes(15), default);
        async Task Due(Guid id) => await db.FileUploadOperations.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var first = await Create("资料/owned.bin");
        Assert.Null(await repository.OwnedAsync(actor with { Subject = "other" }, first.Id, default));
        Assert.Null(await repository.OwnedAsync(actor with { LoginBinding = new('D', 64) }, first.Id, default));
        Assert.NotEqual(first.LoginBinding, first.OwnerHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("资料/owned.bin"));
        var job = await db.OperationJobs.AsNoTracking().SingleAsync(item => item.Id == first.Id);
        Assert.DoesNotContain("owned.bin", job.RequestJson); Assert.DoesNotContain(actor.LoginBinding, job.RequestJson);
        Assert.Equal(1, await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM outbox_messages WHERE message_type='FileUploadRequested' AND payload_json->>'jobId'={first.Id.ToString("D")}").SingleAsync());
        Assert.Single(await db.AuditEvents.Where(item => item.JobId == first.Id).ToArrayAsync());
        var receiving = await repository.BeginReceiveAsync(actor, first.Id, now, default);
        Assert.True(await repository.MayReceiveAsync(first.Id, receiving.ReceiveToken!.Value, now, default));
        Assert.False(await repository.MayReceiveAsync(first.Id, Guid.NewGuid(), now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginReceiveAsync(actor, first.Id, now, default));
        await repository.ReadyAsync(receiving, default); await Due(first.Id);
        var claim = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(first.Id, claim.Id); Assert.Null(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        var priorLease = claim.LeaseExpiresAtUtc;
        Assert.True(await repository.RenewAsync(claim, DateTimeOffset.UtcNow, default));
        Assert.True((await repository.OwnedAsync(actor, claim.Id, default))!.LeaseExpiresAtUtc >= priorLease);
        Assert.False(await repository.RenewAsync(first, DateTimeOffset.UtcNow, default));
        Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, DateTimeOffset.UtcNow, default));
        var completed = new FileUploadReceiptDto("Completed", 3, new('B', 64), new('D', 64), true, true, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed with { Sha256 = new('E', 64) }, default));
        Assert.True(await repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed, default));
        Assert.False(await repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed, default));
        Assert.False(await repository.RenewAsync(claim, DateTimeOffset.UtcNow, default));
        var finished = await repository.OwnedAsync(actor, first.Id, default); Assert.Equal("Succeeded", finished!.State); Assert.True(finished.PreviousRetained);
        Assert.Equal(4, await db.OperationJobEvents.CountAsync(item => item.JobId == first.Id));
        Assert.Equal(2, await db.AuditEvents.CountAsync(item => item.JobId == first.Id));
        Assert.Equal(0, await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM outbox_messages WHERE message_type='FileUploadRequested' AND payload_json->>'jobId'={first.Id.ToString("D")} AND processed_at_utc IS NULL").SingleAsync());

        var cancel = await Create("cancel-before-body"); await repository.CancelAsync(actor, cancel.Id, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginReceiveAsync(actor, cancel.Id, DateTimeOffset.UtcNow, default));
        var canceled = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(cancel.Id, canceled.Id); Assert.Equal(FileUploadStartResult.Canceled, await repository.StartedAsync(canceled, DateTimeOffset.UtcNow, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FinishAsync(canceled, "Canceled", "FILES_CANCELED", null, default));
        Assert.True(await repository.FinishAsync(canceled, "Canceled", "FILES_CANCELED", new("Aborted", 0, new('0', 64), "", false, false, false), default));

        var raced = await Create("lease-recovery"); await Due(raced.Id);
        var expired = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        await db.FileUploadOperations.Where(item => item.Id == raced.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.False(await repository.RenewAsync(expired, DateTimeOffset.UtcNow, default));
        await using var concurrent = new PlatformDbContext(options); var second = new FileUploadRepository(concurrent);
        var winner = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await second.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.NotEqual(expired.LeaseToken, winner.LeaseToken);
        Assert.False(await repository.RenewAsync(expired, DateTimeOffset.UtcNow, default));
        Assert.True(await second.RenewAsync(winner, DateTimeOffset.UtcNow, default));
        Assert.False(await repository.FinishAsync(expired, "Succeeded", "FILES_PUBLISHED", completed, default));
        Assert.True(await second.FinishAsync(winner, "Unknown", "FILES_RESULT_UNKNOWN", null, default));
        await db.FileUploadOperations.Where(item => item.Id == raced.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Null((await repository.OwnedAsync(actor, raced.Id, default))!.CompletedAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("lease-recovery"));

        // Failure to persist immutable human audit must roll back intent, job,
        // outbox and initial event together, not leave an unaudited authority.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT native_file_audit_failure CHECK (action NOT LIKE 'files.%') NOT VALID");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => Create("audit-failure")); }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT native_file_audit_failure"); }
        Assert.False(await db.FileUploadOperations.AnyAsync(item => item.RelativePath == "audit-failure"));
        Assert.Equal(3, await db.FileUploadOperations.CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_upload_operations SET size_bytes=1073741825 WHERE id={first.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_upload_operations SET sha256='invalid' WHERE id={first.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_upload_operations SET state='imaginary' WHERE id={first.Id}"));

        var folder = Path.Combine(Path.GetTempPath(), "wd-file-seal-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var keyFile = Path.Combine(folder, "key"); await File.WriteAllBytesAsync(keyFile, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = keyFile }).Build());
            var sealId = Guid.NewGuid(); var expiry = DateTimeOffset.UtcNow.AddMinutes(15); expiry = expiry.AddTicks(-(expiry.Ticks % 10));
            var claims = Claims("sealed-durable-intent");
            var seal = protector.Fingerprint(new(sealId, actor.Subject, actor.LoginBinding, claims.File, expiry));
            await repository.EnqueueAsync(actor, sealId, Guid.NewGuid().ToString("D"), seal, claims, expiry, default);
            var sealedRow = await repository.OwnedAsync(actor, sealId, default); Assert.True(FileManagementService.HasValidBinding(sealedRow!, protector));
            await db.FileUploadOperations.Where(item => item.Id == sealId).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, expiry.AddSeconds(1)));
            Assert.False(FileManagementService.HasValidBinding((await repository.OwnedAsync(actor, sealId, default))!, protector));
            await db.FileUploadOperations.Where(item => item.Id == sealId).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, expiry));
            await repository.CancelAsync(actor, sealId, default);
            var sealCanceled = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(sealId, sealCanceled.Id);
            Assert.True(await repository.FinishAsync(sealCanceled, "Canceled", "FILES_CANCELED", new("Aborted", 0, new('0', 64), "", false, false, false), default));
        }
        finally { File.Delete(keyFile); Directory.Delete(folder); }

        // A lease valid before a blocked job-row lock must be rechecked at
        // the actual start, not use the request's earlier application time.
        var late = await Create("late-start-lease"); await Due(late.Id);
        var lateClaim = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default)); Assert.Equal(late.Id, lateClaim.Id);
        await db.FileUploadOperations.Where(item => item.Id == late.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMilliseconds(500)));
        await using (var locker = new PlatformDbContext(options))
        {
            await using var transaction = await locker.Database.BeginTransactionAsync();
            await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM operation_jobs WHERE id={late.Id} FOR UPDATE");
            var pending = Task.Run(async () =>
            {
                await using var contender = new PlatformDbContext(options);
                return await new FileUploadRepository(contender).StartedAsync(lateClaim, DateTimeOffset.UtcNow, default);
            });
            await Task.Delay(1200); await transaction.CommitAsync();
            Assert.Equal(FileUploadStartResult.LeaseLost, await pending);
        }
        Assert.Null((await repository.OwnedAsync(actor, late.Id, default))!.RootStartedAtUtc);
        await db.FileUploadOperations.Where(item => item.Id == late.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMilliseconds(500)));
        await using (var locker = new PlatformDbContext(options))
        {
            await using var transaction = await locker.Database.BeginTransactionAsync();
            await locker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM file_upload_operations WHERE id={late.Id} FOR UPDATE");
            var pendingRenewal = Task.Run(async () =>
            {
                await using var contender = new PlatformDbContext(options);
                return await new FileUploadRepository(contender).RenewAsync(lateClaim, DateTimeOffset.UtcNow, default);
            });
            await Task.Delay(1200); await transaction.CommitAsync();
            Assert.False(await pendingRenewal);
        }
        Console.WriteLine("INBOX_PHASE=file-intents-owner-audit-leases-cancel-native-sql");
    }
}
