using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<DevelopmentWorkspaceRecord> DevelopmentWorkspaces => Set<DevelopmentWorkspaceRecord>();
    public DbSet<WorkspaceRunIntent> WorkspaceRunIntents => Set<WorkspaceRunIntent>();

    private static void ConfigureDevelopmentWorkspaces(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceRunIntent>(entity =>
        {
            entity.ToTable("development_workspace_runs", table =>
            {
                table.HasCheckConstraint("ck_workspace_run_lease",
                    "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL OR report_stage IN ('Collect', 'Retire')) IS TRUE");
                table.HasCheckConstraint("ck_workspace_run_report_stage",
                    "report_stage IS NULL OR (finished_at_utc IS NOT NULL AND report_stage IN ('Collect', 'Retire', 'Complete', 'Attention'))");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Runtime).HasMaxLength(48);
            entity.Property(row => row.Task).HasMaxLength(24);
            entity.Property(row => row.LoginBinding).HasMaxLength(64);
            entity.Property(row => row.SnapshotJson).HasColumnType("jsonb");
            entity.Property(row => row.ReportStage).HasMaxLength(12);
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<WorkspaceRunIntent>(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL")
                .HasDatabaseName("uq_workspace_run_pending");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL");
            entity.HasIndex(row => new { row.ReportStage, row.AvailableAtUtc, row.Id })
                .HasFilter("report_stage IN ('Collect', 'Retire')");
        });
        modelBuilder.Entity<DevelopmentWorkspaceRecord>(entity =>
        {
            entity.ToTable("development_workspaces", table =>
            {
                table.HasCheckConstraint("ck_development_workspace_shape",
                    "cpu_millis >= 100 AND memory_mi_b >= 128 AND disk_mi_b >= 256");
                table.HasCheckConstraint("ck_development_workspace_storage_state", """
                    (CASE state
                        WHEN 'Registered' THEN version = 1 AND storage_plan_sha256 IS NULL
                            AND storage_allocation_started_at_utc IS NULL AND storage_allocated_at_utc IS NULL
                        WHEN 'StorageAllocating' THEN version = 2 AND storage_plan_sha256 ~ '^[a-f0-9]{64}$'
                            AND storage_allocation_started_at_utc >= created_at_utc AND storage_allocated_at_utc IS NULL
                        WHEN 'StorageAllocated' THEN version = 3 AND storage_plan_sha256 ~ '^[a-f0-9]{64}$'
                            AND storage_allocation_started_at_utc >= created_at_utc
                            AND storage_allocated_at_utc >= storage_allocation_started_at_utc
                        ELSE FALSE END) IS TRUE
                    """);
                table.HasCheckConstraint("ck_development_workspace_manifest",
                    "octet_length(environment_json) BETWEEN 1 AND 4096 AND environment_sha256 = encode(sha256(convert_to(environment_json,'UTF8')),'hex') AND request_sha256 ~ '^[a-f0-9]{64}$'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Slug).HasMaxLength(48).UseCollation("C");
            entity.Property(row => row.OwnerSubject).HasMaxLength(256);
            entity.Property(row => row.StorageRootId).HasMaxLength(48);
            entity.Property(row => row.State).HasMaxLength(24);
            entity.Property(row => row.EnvironmentSha256).HasMaxLength(64);
            entity.Property(row => row.RequestSha256).HasMaxLength(64);
            entity.Property(row => row.StoragePlanSha256).HasMaxLength(64);
            entity.HasIndex(row => row.Slug).IsUnique();
            entity.HasIndex(row => new { row.OwnerSubject, row.RegistrationKey }).IsUnique();
            entity.HasIndex(row => new { row.OwnerSubject, row.Slug });
        });
    }
}
