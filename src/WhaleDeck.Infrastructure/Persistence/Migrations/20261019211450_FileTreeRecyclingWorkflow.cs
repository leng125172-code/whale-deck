using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreeRecyclingWorkflow : Migration
{
    private static readonly string[] CaptureColumns = ["source_hash", "created_at_utc", "id"];
    private static readonly string[] OwnerColumns = ["actor_subject", "login_binding", "idempotency_key"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_tree_recycling_operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                command_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                planning_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ready_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                execution_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                capture_id = table.Column<Guid>(type: "uuid", nullable: true),
                capture_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                capture_created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                archive_bytes = table.Column<long>(type: "bigint", nullable: true),
                archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                capture_verified = table.Column<bool>(type: "boolean", nullable: false),
                capture_may_be_retained = table.Column<bool>(type: "boolean", nullable: false),
                source_may_have_changed = table.Column<bool>(type: "boolean", nullable: false),
                source_removed = table.Column<bool>(type: "boolean", nullable: false),
                originals_retained = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_tree_recycling_operations", x => x.id);
                table.CheckConstraint("ck_tree_recycling_capture", "capture_verified=(capture_id IS NOT NULL) AND capture_verified=(capture_hash IS NOT NULL) AND capture_verified=(capture_created_at_utc IS NOT NULL) AND capture_verified=(archive_bytes IS NOT NULL) AND capture_verified=(archive_sha256 IS NOT NULL) AND (NOT capture_verified OR (execution_job_id IS NOT NULL AND capture_id=execution_job_id AND capture_hash ~ '^[A-F0-9]{64}$' AND archive_bytes BETWEEN 1 AND 18253611008 AND archive_sha256 ~ '^[A-F0-9]{64}$'))");
                table.CheckConstraint("ck_tree_recycling_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('RecyclingQueued','Recycling','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                table.CheckConstraint("ck_tree_recycling_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_tree_recycling_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_tree_recycling_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.CheckConstraint("ck_tree_recycling_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','RecyclingQueued','Recycling','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.CheckConstraint("ck_tree_recycling_result", "(state='Succeeded')=(source_removed AND originals_retained) AND (NOT source_may_have_changed OR capture_verified) AND (NOT source_removed OR source_may_have_changed) AND (NOT capture_verified OR capture_may_be_retained) AND (NOT originals_retained OR source_removed)");
                table.CheckConstraint("ck_tree_recycling_state", "state IN ('Queued','Planning','Ready','RecyclingQueued','Recycling','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.CheckConstraint("ck_tree_recycling_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','RecyclingQueued','Recycling','Unknown'))");
                table.ForeignKey(
                    name: "FK_file_tree_recycling_operations_operation_jobs_execution_job~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_recycling_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "file_tree_captures",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_json = table.Column<string>(type: "jsonb", nullable: false),
                manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                entry_count = table.Column<int>(type: "integer", nullable: false),
                total_bytes = table.Column<long>(type: "bigint", nullable: false),
                archive_bytes = table.Column<long>(type: "bigint", nullable: false),
                archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_tree_captures", x => x.id);
                table.CheckConstraint("ck_tree_capture_hashes", "owner_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND manifest_sha256 ~ '^[A-F0-9]{64}$' AND archive_sha256 ~ '^[A-F0-9]{64}$' AND proof_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_tree_capture_limits", "entry_count BETWEEN 1 AND 10000 AND total_bytes BETWEEN 0 AND 17179869184 AND archive_bytes BETWEEN 1 AND 18253611008 AND jsonb_typeof(source_json)='object' AND octet_length(source_json::text)<=32768");
                table.ForeignKey(
                    name: "FK_file_tree_captures_file_tree_recycling_operations_intent_id",
                    column: x => x.intent_id,
                    principalTable: "file_tree_recycling_operations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_captures_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_captures_intent_id",
            table: "file_tree_captures",
            column: "intent_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_captures_source_hash_created_at_utc_id",
            table: "file_tree_captures",
            columns: CaptureColumns);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_recycling_operations_actor_subject_login_binding_~",
            table: "file_tree_recycling_operations",
            columns: OwnerColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_recycling_operations_execution_job_id",
            table: "file_tree_recycling_operations",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_recycling_operations_lease_token",
            table: "file_tree_recycling_operations",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_recycling_operations_next_attempt_at_utc_id",
            table: "file_tree_recycling_operations",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");
        migrationBuilder.Sql("""
                CREATE FUNCTION whaledeck_tree_recycling_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'tree_recycling_permanent_intent' USING ERRCODE='23514'; END IF;
                  IF ROW(NEW.id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.owner_hash,NEW.request_hash,NEW.source_hash,NEW.command_json,NEW.created_at_utc,NEW.planning_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.owner_hash,OLD.request_hash,OLD.source_hash,OLD.command_json,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                    RAISE EXCEPTION 'tree_recycling_request_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN RAISE EXCEPTION 'tree_recycling_plan_immutable' USING ERRCODE='23514'; END IF;
                  IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.execution_expires_at_utc) IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.execution_expires_at_utc) THEN RAISE EXCEPTION 'tree_recycling_execution_immutable' USING ERRCODE='23514'; END IF;
                  IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN RAISE EXCEPTION 'tree_recycling_started_immutable' USING ERRCODE='23514'; END IF;
                  IF OLD.capture_verified AND ROW(NEW.capture_verified,NEW.capture_id,NEW.capture_hash,NEW.capture_created_at_utc,NEW.archive_bytes,NEW.archive_sha256)
                     IS DISTINCT FROM ROW(OLD.capture_verified,OLD.capture_id,OLD.capture_hash,OLD.capture_created_at_utc,OLD.archive_bytes,OLD.archive_sha256) THEN RAISE EXCEPTION 'tree_recycling_capture_immutable' USING ERRCODE='23514'; END IF;
                  IF (OLD.capture_may_be_retained AND NOT NEW.capture_may_be_retained) OR (OLD.source_may_have_changed AND NOT NEW.source_may_have_changed) THEN RAISE EXCEPTION 'tree_recycling_effects_monotonic' USING ERRCODE='23514'; END IF;
                  IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'tree_recycling_terminal_immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $function$;
                CREATE TRIGGER tree_recycling_guard BEFORE UPDATE OR DELETE ON file_tree_recycling_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_recycling_guard();
                CREATE FUNCTION whaledeck_tree_capture_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
                BEGIN
                  IF TG_OP='DELETE' OR NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'tree_capture_immutable' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $function$;
                CREATE TRIGGER tree_capture_guard BEFORE UPDATE OR DELETE ON file_tree_captures FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_capture_guard();
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM file_tree_recycling_operations) OR EXISTS (SELECT 1 FROM file_tree_captures) THEN RAISE EXCEPTION 'tree_recycling_downgrade_requires_empty_table'; END IF;
                END $guard$;
                """);
        migrationBuilder.DropTable(
            name: "file_tree_captures");

        migrationBuilder.DropTable(
            name: "file_tree_recycling_operations");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_tree_capture_guard(); DROP FUNCTION whaledeck_tree_recycling_guard();");
    }
}
