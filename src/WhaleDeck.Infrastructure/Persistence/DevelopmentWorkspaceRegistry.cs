using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class DevelopmentWorkspaceRegistry(PlatformDbContext db) : IDevelopmentWorkspaceRegistry
{
    public async Task<WorkspaceRegistrationReceipt?> FindReplayAsync(string subject, Guid key, string requestHash, CancellationToken token)
    {
        var row = await db.DevelopmentWorkspaces.AsNoTracking().SingleOrDefaultAsync(
            row => row.OwnerSubject == subject && row.RegistrationKey == key, token);
        if (row is null) return null;
        if (row.RequestSha256 != requestHash) throw new InvalidOperationException("WORKSPACE_REGISTRATION_KEY_CONFLICT");
        return new(Map(row), true);
    }

    public Task<WorkspaceRegistrationReceipt> RegisterAsync(TerminalActor actor, Guid key, string requestHash,
        WorkspaceDefinition workspace, WorkspaceBudget capacity, CancellationToken token) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SELECT pg_advisory_xact_lock(41307420);", token);
            var prior = await FindReplayAsync(actor.Subject, key, requestHash, token);
            if (prior is not null) return prior;

            if (await db.DevelopmentWorkspaces.AnyAsync(row => row.Slug == workspace.Slug, token))
                throw new InvalidOperationException("WORKSPACE_SLUG_CONFLICT");
            var totals = await db.DevelopmentWorkspaces.GroupBy(_ => 1).Select(rows => new
            {
                Cpu = rows.Sum(row => (long)row.CpuMillis), Memory = rows.Sum(row => (long)row.MemoryMiB),
                Disk = rows.Sum(row => (long)row.DiskMiB)
            }).SingleOrDefaultAsync(token);
            if ((totals?.Cpu ?? 0) + workspace.Budget.CpuMillis > capacity.CpuMillis ||
                (totals?.Memory ?? 0) + workspace.Budget.MemoryMiB > capacity.MemoryMiB ||
                (totals?.Disk ?? 0) + workspace.Budget.DiskMiB > capacity.DiskMiB)
                throw new InvalidOperationException("WORKSPACE_REGISTRATION_CAPACITY");

            db.DevelopmentWorkspaces.Add(new()
            {
                Id = workspace.Id, Slug = workspace.Slug, OwnerSubject = actor.Subject, StorageRootId = workspace.StorageRootId,
                State = workspace.State, Version = workspace.Version, CpuMillis = workspace.Budget.CpuMillis,
                MemoryMiB = workspace.Budget.MemoryMiB, DiskMiB = workspace.Budget.DiskMiB,
                EnvironmentJson = DevelopmentWorkspaces.EncodeEnvironment(workspace.Environment), EnvironmentSha256 = workspace.EnvironmentSha256,
                RegistrationKey = key, RequestSha256 = requestHash, CreatedAtUtc = workspace.CreatedAtUtc
            });
            db.AuditEvents.Add(new()
            {
                ActorSubject = actor.Subject, Action = "development.workspace.register", TargetType = "workspace",
                TargetId = workspace.Id.ToString("D"), TraceId = actor.TraceId, SourceIp = actor.SourceIp, OccurredAtUtc = workspace.CreatedAtUtc
            });
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new WorkspaceRegistrationReceipt(workspace, false);
        });

    public async Task<WorkspaceDefinition?> FindAsync(Guid id, string? owner, CancellationToken token)
    {
        var row = await Scope(owner).SingleOrDefaultAsync(row => row.Id == id, token);
        return row is null ? null : Map(row);
    }

    public async Task<WorkspacePage> ListAsync(string? owner, string? after, int take, CancellationToken token)
    {
        var rows = await Scope(owner, after).OrderBy(row => row.Slug).Take(take + 1).ToArrayAsync(token);
        return new([.. rows.Take(take).Select(Map)], rows.Length > take ? rows[take - 1].Slug : null);
    }

    private IQueryable<DevelopmentWorkspaceRecord> Scope(string? owner, string? after = null)
    {
        var query = (after is null ? db.DevelopmentWorkspaces :
            db.DevelopmentWorkspaces.FromSqlInterpolated($"SELECT * FROM development_workspaces WHERE slug > {after}")).AsNoTracking();
        return owner is null ? query : query.Where(row => row.OwnerSubject == owner);
    }

    private static WorkspaceDefinition Map(DevelopmentWorkspaceRecord row) => new(
        row.Id, row.Slug, row.OwnerSubject, row.StorageRootId, row.State, row.Version,
        new(row.CpuMillis, row.MemoryMiB, row.DiskMiB), DevelopmentWorkspaces.DecodeEnvironment(row.EnvironmentJson, row.EnvironmentSha256),
        row.EnvironmentSha256, row.CreatedAtUtc);
}
