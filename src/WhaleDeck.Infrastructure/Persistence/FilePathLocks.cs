using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Called inside the shared 41307341 advisory transaction by both workflows.</summary>
internal static class FilePathLocks
{
    internal static async Task Acquire(PlatformDbContext db, Guid id, string actor, IEnumerable<string> hashes, CancellationToken cancellationToken)
    {
        if (await db.FileUploadOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) +
            await db.FileMutationOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) +
            await db.FileArchiveOperations.CountAsync(item => item.CompletedAtUtc == null, cancellationToken) >= 64 ||
            await db.FileUploadOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) +
            await db.FileMutationOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) +
            await db.FileArchiveOperations.CountAsync(item => item.CompletedAtUtc == null && item.ActorSubject == actor, cancellationToken) >= 4)
            throw new InvalidOperationException("FILES_OPERATION_BUDGET_EXHAUSTED");
        var targets = hashes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (await db.FilePathLocks.AnyAsync(item => targets.Contains(item.PathHash), cancellationToken)) throw new InvalidOperationException("FILES_TARGET_BUSY");
        foreach (var hash in targets) db.FilePathLocks.Add(new() { PathHash = hash, OperationId = id });
    }
    internal static Task<int> Release(PlatformDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.FilePathLocks.Where(item => item.OperationId == id).ExecuteDeleteAsync(cancellationToken);
}
