using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    private static async Task FileArchiveBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var other = Guid.NewGuid(); var hash = new string('A', 64);
        db.OperationJobs.AddRange(new() { Id = id, JobType = "files.archive", ActorSubject = "native-archive-admin", IdempotencyKey = id.ToString("D") },
            new() { Id = other, JobType = "files.archive", ActorSubject = "native-other-admin", IdempotencyKey = other.ToString("D") });
        db.FileVersions.Add(new() { Id = id, RootId = "test.archive", RelativePath = "source", TargetHash = hash, SourceVersion = hash, Sha256 = hash,
            SizeBytes = 7, CaptureKind = "Trash", CaptureState = "Trashed", CapturedAtUtc = now, LastObservedAtUtc = now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        async Task Reject(Func<Task<int>> write, string message)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(write); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal(message, error.MessageText);
        }
        // Unknown legacy capture actors may be linked once, only to the exact
        // human-owned source Job. No actor inference from the browsing user.
        await Reject(() => db.FileVersions.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SourceJobId, id).SetProperty(item => item.CreatedBySubject, "native-other-admin")), "file_version_actor_mismatch");
        Assert.Equal(1, await db.FileVersions.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SourceJobId, id).SetProperty(item => item.CreatedBySubject, "native-archive-admin")));
        await Reject(() => db.FileVersions.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SourceJobId, (Guid?)null).SetProperty(item => item.CreatedBySubject, (string?)null)), "file_version_actor_immutable");
        await Reject(() => db.FileVersions.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.SourceJobId, other).SetProperty(item => item.CreatedBySubject, "native-other-admin")), "file_version_actor_immutable");
        foreach (var statement in new Func<Task<int>>[] {
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET root_id='other.root' WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET relative_path='elsewhere' WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET target_hash={new string('B', 64)} WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET source_version={new string('B', 64)} WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET sha256={new string('B', 64)} WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET size_bytes=8 WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET capture_kind='Overwrite' WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET captured_at_utc={now.AddSeconds(1)} WHERE id={id}"),
            () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE file_versions SET different_source_device=true WHERE id={id}") })
            await Reject(statement, "file_version_identity_immutable");
        var stateError = await Assert.ThrowsAsync<PostgresException>(() => db.FileVersions.Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CaptureState, "Available")));
        Assert.Equal(PostgresErrorCodes.CheckViolation, stateError.SqlState); Assert.Equal("ck_file_version_capture", stateError.ConstraintName);
        Assert.Equal(1, await db.FileVersions.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.CaptureState, "Restored").SetProperty(item => item.LastFullVerificationAtUtc, now).SetProperty(item => item.Version, 1L)));
        var version = await db.FileVersions.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal("source", version.RelativePath); Assert.Equal(hash, version.Sha256); Assert.Equal(7, version.SizeBytes);
        Assert.Equal("native-archive-admin", version.CreatedBySubject); Assert.Equal("Restored", version.CaptureState);
        db.FileArchiveOperations.Add(new() { Id = other, ActorSubject = "native-other-admin", LoginBinding = hash, OwnerHash = hash, RequestHash = hash,
            Kind = "restore", RootId = "test.archive", RelativePath = "source", TargetHash = hash, ArchiveVersionId = id,
            ExpectedVersion = "ABSENT", Sha256 = hash, SizeBytes = 7, PlanJson = "{}", CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(10), NextAttemptAtUtc = now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var terminal = await Assert.ThrowsAsync<PostgresException>(() => db.FileArchiveOperations.Where(item => item.Id == other)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.State, "Succeeded")));
        Assert.Equal("ck_file_archive_terminal", terminal.ConstraintName);
        var retained = await Assert.ThrowsAsync<PostgresException>(() => db.FileVersions.Where(item => item.Id == id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, retained.SqlState);
        Assert.Equal("FK_file_archive_operations_file_versions_archive_version_id", retained.ConstraintName);
        Assert.Equal(1, await db.FileVersions.CountAsync(item => item.Id == id));
        Assert.Equal(1, await db.FileArchiveOperations.Where(item => item.Id == other).ExecuteDeleteAsync());
        Assert.Equal(1, await db.FileVersions.Where(item => item.Id == id).ExecuteDeleteAsync());
        await db.OperationJobs.Where(item => item.Id == id || item.Id == other).ExecuteDeleteAsync();
        Console.WriteLine("INBOX_PHASE=file-archive-constraints");
    }
}
