using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureTreeCopies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileTreeCopyOperation>(entity =>
        {
            entity.ToTable("file_tree_copy_operations", table =>
            {
                table.HasCheckConstraint("ck_tree_copy_state", "state IN ('Queued','Planning','Ready','CopyQueued','Copying','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_tree_copy_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','CopyQueued','Copying','Unknown'))");
                table.HasCheckConstraint("ck_tree_copy_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_tree_copy_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.HasCheckConstraint("ck_tree_copy_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','CopyQueued','Copying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_copy_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('CopyQueued','Copying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                table.HasCheckConstraint("ck_tree_copy_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_tree_copy_result", "(state='Succeeded')=(published_version IS NOT NULL) AND (NOT partial_retained OR state IN ('Unknown','Failed','Canceled','Acknowledged'))");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(36); entity.Property(item => item.State).HasMaxLength(16);
            foreach (var name in new[] { nameof(FileTreeCopyOperation.LoginBinding), nameof(FileTreeCopyOperation.OwnerHash), nameof(FileTreeCopyOperation.RequestHash),
                nameof(FileTreeCopyOperation.SourceHash), nameof(FileTreeCopyOperation.TargetHash), nameof(FileTreeCopyOperation.PlanHash), nameof(FileTreeCopyOperation.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.CommandJson).HasColumnType("jsonb"); entity.Property(item => item.PlanJson).HasColumnType("jsonb");
            entity.Property(item => item.ResultCode).HasMaxLength(80); entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => item.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
