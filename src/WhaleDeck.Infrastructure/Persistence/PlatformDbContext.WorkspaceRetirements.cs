using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceRetirementIntent> WorkspaceRetirementIntents => Set<WorkspaceRetirementIntent>();
    public DbSet<WorkspaceRetirementWorkflow> WorkspaceRetirementWorkflows => Set<WorkspaceRetirementWorkflow>();
    public DbSet<WorkspaceRetirementStep> WorkspaceRetirementSteps => Set<WorkspaceRetirementStep>();
    public DbSet<WorkspaceRetirementCompletion> WorkspaceRetirementCompletions => Set<WorkspaceRetirementCompletion>();
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
        builder.Entity<WorkspaceRetirementWorkflow>(entity =>
        {
            entity.ToTable("development_workspace_retirement_workflows", table =>
            {
                table.HasCheckConstraint("ck_workspace_retire_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.HasCheckConstraint("ck_workspace_retire_digests", "archive_plan_sha256 ~ '^[a-f0-9]{64}$' AND capture_receipt_sha256 ~ '^[a-f0-9]{64}$'");
                table.HasCheckConstraint("ck_workspace_retire_phase", "phase IN ('plan','execute') AND (phase <> 'execute' OR confirmed_at_utc IS NOT NULL)");
                table.HasCheckConstraint("ck_workspace_retire_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.HasCheckConstraint("ck_workspace_retire_plan", "(plan_json IS NULL) = (plan_expires_at_utc IS NULL) AND (confirmed_at_utc IS NULL OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_workspace_retire_size", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384 AND (plan_json IS NULL OR octet_length(plan_json::text) BETWEEN 1 AND 8192) AND (execution_json IS NULL OR octet_length(execution_json::text) BETWEEN 1 AND 8192)");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Phase).HasMaxLength(8);
            entity.Property(row => row.ArchivePlanSha256).HasMaxLength(64); entity.Property(row => row.CaptureReceiptSha256).HasMaxLength(64);
            entity.Property(row => row.ExecutionSubject).HasMaxLength(256); entity.Property(row => row.LoginBinding).HasMaxLength(64);
            entity.Property(row => row.SnapshotJson).HasColumnType("jsonb"); entity.Property(row => row.PlanJson).HasColumnType("jsonb");
            entity.Property(row => row.ExecutionJson).HasColumnType("jsonb");
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkspaceArchiveIntent>().WithMany().HasForeignKey(row => row.ArchiveId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<WorkspaceRetirementWorkflow>(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL");
        });
        builder.Entity<WorkspaceRetirementStep>(entity =>
        {
            entity.ToTable("development_workspace_retirement_steps", table =>
            {
                table.HasCheckConstraint("ck_workspace_retire_step_phase", "phase IN ('plan','execute')");
                table.HasCheckConstraint("ck_workspace_retire_step_request", "octet_length(request_json) BETWEEN 1 AND 4096");
                table.HasCheckConstraint("ck_workspace_retire_step_proof", "proof_json IS NULL OR octet_length(proof_json::text) BETWEEN 1 AND 32768");
                table.HasCheckConstraint("ck_workspace_retire_step_settled", "settled_at_utc IS NULL OR proof_json IS NOT NULL");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Phase).HasMaxLength(8); entity.Property(row => row.RequestJson).HasColumnType("text");
            entity.Property(row => row.ProofJson).HasColumnType("jsonb");
            entity.HasOne<WorkspaceRetirementWorkflow>().WithMany().HasForeignKey(row => row.RetirementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.RetirementId, row.Phase }).IsUnique();
        });
        builder.Entity<WorkspaceRetirementCompletion>(entity =>
        {
            entity.ToTable("development_workspace_retirement_completions");
            entity.HasKey(row => row.WorkspaceId); entity.Property(row => row.WorkspaceId).ValueGeneratedNever();
            entity.HasOne<DevelopmentWorkspaceRecord>().WithOne().HasForeignKey<WorkspaceRetirementCompletion>(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkspaceRetirementWorkflow>().WithOne().HasForeignKey<WorkspaceRetirementCompletion>(row => row.RetirementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkspaceRetirementStep>().WithOne().HasForeignKey<WorkspaceRetirementCompletion>(row => row.ExecuteStepId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
