using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceRetirementIntent> WorkspaceRetirementIntents => Set<WorkspaceRetirementIntent>();
    private static void ConfigureWorkspaceRetirements(ModelBuilder builder)
    {
        builder.Entity<WorkspaceRetirementIntent>(entity =>
        {
            entity.ToTable("development_workspace_retirements", table =>
            {
                table.HasCheckConstraint("ck_workspace_retirement_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.HasCheckConstraint("ck_workspace_retirement_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.HasCheckConstraint("ck_workspace_retirement_plan", "(plan_json IS NULL) = (plan_expires_at_utc IS NULL) AND (confirmed_at_utc IS NULL OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_workspace_retirement_size", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384 AND (plan_json IS NULL OR octet_length(plan_json::text) BETWEEN 1 AND 8192)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.ExecutionSubject).HasMaxLength(256);
            entity.Property(row => row.LoginBinding).HasMaxLength(64);
            entity.Property(row => row.SnapshotJson).HasColumnType("jsonb");
            entity.Property(row => row.PlanJson).HasColumnType("jsonb");
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkspaceArchiveIntent>().WithMany().HasForeignKey(row => row.ArchiveId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<WorkspaceRetirementIntent>(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL AND plan_json IS NULL");
        });
    }
}
