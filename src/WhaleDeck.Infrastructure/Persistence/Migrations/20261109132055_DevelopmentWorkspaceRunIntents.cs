using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceRunIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "development_workspace_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                runtime = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                task = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_runs", x => x.id);
                table.CheckConstraint("ck_workspace_run_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.ForeignKey(
                    name: "FK_development_workspace_runs_development_workspaces_workspace~",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_runs_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_runs_available_at_utc_id",
            table: "development_workspace_runs",
            columns: ["available_at_utc", "id"],
            filter: "finished_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_workspace_run_pending",
            table: "development_workspace_runs",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_runs) THEN
                    RAISE EXCEPTION 'development_workspace_run_history_present';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(
            name: "development_workspace_runs");
    }
}
