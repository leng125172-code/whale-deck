using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerRecycling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerRecycleIntent>(entity =>
        {
            entity.ToTable("container_recycle_intents", table =>
            {
                table.HasCheckConstraint("ck_container_recycle_requested", "whaledeck_recycle_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.HasCheckConstraint("ck_container_recycle_planned", "plan_json IS NULL OR whaledeck_recycle_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.HasCheckConstraint("ck_container_recycle_receipt_shape", "receipt_json IS NULL OR whaledeck_recycle_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.HasCheckConstraint("ck_container_recycle_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_container_recycle_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown'))");
                table.HasCheckConstraint("ck_container_recycle_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.HasCheckConstraint("ck_container_recycle_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.HasCheckConstraint("ck_container_recycle_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_container_recycle_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.HasCheckConstraint("ck_container_recycle_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_recycle_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.HasCheckConstraint("ck_container_recycle_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state <> 'Applying' OR publish_barrier)");
                table.HasCheckConstraint("ck_container_recycle_result", "((state <> 'Succeeded' OR root_state = 'Trashed' AND binding_current) AND (state <> 'Superseded' OR root_state = 'Restored' OR root_state = 'Trashed' AND NOT binding_current) AND (state NOT IN ('Succeeded','Superseded') OR history_verified AND root_started_at_utc IS NOT NULL) AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state = 'Aborted')) IS TRUE");
                table.HasCheckConstraint("ck_container_recycle_history", "(NOT history_verified OR history_may_exist AND history_digest IS NOT NULL) AND ((history_digest IS NOT NULL) = history_verified) AND (history_digest IS NULL OR history_digest ~ '^[A-F0-9]{64}$') AND (execution_job_id IS NOT NULL OR root_state IS NULL AND NOT history_may_exist AND NOT history_verified AND history_digest IS NULL) AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state = 'Aborted')");
                table.HasCheckConstraint("ck_container_recycle_receipt", "(root_state IS NULL AND receipt_json IS NULL AND receipt_hash IS NULL AND source_retained IS NULL AND source_restored IS NULL AND binding_current IS NULL AND history_available IS NULL) OR ((root_state IN ('Trashed','Restored','Aborted','Unknown') AND receipt_json IS NOT NULL AND receipt_hash IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND octet_length(receipt_json) BETWEEN 1 AND 65536 AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND binding_current IS NOT NULL AND history_available IS NOT NULL AND (NOT history_available OR history_verified) AND (NOT binding_current OR root_state IN ('Trashed','Restored') AND history_available) AND CASE WHEN root_state='Unknown' THEN source_retained IS NULL AND source_restored IS NULL ELSE source_retained IS NOT NULL AND source_restored IS NOT NULL AND source_retained=(root_state='Trashed') AND source_restored=(root_state='Restored') END AND (root_state NOT IN ('Trashed','Restored') OR history_verified)) IS TRUE)");
                table.HasCheckConstraint("ck_container_recycle_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id' = execution_job_id::text AND receipt_json::jsonb->>'ContainerId' = container_id AND receipt_json::jsonb->>'InstanceId' = instance_id AND receipt_json::jsonb->>'Fingerprint' = plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'TargetProof' = plan_json::jsonb->>'TargetProof' AND receipt_json::jsonb->>'State' = root_state AND (receipt_json::jsonb->>'SourceRetained')::boolean IS NOT DISTINCT FROM source_retained AND (receipt_json::jsonb->>'SourceRestored')::boolean IS NOT DISTINCT FROM source_restored AND (receipt_json::jsonb->>'BindingCurrent')::boolean = binding_current AND (receipt_json::jsonb->>'HistoryAvailable')::boolean = history_available AND (receipt_json::jsonb->>'HistoryVerified')::boolean = history_verified AND (NOT (receipt_json::jsonb->>'HistoryMayExist')::boolean OR history_may_exist) AND receipt_json::jsonb->>'HistoryDigest' = COALESCE(history_digest,'')) IS TRUE)");
                table.HasCheckConstraint("ck_container_recycle_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_container_recycle_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActorSubject).HasMaxLength(256); entity.Property(r => r.AcknowledgedBy).HasMaxLength(256);
            entity.Property(r => r.TraceId).HasMaxLength(128); entity.Property(r => r.ContainerId).HasMaxLength(64); entity.Property(r => r.InstanceId).HasMaxLength(64);
            entity.Property(r => r.RelativePath).HasMaxLength(4096);
            entity.Property(r => r.CommandJson).HasColumnType("text"); entity.Property(r => r.PlanJson).HasColumnType("text"); entity.Property(r => r.AcknowledgementJson).HasColumnType("text");
            entity.Property(r => r.ReceiptJson).HasColumnType("text"); entity.Property(r => r.RootState).HasMaxLength(16);
            entity.Property(r => r.State).HasMaxLength(16); entity.Property(r => r.ResultCode).HasMaxLength(64); entity.Property(r => r.Version).IsConcurrencyToken();
            foreach (var name in new[] { nameof(ContainerRecycleIntent.LoginBinding), nameof(ContainerRecycleIntent.OwnerHash), nameof(ContainerRecycleIntent.RequestHash),
                nameof(ContainerRecycleIntent.ReceiptHash), nameof(ContainerRecycleIntent.PlanHash), nameof(ContainerRecycleIntent.ExecutionHash), nameof(ContainerRecycleIntent.HistoryDigest) }) entity.Property(name).HasMaxLength(64);
            entity.HasIndex(r => new { r.ActorSubject, r.IdempotencyKey }).IsUnique();
            entity.HasIndex(r => r.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(r => r.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(r => new { r.ContainerId, r.CompletedAtUtc });
            entity.HasIndex(r => new { r.NextAttemptAtUtc, r.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(r => r.PublishBarrier).IsUnique().HasFilter("publish_barrier");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
