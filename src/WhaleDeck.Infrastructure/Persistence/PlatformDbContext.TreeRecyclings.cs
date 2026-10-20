using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureTreeRecyclings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileTreeRecyclingOperation>(entity =>
        {
            entity.ToTable("file_tree_recycling_operations", table =>
            {
                table.HasCheckConstraint("ck_tree_recycling_state", "state IN ('Queued','Planning','Ready','RecyclingQueued','Recycling','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_tree_recycling_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','RecyclingQueued','Recycling','Unknown'))");
                table.HasCheckConstraint("ck_tree_recycling_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_tree_recycling_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.HasCheckConstraint("ck_tree_recycling_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','RecyclingQueued','Recycling','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_recycling_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('RecyclingQueued','Recycling','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_recycling_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_tree_recycling_result", "(state='Succeeded')=(source_removed AND originals_retained) AND (NOT source_may_have_changed OR capture_verified) AND (NOT source_removed OR source_may_have_changed) AND (NOT capture_verified OR capture_may_be_retained) AND (NOT originals_retained OR source_removed)");
                table.HasCheckConstraint("ck_tree_recycling_capture", "capture_verified=(capture_id IS NOT NULL) AND capture_verified=(capture_hash IS NOT NULL) AND capture_verified=(capture_created_at_utc IS NOT NULL) AND capture_verified=(archive_bytes IS NOT NULL) AND capture_verified=(archive_sha256 IS NOT NULL) AND (NOT capture_verified OR (execution_job_id IS NOT NULL AND capture_id=execution_job_id AND capture_hash ~ '^[A-F0-9]{64}$' AND archive_bytes BETWEEN 1 AND 18253611008 AND archive_sha256 ~ '^[A-F0-9]{64}$'))");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(36); entity.Property(item => item.State).HasMaxLength(16);
            foreach (var name in new[] { nameof(FileTreeRecyclingOperation.LoginBinding), nameof(FileTreeRecyclingOperation.OwnerHash), nameof(FileTreeRecyclingOperation.RequestHash),
                nameof(FileTreeRecyclingOperation.SourceHash), nameof(FileTreeRecyclingOperation.PlanHash), nameof(FileTreeRecyclingOperation.CaptureHash), nameof(FileTreeRecyclingOperation.ArchiveSha256) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.CommandJson).HasColumnType("jsonb"); entity.Property(item => item.PlanJson).HasColumnType("jsonb");
            entity.Property(item => item.ResultCode).HasMaxLength(80); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => item.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FileTreeCapture>(entity =>
        {
            entity.ToTable("file_tree_captures", table =>
            {
                table.HasCheckConstraint("ck_tree_capture_hashes", "owner_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND manifest_sha256 ~ '^[A-F0-9]{64}$' AND archive_sha256 ~ '^[A-F0-9]{64}$' AND proof_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_tree_capture_limits", "entry_count BETWEEN 1 AND 10000 AND total_bytes BETWEEN 0 AND 17179869184 AND archive_bytes BETWEEN 1 AND 18253611008 AND jsonb_typeof(source_json)='object' AND octet_length(source_json::text)<=32768");
                table.HasCheckConstraint("ck_tree_capture_kind", "(kind='Trash' AND content_sha256 IS NULL) OR (kind='Overwrite' AND content_sha256 IS NOT NULL AND content_sha256 ~ '^[A-F0-9]{64}$')");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.SourceJson).HasColumnType("jsonb");
            entity.Property(item => item.Kind).HasMaxLength(16).HasDefaultValue("Trash");
            entity.Property(item => item.ContentSha256).HasMaxLength(64);
            foreach (var name in new[] { nameof(FileTreeCapture.OwnerHash), nameof(FileTreeCapture.SourceHash), nameof(FileTreeCapture.ManifestSha256), nameof(FileTreeCapture.ArchiveSha256), nameof(FileTreeCapture.ProofHash) }) entity.Property(name).HasMaxLength(64);
            entity.HasIndex(item => item.IntentId).IsUnique(); entity.HasIndex(item => new { item.SourceHash, item.CreatedAtUtc, item.Id });
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.IntentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
