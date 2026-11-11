using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class WorkspaceRetirementRecovery : Migration
{
    private static readonly string[] AttemptColumns = ["retirement_id", "phase", "attempt"];
    private static readonly string[] RecoveryKeyColumns = ["retirement_id", "idempotency_key"];
    private static readonly string[] LegacyStepColumns = ["retirement_id", "phase"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~1",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropIndex(
            name: "IX_development_workspace_retirement_steps_retirement_id_phase",
            table: "development_workspace_retirement_steps");

        migrationBuilder.AddColumn<Guid>(
            name: "execute_step_id",
            table: "development_workspace_retirement_workflows",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "execution_attempt",
            table: "development_workspace_retirement_workflows",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "attempt",
            table: "development_workspace_retirement_steps",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.Sql(BackfillCurrentStep);

        migrationBuilder.CreateTable(
            name: "development_workspace_retirement_recoveries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                retirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                previous_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                previous_proof_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                plan_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_retirement_recoveries", x => x.id);
                table.CheckConstraint("ck_workspace_retire_recovery_binding", "attempt BETWEEN 1 AND 1024 AND id <> previous_step_id AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(actor_subject)) > 0 AND login_binding ~ '^[a-fA-F0-9]{64}$' AND previous_proof_sha256 ~ '^[a-f0-9]{64}$' AND plan_sha256 ~ '^[a-f0-9]{64}$'");
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_recoveries_development_wor~",
                    column: x => x.id,
                    principalTable: "development_workspace_retirement_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_recoveries_development_wo~1",
                    column: x => x.previous_step_id,
                    principalTable: "development_workspace_retirement_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirement_recoveries_development_wo~2",
                    column: x => x.retirement_id,
                    principalTable: "development_workspace_retirement_workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_workflows_execute_step_id",
            table: "development_workspace_retirement_workflows",
            column: "execute_step_id");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_retire_attempt",
            table: "development_workspace_retirement_workflows",
            sql: "execution_attempt BETWEEN 0 AND 1024");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_steps_retirement_id_phase_~",
            table: "development_workspace_retirement_steps",
            columns: AttemptColumns,
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_retire_step_attempt",
            table: "development_workspace_retirement_steps",
            sql: "attempt BETWEEN 0 AND 1024 AND (phase <> 'plan' OR attempt = 0)");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_recoveries_previous_step_id",
            table: "development_workspace_retirement_recoveries",
            column: "previous_step_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_recoveries_retirement_id_i~",
            table: "development_workspace_retirement_recoveries",
            columns: RecoveryKeyColumns,
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~1",
            table: "development_workspace_retirement_workflows",
            column: "execute_step_id",
            principalTable: "development_workspace_retirement_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~2",
            table: "development_workspace_retirement_workflows",
            column: "workspace_id",
            principalTable: "development_workspaces",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql(RecoveryGuards);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RemoveRecoveryGuards);
        migrationBuilder.DropForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~1",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~2",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropTable(
            name: "development_workspace_retirement_recoveries");

        migrationBuilder.DropIndex(
            name: "IX_development_workspace_retirement_workflows_execute_step_id",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_retire_attempt",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropIndex(
            name: "IX_development_workspace_retirement_steps_retirement_id_phase_~",
            table: "development_workspace_retirement_steps");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_retire_step_attempt",
            table: "development_workspace_retirement_steps");

        migrationBuilder.DropColumn(
            name: "execute_step_id",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropColumn(
            name: "execution_attempt",
            table: "development_workspace_retirement_workflows");

        migrationBuilder.DropColumn(
            name: "attempt",
            table: "development_workspace_retirement_steps");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirement_steps_retirement_id_phase",
            table: "development_workspace_retirement_steps",
            columns: LegacyStepColumns,
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_development_workspace_retirement_workflows_development_wor~1",
            table: "development_workspace_retirement_workflows",
            column: "workspace_id",
            principalTable: "development_workspaces",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}
