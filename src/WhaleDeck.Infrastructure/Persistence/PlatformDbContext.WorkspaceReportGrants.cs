using Microsoft.EntityFrameworkCore;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed partial class PlatformDbContext
{
    public DbSet<WorkspaceReportGrantRecord> WorkspaceReportGrants => Set<WorkspaceReportGrantRecord>();

    private static void ConfigureWorkspaceReportGrants(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceReportGrantRecord>(entity =>
        {
            entity.ToTable("workspace_report_grants", table => table.HasCheckConstraint("ck_workspace_report_grant",
                "workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND group_id <> '00000000-0000-0000-0000-000000000000'::uuid AND version > 0"));
            entity.HasKey(row => new { row.WorkspaceId, row.GroupId });
        });
    }
}
