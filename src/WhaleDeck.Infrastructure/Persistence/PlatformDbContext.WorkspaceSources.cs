using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceSourceIntent> WorkspaceSourceIntents => Set<WorkspaceSourceIntent>();
    public DbSet<WorkspaceSourceChunk> WorkspaceSourceChunks => Set<WorkspaceSourceChunk>();

    private static void ConfigureWorkspaceSources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceSourceIntent>(entity =>
        {
            entity.ToTable("development_workspace_source_imports", table =>
            {
                table.HasCheckConstraint("ck_workspace_source_archive",
                    """
                    received_bytes BETWEEN 0 AND archive_bytes AND (
                        (format IN ('zip','tar','tar.gz') AND archive_bytes BETWEEN 1 AND 134217728 AND archive_sha256 ~ '^[a-f0-9]{64}$')
                        OR (git_repository_id IS NOT NULL AND format = 'tar' AND archive_bytes = 0 AND archive_sha256 = ''
                            AND state IN ('Acquiring','Canceled','Failed'))
                    )
                    """);
                table.HasCheckConstraint("ck_workspace_source_state", """
                    state IN ('Uploading','Acquiring','Ready','Staged','Publishing','Canceling','Published','Canceled','Failed')
                    AND (state IN ('Published','Canceled','Failed')) = (finished_at_utc IS NOT NULL)
                    AND (lease IS NULL) = (lease_until_utc IS NULL)
                    AND (finished_at_utc IS NULL OR lease IS NULL)
                    AND (state NOT IN ('Ready','Staged','Publishing','Published') OR received_bytes = archive_bytes)
                    """);
                table.HasCheckConstraint("ck_workspace_source_git", """
                    (git_repository_id IS NULL AND git_ref IS NULL AND git_credential_ref IS NULL AND git_resolved_object IS NULL
                        AND git_commit IS NULL AND git_file_count IS NULL AND git_total_bytes IS NULL AND state <> 'Acquiring')
                    OR (git_repository_id IS NOT NULL AND git_repository_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
                        AND git_ref IS NOT NULL AND char_length(git_ref) BETWEEN 1 AND 255
                        AND (git_credential_ref IS NULL OR git_credential_ref ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
                        AND state <> 'Uploading' AND format = 'tar' AND (
                            (archive_bytes = 0 AND git_resolved_object IS NULL AND git_commit IS NULL
                                AND git_file_count IS NULL AND git_total_bytes IS NULL)
                            OR (archive_bytes > 0 AND git_resolved_object IS NOT NULL AND git_commit IS NOT NULL
                                AND git_resolved_object ~ '^[a-f0-9]{40}([a-f0-9]{24})?$' AND git_resolved_object ~ '[1-9a-f]'
                                AND git_commit ~ '^[a-f0-9]{40}([a-f0-9]{24})?$' AND git_commit ~ '[1-9a-f]'
                                AND length(git_resolved_object) = length(git_commit)
                                AND git_file_count IS NOT NULL AND git_file_count BETWEEN 1 AND 9999
                                AND git_total_bytes IS NOT NULL AND git_total_bytes BETWEEN 0 AND archive_bytes)
                        ))
                    """);
                table.HasCheckConstraint("ck_workspace_source_plan", """
                    (plan_sha256 IS NULL AND content_sha256 IS NULL AND file_count IS NULL AND total_bytes IS NULL
                        AND state NOT IN ('Staged','Publishing','Published'))
                    OR (plan_sha256 IS NOT NULL AND plan_sha256 ~ '^[a-f0-9]{64}$'
                        AND content_sha256 IS NOT NULL AND content_sha256 ~ '^[a-f0-9]{64}$'
                        AND file_count IS NOT NULL AND file_count BETWEEN 1 AND 9999
                        AND total_bytes IS NOT NULL AND total_bytes BETWEEN 0 AND 1073741824)
                    """);
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.OwnerSubject).HasMaxLength(256);
            entity.Property(row => row.LoginBinding).HasMaxLength(64);
            entity.Property(row => row.WorkspaceJson).HasColumnType("jsonb");
            entity.Property(row => row.Format).HasMaxLength(8);
            entity.Property(row => row.ArchiveSha256).HasMaxLength(64);
            entity.Property(row => row.GitRepositoryId).HasMaxLength(48);
            entity.Property(row => row.GitRef).HasMaxLength(255);
            entity.Property(row => row.GitCredentialRef).HasMaxLength(48);
            entity.Property(row => row.GitResolvedObject).HasMaxLength(64);
            entity.Property(row => row.GitCommit).HasMaxLength(64);
            entity.Property(row => row.State).HasMaxLength(16);
            entity.Property(row => row.PlanSha256).HasMaxLength(64);
            entity.Property(row => row.ContentSha256).HasMaxLength(64);
            entity.Property(row => row.Code).HasMaxLength(64);
            entity.HasOne<DevelopmentWorkspaceRecord>().WithMany().HasForeignKey(row => row.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.WorkspaceId).IsUnique().HasFilter("finished_at_utc IS NULL")
                .HasDatabaseName("uq_workspace_source_pending");
            entity.HasIndex(row => new { row.AvailableAtUtc, row.Id }).HasFilter("finished_at_utc IS NULL");
        });
        modelBuilder.Entity<WorkspaceSourceChunk>(entity =>
        {
            entity.ToTable("development_workspace_source_chunks", table =>
                table.HasCheckConstraint("ck_workspace_source_chunk",
                    "\"offset\" BETWEEN 0 AND 133169152 AND \"offset\" % 1048576 = 0 AND octet_length(sealed_content) BETWEEN 30 AND 1048605"));
            entity.HasKey(row => new { row.ImportId, row.Offset });
            entity.HasOne<WorkspaceSourceIntent>().WithMany().HasForeignKey(row => row.ImportId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
