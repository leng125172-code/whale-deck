using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<FileArchiveRegistration> FileArchiveRegistrations => Set<FileArchiveRegistration>();
    private static void ConfigureFileArchives(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileArchiveRegistration>(entity =>
        {
            entity.ToTable("file_archive_registrations", table =>
            {
                table.HasCheckConstraint("ck_file_archive_registration_limits", "id<>'00000000-0000-0000-0000-000000000000'::uuid AND expires_at_utc>created_at_utc AND octet_length(plan_json::text)<=32768");
                table.HasCheckConstraint("ck_file_archive_registration_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.LoginBinding).HasMaxLength(64); entity.Property(item => item.IdempotencyKey).HasMaxLength(36);
            entity.Property(item => item.RequestHash).HasMaxLength(64); entity.Property(item => item.PlanJson).HasColumnType("jsonb");
            entity.HasIndex(item => item.ExpiresAtUtc);
            entity.HasIndex(item => new { item.ActorSubject, item.LoginBinding, item.IdempotencyKey }).IsUnique();
        });
        modelBuilder.Entity<FileVersion>(entity =>
        {
            entity.ToTable("file_versions", table =>
            {
                table.HasCheckConstraint("ck_file_version_capture", "(capture_kind='Trash' AND capture_state IN ('Pending','Trashed','Restored','Canceled','Purged')) OR (capture_kind='Overwrite' AND capture_state IN ('Available','Purged'))");
                table.HasCheckConstraint("ck_file_version_availability", "availability IN ('Present','Missing','Incomplete','Invalid','Purged') AND ((capture_state='Purged')=(availability='Purged'))");
                table.HasCheckConstraint("ck_file_version_limits", "size_bytes BETWEEN 0 AND 1073741824 AND version>=0 AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND captured_at_utc >= TIMESTAMPTZ '1970-01-01 00:00:00+00'");
                table.HasCheckConstraint("ck_file_version_hashes", "target_hash ~ '^[A-F0-9]{64}$' AND source_version ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_file_version_actor", "(source_job_id IS NULL)=(created_by_subject IS NULL) AND (source_job_id IS NULL OR source_job_id=id)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RootId).HasMaxLength(64); entity.Property(item => item.RelativePath).HasMaxLength(4096);
            entity.Property(item => item.CreatedBySubject).HasMaxLength(256);
            entity.Property(item => item.TargetHash).HasMaxLength(64); entity.Property(item => item.SourceVersion).HasMaxLength(64); entity.Property(item => item.Sha256).HasMaxLength(64);
            entity.Property(item => item.CaptureKind).HasMaxLength(16); entity.Property(item => item.CaptureState).HasMaxLength(16); entity.Property(item => item.Availability).HasMaxLength(16);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.RootId, item.TargetHash, item.CapturedAtUtc, item.Id });
            entity.HasIndex(item => new { item.RootId, item.CaptureKind, item.CaptureState, item.CapturedAtUtc, item.Id });
            entity.HasIndex(item => new { item.Availability, item.LastObservedAtUtc });
            entity.HasIndex(item => item.SourceJobId).IsUnique().HasFilter("source_job_id IS NOT NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.SourceJobId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(item => item.LastOperationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<FileArchiveOperation>(entity =>
        {
            entity.ToTable("file_archive_operations", table =>
            {
                table.HasCheckConstraint("ck_file_archive_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                table.HasCheckConstraint("ck_file_archive_kind", "(kind='trash' AND archive_version_id IS NULL) OR (kind IN ('restore','purge') AND archive_version_id IS NOT NULL)");
                table.HasCheckConstraint("ck_file_archive_limits", "size_bytes BETWEEN 0 AND 1073741824 AND version>=0 AND expires_at_utc>created_at_utc AND octet_length(plan_json::text)<=32768 AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096");
                table.HasCheckConstraint("ck_file_archive_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND (expected_version='ABSENT' OR expected_version ~ '^[A-F0-9]{64}$')");
                table.HasCheckConstraint("ck_file_archive_terminal", "(completed_at_utc IS NULL)=(state IN ('Pending','Running','Unknown'))");
                table.HasCheckConstraint("ck_file_archive_source", "(kind='trash' AND expected_version<>'ABSENT') OR kind='restore' OR (kind='purge' AND expected_version='ABSENT')");
            });
            entity.HasKey(item => item.Id); entity.Property(item => item.ActorSubject).HasMaxLength(256);
            entity.Property(item => item.Kind).HasMaxLength(16); entity.Property(item => item.State).HasMaxLength(16);
            entity.Property(item => item.RootId).HasMaxLength(64); entity.Property(item => item.RelativePath).HasMaxLength(4096);
            entity.Property(item => item.PlanJson).HasColumnType("jsonb"); entity.Property(item => item.ResultCode).HasMaxLength(64);
            foreach (var name in new[] { nameof(FileArchiveOperation.LoginBinding), nameof(FileArchiveOperation.OwnerHash), nameof(FileArchiveOperation.RequestHash),
                nameof(FileArchiveOperation.TargetHash), nameof(FileArchiveOperation.ExpectedVersion), nameof(FileArchiveOperation.Sha256), nameof(FileArchiveOperation.PublishedVersion) })
                entity.Property(name).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.LeaseToken).IsUnique().HasFilter("lease_token IS NOT NULL");
            entity.HasIndex(item => new { item.NextAttemptAtUtc, item.Id }).HasFilter("completed_at_utc IS NULL");
            entity.HasIndex(item => new { item.ActorSubject, item.CreatedAtUtc });
            entity.HasIndex(item => item.TargetHash).HasFilter("completed_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithOne().HasForeignKey<FileArchiveOperation>(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FileArchiveRegistration>().WithOne().HasForeignKey<FileArchiveOperation>(item => item.Id).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FileVersion>().WithMany().HasForeignKey(item => item.ArchiveVersionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
