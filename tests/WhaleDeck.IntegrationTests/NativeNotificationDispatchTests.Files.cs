using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Persistence;

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
        Assert.True(await db.OutboxMessages.AnyAsync(item => item.MessageType == "FileUploadRequested" && item.PayloadJson.Contains(first.Id.ToString("D"))));
        Assert.Single(await db.AuditEvents.Where(item => item.JobId == first.Id).ToArrayAsync());
        var receiving = await repository.BeginReceiveAsync(actor, first.Id, now, default);
        Assert.True(await repository.MayReceiveAsync(first.Id, receiving.ReceiveToken!.Value, now, default));
        Assert.False(await repository.MayReceiveAsync(first.Id, Guid.NewGuid(), now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginReceiveAsync(actor, first.Id, now, default));
        await repository.ReadyAsync(receiving, default); await Due(first.Id);
        var claim = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(first.Id, claim.Id); Assert.Null(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(FileUploadStartResult.Started, await repository.StartedAsync(claim, DateTimeOffset.UtcNow, default));
        var completed = new FileUploadReceiptDto("Completed", 3, new('B', 64), new('D', 64), true, true, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed with { Sha256 = new('E', 64) }, default));
        Assert.True(await repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed, default));
        Assert.False(await repository.FinishAsync(claim, "Succeeded", "FILES_PUBLISHED", completed, default));
        var finished = await repository.OwnedAsync(actor, first.Id, default); Assert.Equal("Succeeded", finished!.State); Assert.True(finished.PreviousRetained);
        Assert.Equal(4, await db.OperationJobEvents.CountAsync(item => item.JobId == first.Id));
        Assert.Equal(2, await db.AuditEvents.CountAsync(item => item.JobId == first.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(item => item.MessageType == "FileUploadRequested" && item.PayloadJson.Contains(first.Id.ToString("D")) && item.ProcessedAtUtc == null));

        var cancel = await Create("cancel-before-body"); await repository.CancelAsync(actor, cancel.Id, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BeginReceiveAsync(actor, cancel.Id, DateTimeOffset.UtcNow, default));
        var canceled = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(cancel.Id, canceled.Id); Assert.Equal(FileUploadStartResult.Canceled, await repository.StartedAsync(canceled, DateTimeOffset.UtcNow, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FinishAsync(canceled, "Canceled", "FILES_CANCELED", null, default));
        Assert.True(await repository.FinishAsync(canceled, "Canceled", "FILES_CANCELED", new("Aborted", 0, new('0', 64), "", false, false, false), default));

        var raced = await Create("lease-recovery"); await Due(raced.Id);
        var expired = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await repository.ClaimAsync(DateTimeOffset.UtcNow, default));
        await db.FileUploadOperations.Where(item => item.Id == raced.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await using var concurrent = new PlatformDbContext(options); var second = new FileUploadRepository(concurrent);
        var winner = Assert.IsType<WhaleDeck.Domain.Entities.FileUploadOperation>(await second.ClaimAsync(DateTimeOffset.UtcNow, default));
        Assert.NotEqual(expired.LeaseToken, winner.LeaseToken);
        Assert.False(await repository.FinishAsync(expired, "Succeeded", "FILES_PUBLISHED", completed, default));
        Assert.True(await second.FinishAsync(winner, "Unknown", "FILES_RESULT_UNKNOWN", null, default));
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
        Console.WriteLine("INBOX_PHASE=file-intents-owner-audit-leases-cancel-native-sql");
    }
}
