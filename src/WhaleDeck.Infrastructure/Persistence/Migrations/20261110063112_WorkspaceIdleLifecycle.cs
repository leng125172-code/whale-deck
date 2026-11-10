using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class WorkspaceIdleLifecycle : Migration
{
    private static readonly string[] ScheduleColumns = ["available_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "lifecycle_state",
            table: "development_workspaces",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Active");

        migrationBuilder.AddColumn<long>(
            name: "lifecycle_version",
            table: "development_workspaces",
            type: "bigint",
            nullable: false,
            defaultValue: 1L);

        migrationBuilder.CreateTable(
            name: "development_workspace_lifecycle",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                action = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                expected_version = table.Column<long>(type: "bigint", nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                execution_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_lifecycle", x => x.id);
                table.CheckConstraint("ck_workspace_lifecycle_action", "action IN ('stop','resume') AND expected_version >= 1");
                table.CheckConstraint("ck_workspace_lifecycle_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.CheckConstraint("ck_workspace_lifecycle_snapshot", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384");
                table.ForeignKey(
                    name: "FK_development_workspace_lifecycle_development_workspaces_work~",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_lifecycle_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces",
            sql: "lifecycle_version >= 1 AND\n(CASE lifecycle_state\n    WHEN 'Active' THEN lifecycle_version % 2 = 1\n    WHEN 'Stopping' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Stopped' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Resuming' THEN lifecycle_version >= 3 AND lifecycle_version % 2 = 1 AND state = 'StorageAllocated'\n    ELSE FALSE END) IS TRUE");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_lifecycle_available_at_utc_id",
            table: "development_workspace_lifecycle",
            columns: ScheduleColumns,
            filter: "finished_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_lifecycle_workspace_id",
            table: "development_workspace_lifecycle",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");

        // Older application binaries must also honor a persisted stop.
        // The shared row lock closes their check/insert race with admission.
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_workspace_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE current_lifecycle text;
            BEGIN
                SELECT lifecycle_state INTO current_lifecycle FROM development_workspaces
                    WHERE id = NEW.workspace_id FOR SHARE;
                IF FOUND AND current_lifecycle <> 'Active' THEN
                    RAISE EXCEPTION 'workspace_lifecycle_inactive'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_workspace_lifecycle_admission';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER workspace_run_admission BEFORE INSERT ON development_workspace_runs
                FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_admission();
            CREATE TRIGGER workspace_source_admission BEFORE INSERT ON development_workspace_source_imports
                FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_admission();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_lifecycle)
                    OR EXISTS (SELECT 1 FROM development_workspaces WHERE lifecycle_version <> 1 OR lifecycle_state <> 'Active') THEN
                    RAISE EXCEPTION 'workspace_lifecycle_history_present';
                END IF;
            END $$;
            DROP TRIGGER workspace_run_admission ON development_workspace_runs;
            DROP TRIGGER workspace_source_admission ON development_workspace_source_imports;
            DROP FUNCTION whaledeck_workspace_admission();
            """);
        migrationBuilder.DropTable(
            name: "development_workspace_lifecycle");

        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces");

        migrationBuilder.DropColumn(
            name: "lifecycle_state",
            table: "development_workspaces");

        migrationBuilder.DropColumn(
            name: "lifecycle_version",
            table: "development_workspaces");
    }
}
