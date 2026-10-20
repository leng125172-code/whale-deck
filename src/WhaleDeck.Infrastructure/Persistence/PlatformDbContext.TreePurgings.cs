using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureTreePurgings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileTreePurgingOperation>(entity =>
        {
            entity.ToTable("file_tree_purging_operations", table =>
            {
                table.HasCheckConstraint("ck_tree_purging_state", "state IN ('Queued','Planning','Ready','PurgingQueued','Purging','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_tree_purging_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','PurgingQueued','Purging','Unknown'))");
                table.HasCheckConstraint("ck_tree_purging_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (receipt_hash IS NULL OR receipt_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_tree_purging_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND (receipt_json IS NULL OR (octet_length(receipt_json::text)<=32768 AND jsonb_typeof(receipt_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.HasCheckConstraint("ck_tree_purging_capture", "(capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'CaptureKind' IN ('Trash','Overwrite') AND command_json->>'Scope' IN ('Archive','Originals','All') AND command_json->>'CatalogueProofHash' ~ '^[A-F0-9]{64}$') IS TRUE");
                table.HasCheckConstraint("ck_tree_purging_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (plan_json IS NULL OR (plan_json->'Command'=command_json AND (plan_json->>'EntryCount')::integer BETWEEN 1 AND 10000 AND (plan_json->>'TotalBytes')::bigint BETWEEN 0 AND 17179869184 AND (plan_json->>'ArchiveBytes')::bigint BETWEEN 1 AND 18253611008) IS TRUE) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc AND ready_expires_at_utc<=planning_expires_at_utc+INTERVAL '5 minutes') AND (state NOT IN ('Ready','PurgingQueued','Purging','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_purging_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_job_id<>capture_id AND execution_expires_at_utc>created_at_utc AND execution_expires_at_utc<=ready_expires_at_utc+INTERVAL '15 minutes')) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('PurgingQueued','Purging','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL) AND (state<>'Purging' OR root_started_at_utc IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_purging_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_tree_purging_effects", "(NOT deletion_started OR root_started_at_utc IS NOT NULL) AND (NOT archive_removed OR deletion_started AND command_json->>'Scope' IN ('Archive','All')) AND (NOT originals_removed OR deletion_started AND command_json->>'Scope' IN ('Originals','All')) AND (receipt_json IS NOT NULL OR NOT (deletion_started OR archive_removed OR originals_removed)) AND (root_started_at_utc IS NULL OR receipt_json IS NOT NULL OR state IN ('Purging','Unknown'))");
                table.HasCheckConstraint("ck_tree_purging_receipt", "(receipt_json IS NULL)=(receipt_hash IS NULL) AND (receipt_json IS NULL)=(receipt_observed_at_utc IS NULL) AND (receipt_json IS NULL OR (execution_job_id IS NOT NULL AND receipt_observed_at_utc>=created_at_utc AND receipt_json->>'OperationId'=execution_job_id::text AND receipt_json->>'Kind'='DirectoryPurge' AND receipt_json->>'CaptureId'=capture_id::text AND receipt_json->>'Scope'=command_json->>'Scope' AND receipt_json->>'Fingerprint'=plan_json->>'Fingerprint' AND (receipt_json->>'EntryCount')::integer=(plan_json->>'EntryCount')::integer AND (receipt_json->>'TotalBytes')::bigint=(plan_json->>'TotalBytes')::bigint AND (receipt_json->>'ArchiveBytes')::bigint=(plan_json->>'ArchiveBytes')::bigint AND (receipt_json->>'DeletionStarted')::boolean=deletion_started AND (receipt_json->>'ArchiveRemoved')::boolean=archive_removed AND (receipt_json->>'OriginalsRemoved')::boolean=originals_removed AND receipt_json->>'State' IN ('Completed','Unknown','Aborted')) IS TRUE)");
                table.HasCheckConstraint("ck_tree_purging_outcome", "(receipt_json IS NULL OR (CASE receipt_json->>'State' WHEN 'Completed' THEN state='Succeeded' AND deletion_started AND (command_json->>'Scope' NOT IN ('Archive','All') OR archive_removed) AND (command_json->>'Scope' NOT IN ('Originals','All') OR originals_removed) WHEN 'Unknown' THEN state IN ('Unknown','Acknowledged') AND deletion_started WHEN 'Aborted' THEN state IN ('Failed','Canceled','Acknowledged') AND NOT deletion_started AND NOT archive_removed AND NOT originals_removed ELSE false END) IS TRUE) AND (state NOT IN ('Succeeded','Acknowledged') OR receipt_json IS NOT NULL)");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(36); entity.Property(item => item.State).HasMaxLength(16);
            foreach (var name in new[] { nameof(FileTreePurgingOperation.LoginBinding), nameof(FileTreePurgingOperation.OwnerHash), nameof(FileTreePurgingOperation.RequestHash),
                nameof(FileTreePurgingOperation.SourceHash), nameof(FileTreePurgingOperation.PlanHash), nameof(FileTreePurgingOperation.ReceiptHash) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.CommandJson).HasColumnType("jsonb"); entity.Property(item => item.PlanJson).HasColumnType("jsonb"); entity.Property(item => item.ReceiptJson).HasColumnType("jsonb");
            entity.Property(item => item.ResultCode).HasMaxLength(80); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => item.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => new { item.CaptureId, item.Id });
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FileTreeCapture>().WithMany().HasForeignKey(item => item.CaptureId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
