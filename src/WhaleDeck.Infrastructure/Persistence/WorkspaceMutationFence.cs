using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Infrastructure.Persistence;

internal static class WorkspaceMutationFence
{
    public static async Task RequireActiveAsync(PlatformDbContext db, Guid id, string owner, CancellationToken token,
        WorkspaceDefinition? snapshot = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("WORKSPACE_FENCE_TRANSACTION_REQUIRED");
        var rows = await db.DevelopmentWorkspaces.FromSqlInterpolated($"""
            SELECT * FROM development_workspaces WHERE id = {id} AND owner_subject = {owner} FOR SHARE
            """).AsNoTracking().ToArrayAsync(token);
        var row = rows.SingleOrDefault() ?? throw new KeyNotFoundException("WORKSPACE_NOT_FOUND");
        if (row.LifecycleState != "Active") throw new InvalidOperationException("WORKSPACE_LIFECYCLE_INACTIVE");
        if (row.State != WorkspaceStorageAllocation.Allocated || snapshot is not null &&
            (row.LifecycleVersion != snapshot.LifecycleVersion || row.Version != snapshot.Version ||
             row.EnvironmentSha256 != snapshot.EnvironmentSha256 || row.StorageRootId != snapshot.StorageRootId ||
             row.CpuMillis != snapshot.Budget.CpuMillis || row.MemoryMiB != snapshot.Budget.MemoryMiB ||
             row.DiskMiB != snapshot.Budget.DiskMiB))
            throw new InvalidOperationException("WORKSPACE_LIFECYCLE_SNAPSHOT_CHANGED");
    }
}
