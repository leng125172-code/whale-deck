using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class WorkspaceReportGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "workspace_report_grants",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                group_id = table.Column<Guid>(type: "uuid", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workspace_report_grants", x => new { x.workspace_id, x.group_id });
                table.CheckConstraint("ck_workspace_report_grant", "workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND group_id <> '00000000-0000-0000-0000-000000000000'::uuid AND version > 0");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE workspace_report_grants IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
                IF EXISTS (SELECT 1 FROM workspace_report_grants) THEN
                    RAISE EXCEPTION 'workspace_report_grant_history_present' USING ERRCODE='23514';
                END IF;
            END $f$;
            """);
        migrationBuilder.DropTable(
            name: "workspace_report_grants");
    }
}
