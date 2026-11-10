using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class WorkspaceRetirementExecution : Migration
{
    private static readonly string[] StepColumns = ["retirement_id", "phase"];
    private static readonly string[] ScheduleColumns = ["available_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces");

        migrationBuilder.CreateTable(
            name: "development_workspace_retirement_workflows",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                expected_epoch = table.Column<long>(type: "bigint", nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                archive_plan_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                capture_receipt_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                execution_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                phase = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                plan_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_json = table.Column<string>(type: "jsonb", nullable: true),
                authorization_required = table.Column<bool>(type: "boolean", nullable: false),
                cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                attention_required = table.Column<bool>(type: "boolean", nullable: false),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_retirement_workflows", x => x.id);
                table.CheckConstraint("ck_workspace_retire_digests", "archive_plan_sha256 ~ '^[a-f0-9]{64}$' AND capture_receipt_sha256 ~ '^[a-f0-9]{64}$'");
                table.CheckConstraint("ck_workspace_retire_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.CheckConstraint("ck_workspace_retire_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.CheckConstraint("ck_workspace_retire_phase", "phase IN ('plan','execute') AND (phase <> 'execute' OR confirmed_at_utc IS NOT NULL)");
                table.CheckConstraint("ck_workspace_retire_plan", "(plan_json IS NULL) = (plan_expires_at_utc IS NULL) AND (confirmed_at_utc IS NULL OR plan_json IS NOT NULL)");
                table.CheckConstraint("ck_workspace_retire_size", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384 AND (plan_json IS NULL OR octet_length(plan_json::text) BETWEEN 1 AND 8192) AND (execution_json IS NULL OR octet_length(execution_json::text) BETWEEN 1 AND 8192)");
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_workflows_development_work~",
                    column: x => x.archive_id,
                    principalTable: "development_workspace_archives",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_workflows_development_wor~1",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_workflows_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "development_workspace_retirement_steps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                retirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                phase = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                request_json = table.Column<string>(type: "text", nullable: false),
                dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                proof_json = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_retirement_steps", x => x.id);
                table.CheckConstraint("ck_workspace_retire_step_phase", "phase IN ('plan','execute')");
                table.CheckConstraint("ck_workspace_retire_step_proof", "proof_json IS NULL OR octet_length(proof_json::text) BETWEEN 1 AND 32768");
                table.CheckConstraint("ck_workspace_retire_step_request", "octet_length(request_json) BETWEEN 1 AND 4096");
                table.CheckConstraint("ck_workspace_retire_step_settled", "settled_at_utc IS NULL OR proof_json IS NOT NULL");
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_steps_development_workspac~",
                    column: x => x.retirement_id,
                    principalTable: "development_workspace_retirement_workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "development_workspace_retirement_completions",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                retirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                execute_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_retirement_completions", x => x.workspace_id);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_completions_development_wo~",
                    column: x => x.execute_step_id,
                    principalTable: "development_workspace_retirement_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_completions_development_w~1",
                    column: x => x.retirement_id,
                    principalTable: "development_workspace_retirement_workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_completions_development_w~2",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces",
            sql: "lifecycle_version >= 1 AND\n(CASE lifecycle_state\n    WHEN 'Active' THEN lifecycle_version % 2 = 1\n    WHEN 'Stopping' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Stopped' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Retiring' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Retired' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Resuming' THEN lifecycle_version >= 3 AND lifecycle_version % 2 = 1 AND state = 'StorageAllocated'\n    ELSE FALSE END) IS TRUE");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_completions_execute_step_id",
            table: "development_workspace_retirement_completions",
            column: "execute_step_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_completions_retirement_id",
            table: "development_workspace_retirement_completions",
            column: "retirement_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_steps_retirement_id_phase",
            table: "development_workspace_retirement_steps",
            columns: StepColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_workflows_archive_id",
            table: "development_workspace_retirement_workflows",
            column: "archive_id");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_workflows_available_at_utc~",
            table: "development_workspace_retirement_workflows",
            columns: ScheduleColumns,
            filter: "finished_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_workflows_workspace_id",
            table: "development_workspace_retirement_workflows",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");
        migrationBuilder.Sql(ExecutionGuards);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RemoveExecutionGuards);
        migrationBuilder.Sql(LegacyPair);
        migrationBuilder.Sql(LegacyAdmission);
        migrationBuilder.DropTable(
            name: "development_workspace_retirement_completions");

        migrationBuilder.DropTable(
            name: "development_workspace_retirement_steps");

        migrationBuilder.DropTable(
            name: "development_workspace_retirement_workflows");

        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces");

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces",
            sql: "lifecycle_version >= 1 AND\n(CASE lifecycle_state\n    WHEN 'Active' THEN lifecycle_version % 2 = 1\n    WHEN 'Stopping' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Stopped' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Retiring' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Resuming' THEN lifecycle_version >= 3 AND lifecycle_version % 2 = 1 AND state = 'StorageAllocated'\n    ELSE FALSE END) IS TRUE");
    }
}
