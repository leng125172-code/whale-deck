using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests;

public sealed partial class NativeNotificationDispatchTests
{
    /// <summary>Actual SQL/repository scope behavior, not installed Agent/filesystem evidence.</summary>
    private static async Task FileHierarchyLockBoundaries(DbContextOptions<PlatformDbContext> options)
    {
        const string root = "test.hierarchy";
        var accepted = new List<Guid>();
        async Task<FileUploadOperation> Upload(string path, string selectedRoot = root)
        {
            await using var context = new PlatformDbContext(options);
            var id = Guid.NewGuid(); var subject = "native-scope-" + id.ToString("N");
            var actor = new TerminalActor(subject, null, [], new('A', 64), "native-file-scope", null);
            var row = await new FileUploadRepository(context).EnqueueAsync(actor, id, id.ToString("D"), new('C', 64),
                new(subject, actor.LoginBinding, new(selectedRoot, path, 3, new('B', 64), "ABSENT"), DateTimeOffset.UtcNow.AddMinutes(5)), DateTimeOffset.UtcNow.AddMinutes(15), default);
            accepted.Add(row.Id); return row;
        }
        static async Task Busy(Func<Task<FileUploadOperation>> operation, string code = "FILES_TARGET_BUSY") =>
            Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(operation)).Message);

        await using var db = new PlatformDbContext(options);
        var parent = await Upload("projects"); await Busy(() => Upload("projects/code/file.txt"));
        var sibling = await Upload("projects-two/code.txt"); var otherRoot = await Upload("projects/code/file.txt", "test.hierarchy-other");
        Assert.NotEqual(parent.Id, sibling.Id); Assert.NotEqual(parent.Id, otherRoot.Id);
        var child = await Upload("reverse/child.txt"); await Busy(() => Upload("reverse"));
        await Upload("case/A.txt"); await Upload("case/a.txt");
        await Upload("unicode/é"); await Upload("unicode/é");

        var legacy = await Upload("legacy/folder/file.txt");
        await db.FilePathLocks.Where(item => item.OperationId == legacy.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ScopeKind, "Legacy").SetProperty(item => item.RootId, (string?)null).SetProperty(item => item.RelativePath, (string?)null));
        await Busy(() => Upload("legacy"));
        // A failed acquisition rolls back its hydration; an unrelated accepted
        // operation persists the upgrade without deleting the original lock.
        Assert.Equal("Legacy", (await db.FilePathLocks.AsNoTracking().SingleAsync(item => item.OperationId == legacy.Id)).ScopeKind);
        await Upload("unrelated-to-legacy");
        var upgraded = await db.FilePathLocks.AsNoTracking().SingleAsync(item => item.OperationId == legacy.Id);
        Assert.Equal("Path", upgraded.ScopeKind); Assert.Equal(root, upgraded.RootId); Assert.Equal("legacy/folder/file.txt", upgraded.RelativePath);

        await db.FileUploadOperations.Where(item => item.Id == child.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.State, "Unknown").SetProperty(item => item.LeaseExpiresAtUtc, DateTimeOffset.UtcNow.AddDays(-1)));
        await Busy(() => Upload("reverse")); // expired lease is not permission to unlock Unknown
        Assert.True(await db.FilePathLocks.AnyAsync(item => item.OperationId == child.Id));

        await db.FilePathLocks.Where(item => item.OperationId == legacy.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ScopeKind, "Token").SetProperty(item => item.RootId, (string?)null).SetProperty(item => item.RelativePath, (string?)null));
        await Busy(() => Upload("unrelated-to-tampering"), "FILES_LOCK_SCOPE_UNVERIFIABLE");
        await db.FilePathLocks.Where(item => item.OperationId == legacy.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ScopeKind, "Path").SetProperty(item => item.RootId, root).SetProperty(item => item.RelativePath, "legacy/folder/file.txt"));

        var orphan = Guid.NewGuid(); var orphanHash = FileManagementService.Hash(root + ":orphan");
        db.OperationJobs.Add(new() { Id = orphan, JobType = "test.file-scope", ActorSubject = "native-scope-orphan", IdempotencyKey = orphan.ToString("D") });
        db.FilePathLocks.Add(new() { OperationId = orphan, PathHash = orphanHash }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Busy(() => Upload("unrelated-to-orphan"), "FILES_LOCK_SCOPE_UNVERIFIABLE");
        Assert.True(await db.FilePathLocks.AnyAsync(item => item.OperationId == orphan));
        await db.FilePathLocks.Where(item => item.OperationId == orphan).ExecuteDeleteAsync();
        await db.OperationJobs.Where(item => item.Id == orphan).ExecuteDeleteAsync();

        // Two actual connections race ancestor against descendant, not just
        // identical leaf hashes. Only one job may be registered.
        var racers = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            try { return (Operation: await Upload(index == 0 ? "race" : "race/child.txt"), Busy: false); }
            catch (InvalidOperationException error) when (error.Message == "FILES_TARGET_BUSY") { return (Operation: (FileUploadOperation?)null, Busy: true); }
        }));
        Assert.Single(racers, item => item.Busy); var winner = Assert.Single(racers, item => !item.Busy).Operation!;
        Assert.Equal(1, await db.FilePathLocks.CountAsync(item => item.OperationId == winner.Id));
        Assert.Equal(1, await db.AuditEvents.CountAsync(item => item.JobId == winner.Id));

        var constraintId = Guid.NewGuid();
        db.OperationJobs.Add(new() { Id = constraintId, JobType = "test.file-scope", ActorSubject = "native-scope-constraint", IdempotencyKey = constraintId.ToString("D") });
        await db.SaveChangesAsync();
        foreach (var kind in new[] { "Bad", "Path" })
        {
            var hash = FileManagementService.Hash("constraint-" + kind);
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO file_path_locks(path_hash,operation_id,scope_kind) VALUES({hash},{constraintId},{kind})"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await db.OperationJobs.Where(item => item.Id == constraintId).ExecuteDeleteAsync();
        Assert.Equal(accepted.Count, await db.FilePathLocks.CountAsync(item => accepted.Contains(item.OperationId)));
        Assert.All(await db.FilePathLocks.AsNoTracking().Where(item => accepted.Contains(item.OperationId)).ToArrayAsync(), item => Assert.Equal("Path", item.ScopeKind));
        // These are isolated SQL fixtures, never dispatched to an Agent. The
        // existing runner drops the entire owned database after the suite.
        Console.WriteLine("INBOX_PHASE=file-hierarchy-locks-real-sql");
    }
}
