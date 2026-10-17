using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureFileRecovery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecoveryOperation>(entity =>
        {
            entity.ToTable("file_recovery_operations", table =>
            {
                table.HasCheckConstraint("ck_file_recovery_kind", "kind IN ('upload','mutation','archive','tree-copy','tree-move') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
                table.HasCheckConstraint("ck_file_recovery_state", "state IN ('Queued','Planning','Ready','DispositionQueued','Fencing','Unknown','Disposed','Failed','Canceled','Expired') AND (completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','DispositionQueued','Fencing','Unknown'))");
                table.HasCheckConstraint("ck_file_recovery_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND original_binding_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (observation_hash IS NULL OR observation_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_file_recovery_limits", "version>=0 AND original_version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(original_json::text)<=262144 AND jsonb_typeof(original_json)='object' AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' AND (observation_json IS NULL OR octet_length(observation_json::text)<=16384)");
                table.HasCheckConstraint("ck_file_recovery_plan", "(plan_hash IS NULL)=(observation_hash IS NULL) AND (plan_hash IS NULL)=(observation_json IS NULL) AND (plan_hash IS NULL)=(root_state IS NULL) AND (plan_hash IS NULL)=(observation_binding_current IS NULL) AND (plan_hash IS NULL)=(ready_at_utc IS NULL) AND (plan_hash IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc=ready_at_utc+INTERVAL '5 minutes') AND (root_state IS NULL OR root_state IN ('Missing','Aborted','Unknown','Completed')) AND (state NOT IN ('Ready','DispositionQueued','Fencing','Unknown','Disposed') OR plan_hash IS NOT NULL)");
                table.HasCheckConstraint("ck_file_recovery_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_hash IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (fence_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('DispositionQueued','Fencing','Unknown','Disposed') OR execution_job_id IS NOT NULL) AND (lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_file_recovery_resolution", "(state='Disposed')=(original_resolution IS NOT NULL) AND (original_resolution IS NULL OR original_resolution IN ('Acknowledged','Succeeded','Superseded','Failed'))");
            });
            entity.HasKey(row => row.Id); entity.Property(row => row.ActorSubject).HasMaxLength(256);
            entity.Property(row => row.Kind).HasMaxLength(16); entity.Property(row => row.Disposition).HasMaxLength(24); entity.Property(row => row.ReasonCode).HasMaxLength(32);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(36); entity.Property(row => row.State).HasMaxLength(24); entity.Property(row => row.RootState).HasMaxLength(16);
            entity.Property(row => row.ResultCode).HasMaxLength(80); entity.Property(row => row.OriginalResolution).HasMaxLength(16);
            foreach (var name in new[] { nameof(FileRecoveryOperation.LoginBinding), nameof(FileRecoveryOperation.OriginalBindingHash), nameof(FileRecoveryOperation.RequestHash), nameof(FileRecoveryOperation.PlanHash), nameof(FileRecoveryOperation.ObservationHash) }) entity.Property(name).HasMaxLength(64);
            entity.Property(row => row.OriginalJson).HasColumnType("jsonb"); entity.Property(row => row.ObservationJson).HasColumnType("jsonb");
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.ActorSubject, row.LoginBinding, row.IdempotencyKey }).IsUnique();
            entity.HasIndex(row => new { row.Kind, row.OriginalIntentId }).IsUnique().HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(row => row.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(row => row.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(row => new { row.NextAttemptAtUtc, row.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(row => row.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(row => row.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(row => row.OriginalExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
