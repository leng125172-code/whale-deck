using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<ContainerFileVersion> ContainerFileVersions => Set<ContainerFileVersion>();
    private static void ConfigureContainerFileHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerFileVersion>(entity =>
        {
            entity.ToTable("container_file_versions", table =>
            {
                table.HasCheckConstraint("ck_container_history_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(original_instance_id) BETWEEN 22 AND 64");
                table.HasCheckConstraint("ck_container_history_hashes", "source_version ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND capture_hash ~ '^[A-F0-9]{64}$'");
                table.HasCheckConstraint("ck_container_history_limits", "size_bytes BETWEEN 0 AND 1073741824 AND captured_at_unix_ms BETWEEN 0 AND 253402300799999 AND version>=0");
                table.HasCheckConstraint("ck_container_history_availability", "availability IN ('Present','Incomplete','Missing','Invalid')");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ContainerId).HasMaxLength(64); entity.Property(item => item.OriginalInstanceId).HasMaxLength(64);
            entity.Property(item => item.RelativePath).HasMaxLength(4096); entity.Property(item => item.Availability).HasMaxLength(16);
            entity.Property(item => item.SourceVersion).HasMaxLength(64); entity.Property(item => item.Sha256).HasMaxLength(64); entity.Property(item => item.CaptureHash).HasMaxLength(64);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.ContainerId, item.Id });
            entity.HasIndex(item => new { item.ContainerId, item.CapturedAtUnixMs, item.Id });
        });
    }
}
