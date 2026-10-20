using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureTreeRestorings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileTreeRestoringOperation>(entity =>
        {
            entity.ToTable("file_tree_restoring_operations", table =>
            {
                table.HasCheckConstraint("ck_tree_restoring_state", "state IN ('Queued','Planning','Ready','RestoringQueued','Restoring','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_tree_restoring_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','RestoringQueued','Restoring','Unknown'))");
                table.HasCheckConstraint("ck_tree_restoring_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_tree_restoring_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.HasCheckConstraint("ck_tree_restoring_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','RestoringQueued','Restoring','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_restoring_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('RestoringQueued','Restoring','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_restoring_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_tree_restoring_result", "(state='Succeeded')=(published_version IS NOT NULL) AND (NOT partial_retained OR state IN ('Unknown','Failed','Canceled','Acknowledged'))");
                table.HasCheckConstraint("ck_tree_restoring_archive", "(archive_bytes IS NULL)=(archive_sha256 IS NULL) AND (plan_json IS NOT NULL)=(archive_bytes IS NOT NULL) AND (archive_bytes IS NULL OR (archive_bytes BETWEEN 1 AND 18253611008 AND archive_sha256 ~ '^[A-F0-9]{64}$' AND archive_bytes=(plan_json->>'ArchiveBytes')::bigint AND archive_sha256=plan_json->>'ArchiveSha256'))");
                table.HasCheckConstraint("ck_tree_restoring_capture", "capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'Format'='tar.gz' AND command_json->>'CaptureKind'='Trash'");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(36); entity.Property(item => item.State).HasMaxLength(16);
            foreach (var name in new[] { nameof(FileTreeRestoringOperation.LoginBinding), nameof(FileTreeRestoringOperation.OwnerHash), nameof(FileTreeRestoringOperation.RequestHash),
                nameof(FileTreeRestoringOperation.SourceHash), nameof(FileTreeRestoringOperation.TargetHash), nameof(FileTreeRestoringOperation.PlanHash), nameof(FileTreeRestoringOperation.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.CommandJson).HasColumnType("jsonb"); entity.Property(item => item.PlanJson).HasColumnType("jsonb");
            entity.Property(item => item.ArchiveSha256).HasMaxLength(64);
            entity.Property(item => item.ResultCode).HasMaxLength(80); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => item.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FileTreeCapture>().WithMany().HasForeignKey(item => item.CaptureId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
