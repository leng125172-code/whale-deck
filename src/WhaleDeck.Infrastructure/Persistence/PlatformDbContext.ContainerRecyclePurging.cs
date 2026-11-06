using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerRecyclePurging(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerRecyclePurgeIntent>(entity =>
        {
            entity.ToTable("container_recycle_purge_intents", table =>
            {
                table.HasCheckConstraint("ck_container_recycle_purge_requested", "whaledeck_recycle_purge_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.HasCheckConstraint("ck_container_recycle_purge_planned", "plan_json IS NULL OR whaledeck_recycle_purge_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.HasCheckConstraint("ck_container_recycle_purge_state", "state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_container_recycle_purge_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown'))");
                table.HasCheckConstraint("ck_container_recycle_purge_source", "source_intent_id <> id AND source_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND source_proof_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_container_recycle_purge_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.HasCheckConstraint("ck_container_recycle_purge_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.HasCheckConstraint("ck_container_recycle_purge_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_container_recycle_purge_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.HasCheckConstraint("ck_container_recycle_purge_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> (command_json::jsonb->'Source'->>'OperationId')::uuid AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_recycle_purge_phase", "(state NOT IN ('Ready','Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.HasCheckConstraint("ck_container_recycle_purge_receipt_shape", "receipt_json IS NULL OR whaledeck_recycle_purge_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.HasCheckConstraint("ck_container_recycle_purge_reservation_shape", "reservation_json IS NULL OR whaledeck_recycle_purge_receipt_valid(reservation_json::jsonb,plan_json::jsonb)");
                table.HasCheckConstraint("ck_container_recycle_purge_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state NOT IN ('Reserving','Applying','Unknown') OR publish_barrier)");
                table.HasCheckConstraint("ck_container_recycle_purge_reserved", "(reservation_json IS NULL AND reservation_hash IS NULL) OR ((reservation_json IS NOT NULL AND reservation_hash ~ '^[A-F0-9]{64}$' AND root_started_at_utc IS NOT NULL AND reservation_json::jsonb->>'State'='Reserved' AND reservation_json::jsonb->>'Id'=execution_job_id::text AND reservation_json::jsonb->>'SourceOperationId'=command_json::jsonb->'Source'->>'OperationId' AND reservation_json::jsonb->>'Fingerprint'=plan_json::jsonb->>'Fingerprint') IS TRUE)");
                table.HasCheckConstraint("ck_container_recycle_purge_apply_entry", "(apply_started_at_utc IS NULL OR (root_started_at_utc IS NOT NULL AND reservation_json IS NOT NULL AND apply_started_at_utc>=root_started_at_utc AND apply_started_at_utc<execution_expires_at_utc)) AND (state<>'Applying' OR apply_started_at_utc IS NOT NULL) AND (root_started_at_utc IS NULL OR (root_started_at_utc>=confirmed_at_utc AND root_started_at_utc<execution_expires_at_utc))");
                table.HasCheckConstraint("ck_container_recycle_purge_receipt", "(receipt_json IS NULL AND receipt_hash IS NULL AND root_state IS NULL AND purge_applied IS NULL AND source_fenced IS NULL AND owned_fence IS NULL) OR ((receipt_json IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND root_state IN ('Completed','Aborted','Unknown') AND source_fenced IS NOT NULL AND owned_fence IS NOT NULL AND (NOT owned_fence OR source_fenced) AND CASE WHEN root_state='Completed' THEN purge_applied=true AND source_fenced AND owned_fence WHEN root_state='Unknown' THEN purge_applied IS NULL AND source_fenced ELSE purge_applied=false AND NOT owned_fence END) IS TRUE)");
                table.HasCheckConstraint("ck_container_recycle_purge_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id'=execution_job_id::text AND receipt_json::jsonb->>'SourceOperationId'=command_json::jsonb->'Source'->>'OperationId' AND receipt_json::jsonb->>'Fingerprint'=plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'State'=root_state AND (receipt_json::jsonb->>'PurgeApplied')::boolean IS NOT DISTINCT FROM purge_applied AND (receipt_json::jsonb->>'SourceFenced')::boolean=source_fenced AND (receipt_json::jsonb->>'OwnedFence')::boolean=owned_fence) IS TRUE)");
                table.HasCheckConstraint("ck_container_recycle_purge_result", "((state<>'Succeeded' OR root_state='Completed' AND purge_applied AND root_started_at_utc IS NOT NULL AND apply_started_at_utc IS NOT NULL AND reservation_json IS NOT NULL) AND (state<>'Unknown' OR root_state IS NULL OR root_state='Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state='Aborted') AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state='Aborted')) IS TRUE");
                table.HasCheckConstraint("ck_container_recycle_purge_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_container_recycle_purge_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActorSubject).HasMaxLength(256); entity.Property(r => r.AcknowledgedBy).HasMaxLength(256);
            entity.Property(r => r.TraceId).HasMaxLength(128); entity.Property(r => r.ContainerId).HasMaxLength(64); entity.Property(r => r.InstanceId).HasMaxLength(64);
            entity.Property(r => r.RelativePath).HasMaxLength(4096);
            entity.Property(r => r.CommandJson).HasColumnType("text"); entity.Property(r => r.PlanJson).HasColumnType("text"); entity.Property(r => r.AcknowledgementJson).HasColumnType("text");
            entity.Property(r => r.ReservationJson).HasColumnType("text"); entity.Property(r => r.ReceiptJson).HasColumnType("text"); entity.Property(r => r.RootState).HasMaxLength(16);
            entity.Property(r => r.State).HasMaxLength(16); entity.Property(r => r.ResultCode).HasMaxLength(64); entity.Property(r => r.Version).IsConcurrencyToken();
            foreach (var name in new[] { nameof(ContainerRecyclePurgeIntent.LoginBinding), nameof(ContainerRecyclePurgeIntent.OwnerHash), nameof(ContainerRecyclePurgeIntent.RequestHash),
                nameof(ContainerRecyclePurgeIntent.SourceProofHash), nameof(ContainerRecyclePurgeIntent.ReceiptHash), nameof(ContainerRecyclePurgeIntent.PlanHash), nameof(ContainerRecyclePurgeIntent.ExecutionHash), nameof(ContainerRecyclePurgeIntent.ReservationHash) }) entity.Property(name).HasMaxLength(64);
            entity.HasIndex(r => new { r.ActorSubject, r.IdempotencyKey }).IsUnique();
            entity.HasIndex(r=>r.SourceIntentId).IsUnique().HasFilter("completed_at_utc IS NULL OR root_state='Unknown' OR purge_applied IS TRUE");
            entity.HasIndex(r => r.ExecutionJobId).IsUnique().HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(r => r.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(r => new { r.ContainerId, r.CompletedAtUtc });
            entity.HasIndex(r => new { r.NextAttemptAtUtc, r.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(r => r.PublishBarrier).IsUnique().HasFilter("publish_barrier");
            entity.HasOne<ContainerRecycleIntent>().WithMany().HasForeignKey(r => r.SourceIntentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
