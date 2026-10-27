using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    private static void ConfigureContainerFileUploads(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerFileUploadOperation>(entity =>
        {
            entity.ToTable("container_file_upload_operations", table =>
            {
                table.HasCheckConstraint("ck_container_upload_state", "state IN ('AwaitingUpload','Receiving','Ready','Publishing','Unknown','Succeeded','Superseded','Canceled','Failed')");
                table.HasCheckConstraint("ck_container_upload_terminal", "(completed_at_utc IS NULL) = (state IN ('AwaitingUpload','Receiving','Ready','Publishing','Unknown'))");
                table.HasCheckConstraint("ck_container_upload_limits", "size_bytes BETWEEN 0 AND 1073741824 AND previous_size_bytes BETWEEN 0 AND 1073741824 AND version >= 0 AND expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00'");
                table.HasCheckConstraint("ck_container_upload_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128");
                table.HasCheckConstraint("ck_container_upload_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND target_proof ~ '^[A-F0-9]{64}$' AND expected_parent_version ~ '^[A-F0-9]{64}$' AND plan_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND (expected_version ~ '^[A-F0-9]{64}$' OR (expected_version='ABSENT' AND previous_size_bytes=0)) AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_container_upload_barrier", "(NOT publish_barrier OR completed_at_utc IS NULL) AND (root_started_at_utc IS NULL OR publish_barrier OR completed_at_utc IS NOT NULL) AND (state <> 'Publishing' OR (publish_barrier AND root_started_at_utc IS NOT NULL))");
                table.HasCheckConstraint("ck_container_upload_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL) AND (state <> 'Receiving' OR receive_token IS NOT NULL)");
                table.HasCheckConstraint("ck_container_upload_history_source", "history_source_json IS NULL OR octet_length(history_source_json) BETWEEN 1 AND 32768");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ActorSubject).HasMaxLength(256); entity.Property(item => item.TraceId).HasMaxLength(128);
            entity.Property(item => item.ContainerId).HasMaxLength(64); entity.Property(item => item.InstanceId).HasMaxLength(64);
            entity.Property(item => item.RelativePath).HasMaxLength(4096); entity.Property(item => item.State).HasMaxLength(16);
            entity.Property(item => item.ResultCode).HasMaxLength(64);
            // Text preserves exact authenticated canonical JSON; jsonb would
            // normalize whitespace/order and invalidate the immutable MAC.
            entity.Property(item => item.HistorySourceJson).HasColumnType("text");
            foreach (var name in new[] { nameof(ContainerFileUploadOperation.LoginBinding), nameof(ContainerFileUploadOperation.OwnerHash),
                nameof(ContainerFileUploadOperation.TargetProof), nameof(ContainerFileUploadOperation.ExpectedParentVersion),
                nameof(ContainerFileUploadOperation.ExpectedVersion), nameof(ContainerFileUploadOperation.PlanHash),
                nameof(ContainerFileUploadOperation.RequestHash), nameof(ContainerFileUploadOperation.Sha256), nameof(ContainerFileUploadOperation.PublishedVersion) })
                entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ActorSubject, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.ContainerId, item.CompletedAtUtc });
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => item.PublishBarrier).IsUnique().HasFilter("publish_barrier");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.Id).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
