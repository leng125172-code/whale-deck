using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerRecycleArchiveRetirement(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerRecycleArchiveRetirementIntent>(entity =>
        {
            entity.ToTable("container_recycle_archive_retirement_intents", table =>
            {
                table.HasCheckConstraint("ck_archive_retirement_identity",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid AND archive_intent_id <> id AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND retain_days BETWEEN 1 AND 3650 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128");
                table.HasCheckConstraint("ck_archive_retirement_hashes",
                    "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_proof_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_archive_retirement_lifetime",
                    "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0 AND (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc)");
                table.HasCheckConstraint("ck_archive_retirement_state",
                    "state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged') AND (completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown'))");
                table.HasCheckConstraint("ck_archive_retirement_plan", """
                    (plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL)
                    OR ((octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash ~ '^[A-F0-9]{64}$'
                    AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes'
                    AND whaledeck_archive_retirement_plan_valid(plan_json::jsonb, retain_days)) IS TRUE)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_execution", """
                    (execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL)
                    OR ((execution_job_id IS NOT NULL AND execution_job_id NOT IN (id, archive_intent_id, '00000000-0000-0000-0000-000000000000'::uuid)
                    AND plan_json IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc
                    AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash ~ '^[A-F0-9]{64}$') IS TRUE)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_phase", """
                    (state NOT IN ('Ready','Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)
                    AND (state NOT IN ('Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)
                    AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_barrier", """
                    (publish_barrier = (state IN ('Reserving','Applying','Unknown')))
                    AND (NOT publish_barrier OR root_started_at_utc IS NOT NULL)
                    AND (root_started_at_utc IS NULL OR ((publish_barrier OR completed_at_utc IS NOT NULL)
                    AND root_started_at_utc >= confirmed_at_utc AND root_started_at_utc < execution_expires_at_utc) IS TRUE)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_reservation", """
                    (reservation_json IS NULL AND reservation_hash IS NULL)
                    OR ((octet_length(reservation_json) BETWEEN 1 AND 32768 AND reservation_hash ~ '^[A-F0-9]{64}$'
                    AND root_started_at_utc IS NOT NULL AND reservation_json::jsonb->>'State' = 'Reserved'
                    AND whaledeck_archive_retirement_receipt_valid(reservation_json::jsonb, plan_json::jsonb, execution_job_id)) IS TRUE)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_apply", """
                    (apply_started_at_utc IS NULL OR (reservation_json IS NOT NULL AND apply_started_at_utc >= root_started_at_utc
                    AND apply_started_at_utc < execution_expires_at_utc
                    AND extract(epoch FROM apply_started_at_utc) * 1000 >= (plan_json::jsonb->>'NotBeforeUnixMs')::numeric) IS TRUE)
                    AND (state <> 'Applying' OR apply_started_at_utc IS NOT NULL)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_receipt", """
                    (receipt_json IS NULL AND receipt_hash IS NULL AND root_state IS NULL AND retirement_applied IS NULL)
                    OR ((octet_length(receipt_json) BETWEEN 1 AND 32768 AND receipt_hash ~ '^[A-F0-9]{64}$'
                    AND root_state IN ('Completed','Aborted','Unknown') AND receipt_json::jsonb->>'State' = root_state
                    AND (receipt_json::jsonb->>'RetirementApplied')::boolean IS NOT DISTINCT FROM retirement_applied
                    AND whaledeck_archive_retirement_receipt_valid(receipt_json::jsonb, plan_json::jsonb, execution_job_id)) IS TRUE)
                    """);
                table.HasCheckConstraint("ck_archive_retirement_result", """
                    ((root_state IS DISTINCT FROM 'Completed' OR apply_started_at_utc IS NOT NULL)
                    AND (state <> 'Succeeded' OR root_state = 'Completed')
                    AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown')
                    AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed','Expired') OR root_state = 'Aborted')
                    AND (state IN ('Succeeded','Unknown','Canceled','Failed','Expired','Acknowledged') OR root_state IS NULL)) IS TRUE
                    """);
                table.HasCheckConstraint("ck_archive_retirement_lease",
                    "(lease_token IS NULL) = (lease_expires_at_utc IS NULL) AND (state NOT IN ('Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged') OR lease_token IS NULL)");
                table.HasCheckConstraint("ck_archive_retirement_ack", """
                    (state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL)
                    AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL)
                    AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND length(acknowledged_by) > 0
                    AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)
                    """);
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActorSubject).HasMaxLength(256);
            entity.Property(r => r.AcknowledgedBy).HasMaxLength(256);
            entity.Property(r => r.TraceId).HasMaxLength(128);
            entity.Property(r => r.ResultCode).HasMaxLength(64);
            entity.Property(r => r.State).HasMaxLength(16);
            entity.Property(r => r.RootState).HasMaxLength(16);
            entity.Property(r => r.Version).IsConcurrencyToken();
            foreach (var name in new[] { nameof(ContainerRecycleArchiveRetirementIntent.LoginBinding), nameof(ContainerRecycleArchiveRetirementIntent.OwnerHash),
                nameof(ContainerRecycleArchiveRetirementIntent.RequestHash), nameof(ContainerRecycleArchiveRetirementIntent.SourceProofHash), nameof(ContainerRecycleArchiveRetirementIntent.PlanHash),
                nameof(ContainerRecycleArchiveRetirementIntent.ExecutionHash), nameof(ContainerRecycleArchiveRetirementIntent.ReservationHash), nameof(ContainerRecycleArchiveRetirementIntent.ReceiptHash) })
                entity.Property(name).HasMaxLength(64);
            entity.HasIndex(r => new { r.ActorSubject, r.LoginBinding, r.IdempotencyKey }).IsUnique().HasDatabaseName("ix_archive_retirement_actor_key");
            entity.HasIndex(r => r.ArchiveIntentId, "ActiveArchiveRetirement").IsUnique().HasDatabaseName("ix_archive_retirement_active_archive")
                .HasFilter("completed_at_utc IS NULL OR root_state IN ('Unknown','Completed')");
            entity.HasIndex(r => r.ArchiveIntentId).HasDatabaseName("ix_archive_retirement_archive");
            entity.HasIndex(r => r.ExecutionJobId).IsUnique().HasDatabaseName("ix_archive_retirement_job").HasFilter("execution_job_id IS NOT NULL");
            entity.HasIndex(r => r.LeaseToken).IsUnique().HasDatabaseName("ix_archive_retirement_lease").HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(r => new { r.NextAttemptAtUtc, r.Id }).HasDatabaseName("ix_archive_retirement_due").HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(r => r.PublishBarrier).IsUnique().HasDatabaseName("ix_archive_retirement_barrier").HasFilter("publish_barrier");
            entity.HasOne<ContainerRecycleArchiveIntent>().WithMany().HasForeignKey(r => r.ArchiveIntentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(r => r.ExecutionJobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
