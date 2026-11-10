using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceArchiveIntent> WorkspaceArchiveIntents => Set<WorkspaceArchiveIntent>();
    public DbSet<WorkspaceArchiveStep> WorkspaceArchiveSteps => Set<WorkspaceArchiveStep>();
    private static void ConfigureWorkspaceArchives(ModelBuilder builder)
    {
        builder.Entity<WorkspaceArchiveIntent>(entity =>
        {
            entity.ToTable("development_workspace_archives", table =>
            {
                table.HasCheckConstraint("ck_workspace_archive_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.HasCheckConstraint("ck_workspace_archive_phase", "phase IN ('plan','capture','verify')");
                table.HasCheckConstraint("ck_workspace_archive_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.HasCheckConstraint("ck_workspace_archive_snapshot", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384");
                table.HasCheckConstraint("ck_workspace_archive_confirmation", "NOT awaiting_confirmation OR (phase = 'plan' AND plan_json IS NOT NULL AND finished_at_utc IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Phase).HasMaxLength(8);
            entity.Property(row => row.ExecutionSubject).HasMaxLength(256);
            entity.Property(row => row.LoginBinding).HasMaxLength(64);
            foreach (var name in new[] { nameof(WorkspaceArchiveIntent.SnapshotJson), nameof(WorkspaceArchiveIntent.PlanJson),
                         nameof(WorkspaceArchiveIntent.CaptureJson), nameof(WorkspaceArchiveIntent.VerifyJson) })
                entity.Property<string>(name).HasColumnType("jsonb");
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<WorkspaceArchiveIntent>(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL");
        });
        builder.Entity<WorkspaceArchiveStep>(entity =>
        {
            entity.ToTable("development_workspace_archive_steps", table =>
            {
                table.HasCheckConstraint("ck_workspace_archive_step_phase", "phase IN ('plan','capture','verify')");
                table.HasCheckConstraint("ck_workspace_archive_step_request", "octet_length(request_json) BETWEEN 1 AND 4096");
                table.HasCheckConstraint("ck_workspace_archive_step_proof", "proof_json IS NULL OR octet_length(proof_json::text) BETWEEN 1 AND 32768");
                table.HasCheckConstraint("ck_workspace_archive_step_settled", "settled_at_utc IS NULL OR proof_json IS NOT NULL");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Phase).HasMaxLength(8);
            // Keep canonical request bytes, not jsonb's reformatted serialization.
            entity.Property(row => row.RequestJson).HasColumnType("text");
            entity.Property(row => row.ProofJson).HasColumnType("jsonb");
            entity.HasOne<WorkspaceArchiveIntent>().WithMany().HasForeignKey(row => row.ArchiveId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.ArchiveId, row.Phase }).IsUnique();
        });
    }
}
