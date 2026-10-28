using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerDirectories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerDirectoryOperation>(entity =>
        {
            entity.ToTable("container_directory_operations", table =>
            {
                table.HasCheckConstraint("ck_container_directory_state", "state IN ('Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                table.HasCheckConstraint("ck_container_directory_terminal", "(completed_at_utc IS NULL) = (state IN ('Pending','Applying','Unknown'))");
                table.HasCheckConstraint("ck_container_directory_lifetime", "expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND version >= 0");
                table.HasCheckConstraint("ck_container_directory_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(plan_json) BETWEEN 1 AND 32768");
                table.HasCheckConstraint("ck_container_directory_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND plan_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_directory_barrier", "(NOT publish_barrier OR completed_at_utc IS NULL) AND (root_started_at_utc IS NULL OR publish_barrier OR completed_at_utc IS NOT NULL) AND (state <> 'Applying' OR (publish_barrier AND root_started_at_utc IS NOT NULL))");
                table.HasCheckConstraint("ck_container_directory_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.HasCheckConstraint("ck_container_directory_acknowledgement", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ActorSubject).HasMaxLength(256); entity.Property(item => item.AcknowledgedBy).HasMaxLength(256);
            entity.Property(item => item.TraceId).HasMaxLength(128); entity.Property(item => item.ContainerId).HasMaxLength(64);
            entity.Property(item => item.InstanceId).HasMaxLength(64); entity.Property(item => item.RelativePath).HasMaxLength(4096);
            entity.Property(item => item.PlanJson).HasColumnType("text"); entity.Property(item => item.AcknowledgementJson).HasColumnType("text");
            entity.Property(item => item.State).HasMaxLength(16); entity.Property(item => item.ResultCode).HasMaxLength(64);
            foreach (var name in new[] { nameof(ContainerDirectoryOperation.LoginBinding), nameof(ContainerDirectoryOperation.OwnerHash),
                nameof(ContainerDirectoryOperation.PlanHash), nameof(ContainerDirectoryOperation.RequestHash), nameof(ContainerDirectoryOperation.PublishedVersion) }) entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.ContainerId, item.CompletedAtUtc });
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => item.PublishBarrier).IsUnique().HasFilter("publish_barrier");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
