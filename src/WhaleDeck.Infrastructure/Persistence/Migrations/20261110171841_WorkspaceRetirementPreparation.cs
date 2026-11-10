using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class WorkspaceRetirementPreparation : Migration
{
    private static readonly string[] ScheduleColumns = ["available_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces");

        migrationBuilder.CreateTable(
            name: "development_workspace_retirements",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                archive_id = table.Column<Guid>(type: "uuid", nullable: false),
                expected_epoch = table.Column<long>(type: "bigint", nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                execution_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                plan_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_retirements", x => x.id);
                table.CheckConstraint("ck_workspace_retirement_epoch", "expected_epoch >= 2 AND expected_epoch % 2 = 0");
                table.CheckConstraint("ck_workspace_retirement_lease", "(lease IS NULL) = (lease_until_utc IS NULL) AND (finished_at_utc IS NULL OR lease IS NULL)");
                table.CheckConstraint("ck_workspace_retirement_plan", "(plan_json IS NULL) = (plan_expires_at_utc IS NULL) AND (confirmed_at_utc IS NULL OR plan_json IS NOT NULL)");
                table.CheckConstraint("ck_workspace_retirement_size", "octet_length(snapshot_json::text) BETWEEN 1 AND 16384 AND (plan_json IS NULL OR octet_length(plan_json::text) BETWEEN 1 AND 8192)");
                table.ForeignKey(
                    name: "FK_development_workspace_retirements_development_workspace_arc~",
                    column: x => x.archive_id,
                    principalTable: "development_workspace_archives",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirements_development_workspaces_wo~",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_development_workspace_retirements_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces",
            sql: "lifecycle_version >= 1 AND\n(CASE lifecycle_state\n    WHEN 'Active' THEN lifecycle_version % 2 = 1\n    WHEN 'Stopping' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Stopped' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Retiring' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Resuming' THEN lifecycle_version >= 3 AND lifecycle_version % 2 = 1 AND state = 'StorageAllocated'\n    ELSE FALSE END) IS TRUE");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirements_archive_id",
            table: "development_workspace_retirements",
            column: "archive_id");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirements_available_at_utc_id",
            table: "development_workspace_retirements",
            columns: ScheduleColumns,
            filter: "finished_at_utc IS NULL AND plan_json IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_retirements_workspace_id",
            table: "development_workspace_retirements",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");

    migrationBuilder.Sql("""
        CREATE FUNCTION whaledeck_workspace_retirement_admission() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE w development_workspaces%ROWTYPE;
        BEGIN
            SELECT * INTO w FROM development_workspaces WHERE id=NEW.workspace_id FOR UPDATE;
            IF NOT FOUND OR w.state <> 'StorageAllocated' OR w.lifecycle_state NOT IN ('Stopped','Retiring')
                OR w.lifecycle_version <> NEW.expected_epoch OR NOT EXISTS (
                    SELECT 1 FROM development_workspace_archives a JOIN operation_jobs j ON j.id=a.id
                    WHERE a.id=NEW.archive_id AND a.workspace_id=NEW.workspace_id AND a.expected_epoch=NEW.expected_epoch
                      AND a.finished_at_utc IS NOT NULL AND a.phase='verify' AND a.lease IS NULL
                      AND a.lease_until_utc IS NULL AND NOT a.cancel_requested
                      AND a.capture_json IS NOT NULL AND a.verify_json IS NOT NULL AND j.state='Succeeded'
                      AND j.completed_at_utc IS NOT NULL) THEN
                RAISE EXCEPTION 'workspace_retirement_requires_verified_stopped'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_admission';
            END IF;
            IF EXISTS (SELECT 1 FROM development_workspace_archives WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_lifecycle WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_source_imports WHERE workspace_id=w.id AND (finished_at_utc IS NULL OR lease IS NOT NULL))
                OR EXISTS (SELECT 1 FROM development_workspace_runs r JOIN operation_jobs j ON j.id=r.id
                    WHERE r.workspace_id=w.id AND (r.finished_at_utc IS NULL OR r.lease IS NOT NULL OR r.report_stage IN ('Collect','Retire')
                        OR j.phase='ResultUnknown' OR j.started_at_utc IS NOT NULL AND j.result_json IS NULL
                          AND j.phase NOT IN ('CanceledBeforeDispatch','AgentUnavailable'))) THEN
                RAISE EXCEPTION 'workspace_retirement_not_idle'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_idle';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retirement_admission BEFORE INSERT ON development_workspace_retirements
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_admission();

        -- Deferred pairing permits atomic admission/cancellation regardless
        -- of EF's update ordering, while rejecting a standalone old-binary
        -- resume, budget/config change, or premature intent completion.
        CREATE FUNCTION whaledeck_workspace_retirement_pair() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE wid uuid; current_state text; pending boolean;
        BEGIN
            IF TG_TABLE_NAME='development_workspaces' THEN
                IF TG_OP='DELETE' THEN wid=OLD.id; ELSE wid=NEW.id; END IF;
            ELSE
                IF TG_OP='DELETE' THEN wid=OLD.workspace_id; ELSE wid=NEW.workspace_id; END IF;
            END IF;
            SELECT lifecycle_state INTO current_state FROM development_workspaces WHERE id=wid;
            SELECT EXISTS (SELECT 1 FROM development_workspace_retirements WHERE workspace_id=wid AND finished_at_utc IS NULL) INTO pending;
            IF pending AND current_state IS DISTINCT FROM 'Retiring' THEN
                RAISE EXCEPTION 'workspace_retirement_fence_changed'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
            END IF;
            IF current_state='Retiring' AND NOT pending THEN
                RAISE EXCEPTION 'workspace_retirement_pair_missing'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_pair';
            END IF;
            IF TG_TABLE_NAME='development_workspaces' AND TG_OP='UPDATE' THEN
              IF pending OR OLD.lifecycle_state='Retiring' THEN
                IF (to_jsonb(NEW)-'lifecycle_state') IS DISTINCT FROM (to_jsonb(OLD)-'lifecycle_state') THEN
                    RAISE EXCEPTION 'workspace_retirement_binding_changed'
                        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
                END IF;
              END IF;
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER workspace_retirement_fence AFTER INSERT OR UPDATE OR DELETE ON development_workspaces
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();
        CREATE CONSTRAINT TRIGGER workspace_retirement_pair AFTER INSERT OR UPDATE OR DELETE ON development_workspace_retirements
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();

        CREATE FUNCTION whaledeck_workspace_retirement_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF (NEW.id,NEW.workspace_id,NEW.archive_id,NEW.expected_epoch,NEW.snapshot_json)
                IS DISTINCT FROM (OLD.id,OLD.workspace_id,OLD.archive_id,OLD.expected_epoch,OLD.snapshot_json)
                OR OLD.plan_json IS NOT NULL AND (NEW.plan_json,NEW.plan_expires_at_utc) IS DISTINCT FROM (OLD.plan_json,OLD.plan_expires_at_utc)
                OR OLD.confirmed_at_utc IS NOT NULL AND NEW.confirmed_at_utc IS DISTINCT FROM OLD.confirmed_at_utc
                OR OLD.finished_at_utc IS NOT NULL AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) THEN
                RAISE EXCEPTION 'workspace_retirement_preparation_immutable'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_immutable';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retirement_immutable BEFORE UPDATE ON development_workspace_retirements
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_immutable();
        """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM development_workspace_retirements) THEN
                RAISE EXCEPTION 'workspace_retirement_history_present';
            END IF;
        END $$;
        DROP TRIGGER workspace_retirement_immutable ON development_workspace_retirements;
        DROP FUNCTION whaledeck_workspace_retirement_immutable();
        DROP TRIGGER workspace_retirement_pair ON development_workspace_retirements;
        DROP TRIGGER workspace_retirement_fence ON development_workspaces;
        DROP FUNCTION whaledeck_workspace_retirement_pair();
        DROP TRIGGER workspace_retirement_admission ON development_workspace_retirements;
        DROP FUNCTION whaledeck_workspace_retirement_admission();
        """);
        migrationBuilder.DropTable(
            name: "development_workspace_retirements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces");

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_lifecycle",
            table: "development_workspaces",
            sql: "lifecycle_version >= 1 AND\n(CASE lifecycle_state\n    WHEN 'Active' THEN lifecycle_version % 2 = 1\n    WHEN 'Stopping' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Stopped' THEN lifecycle_version % 2 = 0 AND state = 'StorageAllocated'\n    WHEN 'Resuming' THEN lifecycle_version >= 3 AND lifecycle_version % 2 = 1 AND state = 'StorageAllocated'\n    ELSE FALSE END) IS TRUE");
    }
}
