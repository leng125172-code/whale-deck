using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerMoves(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerMoveIntent>(entity =>
        {
            entity.ToTable("container_move_intents", table =>
            {
                table.HasCheckConstraint("ck_container_move_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.HasCheckConstraint("ck_container_move_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown'))");
                table.HasCheckConstraint("ck_container_move_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(source_path) BETWEEN 1 AND 4096 AND octet_length(target_path) BETWEEN 1 AND 4096 AND source_path <> target_path AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.HasCheckConstraint("ck_container_move_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.HasCheckConstraint("ck_container_move_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_move_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.HasCheckConstraint("ck_container_move_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_move_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.HasCheckConstraint("ck_container_move_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state <> 'Applying' OR publish_barrier)");
                table.HasCheckConstraint("ck_container_move_result", "(state NOT IN ('Succeeded','Superseded') OR published_version IS NOT NULL AND root_started_at_utc IS NOT NULL AND NOT private_may_exist) AND (published_version IS NULL OR state IN ('Succeeded','Superseded')) AND (state <> 'Unknown' OR private_may_exist)");
                table.HasCheckConstraint("ck_container_move_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_container_move_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.HasCheckConstraint("ck_container_move_facts", "(state NOT IN ('Succeeded','Superseded') OR (destination_published IS TRUE AND source_removed IS TRUE AND source_retained IS TRUE)) AND (execution_job_id IS NOT NULL OR (destination_published IS NULL AND source_removed IS NULL AND source_retained IS NULL)) AND (root_started_at_utc IS NOT NULL OR (destination_published IS NOT TRUE AND source_removed IS NOT TRUE AND source_retained IS NOT TRUE)) AND (execution_job_id IS NULL OR state NOT IN ('Failed','Canceled') OR (destination_published IS FALSE AND source_removed IS FALSE AND source_retained IS FALSE))");
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ActorSubject).HasMaxLength(256); entity.Property(r => r.AcknowledgedBy).HasMaxLength(256);
            entity.Property(r => r.TraceId).HasMaxLength(128); entity.Property(r => r.ContainerId).HasMaxLength(64); entity.Property(r => r.InstanceId).HasMaxLength(64);
            entity.Property(r => r.SourcePath).HasMaxLength(4096); entity.Property(r => r.TargetPath).HasMaxLength(4096);
            entity.Property(r => r.CommandJson).HasColumnType("text"); entity.Property(r => r.PlanJson).HasColumnType("text"); entity.Property(r => r.AcknowledgementJson).HasColumnType("text");
            entity.Property(r => r.State).HasMaxLength(16); entity.Property(r => r.ResultCode).HasMaxLength(64); entity.Property(r => r.Version).IsConcurrencyToken();
            foreach (var name in new[] { nameof(ContainerMoveIntent.LoginBinding), nameof(ContainerMoveIntent.OwnerHash), nameof(ContainerMoveIntent.RequestHash),
                nameof(ContainerMoveIntent.PlanHash), nameof(ContainerMoveIntent.ExecutionHash), nameof(ContainerMoveIntent.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
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
