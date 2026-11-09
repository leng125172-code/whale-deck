using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceRunReportRecord> WorkspaceRunReports => Set<WorkspaceRunReportRecord>();

    private static void ConfigureWorkspaceRunReports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceRunReportRecord>(entity =>
        {
            entity.ToTable("workspace_run_reports", table =>
            {
                table.HasCheckConstraint("ck_workspace_report_identity",
                    "run_id <> '00000000-0000-0000-0000-000000000000'::uuid AND workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND run_id <> job_id AND schema_version > 0");
                table.HasCheckConstraint("ck_workspace_report_lifetime",
                    "retain_until_utc > finalized_at_utc AND retain_until_utc <= finalized_at_utc + INTERVAL '8760 hours'");
                table.HasCheckConstraint("ck_workspace_report_content", """
                    ((expired_at_utc IS NULL AND json_content IS NOT NULL AND markdown_content IS NOT NULL
                      AND octet_length(json_content) BETWEEN 1 AND 524288 AND octet_length(markdown_content) BETWEEN 1 AND 524288
                      AND json_sha256 = encode(sha256(json_content),'hex') AND markdown_sha256 = encode(sha256(markdown_content),'hex'))
                    OR (expired_at_utc >= retain_until_utc AND json_content IS NULL AND markdown_content IS NULL)) IS TRUE
                    """);
                table.HasCheckConstraint("ck_workspace_report_digests",
                    "json_sha256 ~ '^[a-f0-9]{64}$' AND markdown_sha256 ~ '^[a-f0-9]{64}$'");
            });
            entity.HasKey(row => row.RunId);
            entity.Property(row => row.RunId).ValueGeneratedNever();
            entity.Property(row => row.JsonSha256).HasMaxLength(64);
            entity.Property(row => row.MarkdownSha256).HasMaxLength(64);
            entity.HasIndex(row => row.JobId).IsUnique();
            entity.HasIndex(row => new { row.RetainUntilUtc, row.RunId }).HasFilter("expired_at_utc IS NULL");
            entity.HasOne<OperationJob>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
