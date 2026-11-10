using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class WorkspaceArchiveWorkflow : Migration
{
    private static readonly string[] StepIdentityColumns = ["archive_id", "phase"];
    private static readonly string[] ScheduleColumns = ["available_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "development_workspace_archives",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                expected_epoch = table.Column<long>(type: "bigint", nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                phase = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                awaiting_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                authorization_required = table.Column<bool>(type: "boolean", nullable: false),
                cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                execution_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                capture_json = table.Column<string>(type: "jsonb", nullable: true),
                verify_json = table.Column<string>(type: "jsonb", nullable: true),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_archives", x => x.id);
                table.CheckConstraint("ck_workspace_archive_confirmation", "NOT awaiting_confirmation OR (phase = 'plan' AND plan_json IS NOT NULL AND finished_at_utc IS NULL)");
                table.CheckConstraint("ck_workspace_archive_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.CheckConstraint("ck_workspace_archive_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.CheckConstraint("ck_workspace_archive_phase", "phase IN ('plan','capture','verify')");
                table.CheckConstraint("ck_workspace_archive_snapshot", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384");
                table.ForeignKey(
                    name: "FK_development_workspace_archives_development_workspaces_works~",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_archives_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "development_workspace_archive_steps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                phase = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                request_json = table.Column<string>(type: "text", nullable: false),
                dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                proof_json = table.Column<string>(type: "jsonb", nullable: true),
                settled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_archive_steps", x => x.id);
                table.CheckConstraint("ck_workspace_archive_step_phase", "phase IN ('plan','capture','verify')");
                table.CheckConstraint("ck_workspace_archive_step_proof", "proof_json IS NULL OR octet_length(proof_json::text) BETWEEN 1 AND 32768");
                table.CheckConstraint("ck_workspace_archive_step_request", "octet_length(request_json) BETWEEN 1 AND 4096");
                table.CheckConstraint("ck_workspace_archive_step_settled", "settled_at_utc IS NULL OR proof_json IS NOT NULL");
                table.ForeignKey(
                    name: "FK_development_workspace_archive_steps_development_workspace_a~",
                    column: x => x.archive_id,
                    principalTable: "development_workspace_archives",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_archive_steps_archive_id_phase",
            table: "development_workspace_archive_steps",
            columns: StepIdentityColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_archives_available_at_utc_id",
            table: "development_workspace_archives",
            columns: ScheduleColumns,
            filter: "finished_at_utc IS NULL");

        // Both new and old API binaries serialize through the workspace
        // row. A lease timeout or Unknown result cannot release this fence.
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_workspace_archive_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE workspace_state text; idle_state text; idle_epoch bigint;
            BEGIN
                SELECT state, lifecycle_state, lifecycle_version INTO workspace_state, idle_state, idle_epoch
                    FROM development_workspaces WHERE id = NEW.workspace_id FOR UPDATE;
                IF NOT FOUND OR workspace_state <> 'StorageAllocated' OR idle_state <> 'Stopped' OR idle_epoch <> NEW.expected_epoch THEN
                    RAISE EXCEPTION 'workspace_archive_requires_stopped'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_workspace_archive_admission';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER workspace_archive_admission BEFORE INSERT ON development_workspace_archives
                FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_archive_admission();
            CREATE FUNCTION whaledeck_workspace_archive_fence() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_archives WHERE workspace_id = OLD.id AND finished_at_utc IS NULL) THEN
                    RAISE EXCEPTION 'workspace_archive_not_settled'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_workspace_archive_not_settled';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER workspace_archive_fence BEFORE UPDATE OR DELETE ON development_workspaces
                FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_archive_fence();
            """);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_archives_workspace_id",
            table: "development_workspace_archives",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_archives) THEN
                    RAISE EXCEPTION 'workspace_archive_history_present';
                END IF;
            END $$;
            DROP TRIGGER workspace_archive_fence ON development_workspaces;
            DROP FUNCTION whaledeck_workspace_archive_fence();
            DROP TRIGGER workspace_archive_admission ON development_workspace_archives;
            DROP FUNCTION whaledeck_workspace_archive_admission();
            """);
        migrationBuilder.DropTable(
            name: "development_workspace_archive_steps");

        migrationBuilder.DropTable(
            name: "development_workspace_archives");
    }
}
