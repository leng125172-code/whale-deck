using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceLifecycleIntent> WorkspaceLifecycleIntents => Set<WorkspaceLifecycleIntent>();

    private static void ConfigureWorkspaceLifecycle(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceLifecycleIntent>(entity =>
        {
            entity.ToTable("development_workspace_lifecycle", table =>
            {
                table.HasCheckConstraint("ck_workspace_lifecycle_action", "action IN ('stop','resume') AND expected_version >= 1");
                table.HasCheckConstraint("ck_workspace_lifecycle_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.HasCheckConstraint("ck_workspace_lifecycle_snapshot", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Action).HasMaxLength(8);
            entity.Property(row => row.ExecutionSubject).HasMaxLength(256);
            entity.Property(row => row.LoginBinding).HasMaxLength(64);
            entity.Property(row => row.SnapshotJson).HasColumnType("jsonb");
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<WorkspaceLifecycleIntent>(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL");
        });
    }
}
