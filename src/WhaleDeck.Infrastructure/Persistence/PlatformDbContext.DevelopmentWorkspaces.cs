using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<DevelopmentWorkspaceRecord> DevelopmentWorkspaces => Set<DevelopmentWorkspaceRecord>();

    private static void ConfigureDevelopmentWorkspaces(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DevelopmentWorkspaceRecord>(entity =>
        {
            entity.ToTable("development_workspaces", table =>
            {
                table.HasCheckConstraint("ck_development_workspace_shape",
                    "state = 'Registered' AND version = 1 AND cpu_millis >= 100 AND memory_mib >= 128 AND disk_mib >= 256");
                table.HasCheckConstraint("ck_development_workspace_manifest",
                    "octet_length(environment_json) BETWEEN 1 AND 4096 AND environment_sha256 = encode(sha256(convert_to(environment_json,'UTF8')),'hex') AND request_sha256 ~ '^[a-f0-9]{64}$'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Slug).HasMaxLength(48).UseCollation("C");
            entity.Property(row => row.OwnerSubject).HasMaxLength(256);
            entity.Property(row => row.StorageRootId).HasMaxLength(48);
            entity.Property(row => row.State).HasMaxLength(24);
            entity.Property(row => row.EnvironmentSha256).HasMaxLength(64);
            entity.Property(row => row.RequestSha256).HasMaxLength(64);
            entity.HasIndex(row => row.Slug).IsUnique();
            entity.HasIndex(row => new { row.OwnerSubject, row.RegistrationKey }).IsUnique();
            entity.HasIndex(row => new { row.OwnerSubject, row.Slug });
        });
    }
}
