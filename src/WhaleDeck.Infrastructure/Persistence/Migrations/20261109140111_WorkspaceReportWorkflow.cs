using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class WorkspaceReportWorkflow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_report_identity",
            table: "workspace_run_reports");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_run_lease",
            table: "development_workspace_runs");

        migrationBuilder.AddColumn<string>(
            name: "report_stage",
            table: "development_workspace_runs",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_report_identity",
            table: "workspace_run_reports",
            sql: "run_id <> '00000000-0000-0000-0000-000000000000'::uuid AND workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND schema_version > 0");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_runs_report_stage_available_at_utc_id",
            table: "development_workspace_runs",
            columns: ["report_stage", "available_at_utc", "id"],
            filter: "report_stage IN ('Collect', 'Retire')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_run_lease",
            table: "development_workspace_runs",
            sql: "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL OR report_stage IN ('Collect', 'Retire')) IS TRUE");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_run_report_stage",
            table: "development_workspace_runs",
            sql: "report_stage IS NULL OR (finished_at_utc IS NOT NULL AND report_stage IN ('Collect', 'Retire', 'Complete', 'Attention'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_runs WHERE report_stage IS NOT NULL
                    OR snapshot_json -> 'command' ->> 'reportRetentionDays' IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM workspace_run_reports WHERE run_id = job_id) THEN
                    RAISE EXCEPTION 'workspace_report_workflow_history_present';
                END IF;
            END $$;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_report_identity",
            table: "workspace_run_reports");

        migrationBuilder.DropIndex(
            name: "IX_development_workspace_runs_report_stage_available_at_utc_id",
            table: "development_workspace_runs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_run_lease",
            table: "development_workspace_runs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_run_report_stage",
            table: "development_workspace_runs");

        migrationBuilder.DropColumn(
            name: "report_stage",
            table: "development_workspace_runs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_report_identity",
            table: "workspace_run_reports",
            sql: "run_id <> '00000000-0000-0000-0000-000000000000'::uuid AND workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND run_id <> job_id AND schema_version > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_run_lease",
            table: "development_workspace_runs",
            sql: "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
    }
}
