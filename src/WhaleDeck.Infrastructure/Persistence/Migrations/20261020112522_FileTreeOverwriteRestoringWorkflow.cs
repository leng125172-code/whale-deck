using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreeOverwriteRestoringWorkflow : Migration
{
    private static readonly string[] ActorColumns = ["actor_subject", "login_binding", "idempotency_key"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_file_tree_captures_file_tree_recycling_operations_intent_id",
            table: "file_tree_captures");

        migrationBuilder.DropCheckConstraint(
            name: "ck_tree_restoring_capture",
            table: "file_tree_restoring_operations");

        migrationBuilder.AddColumn<string>(
            name: "content_sha256",
            table: "file_tree_captures",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "kind",
            table: "file_tree_captures",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Trash");

        migrationBuilder.CreateTable(
            name: "file_tree_overwrite_restoring_operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                command_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                planning_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ready_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                execution_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                archive_bytes = table.Column<long>(type: "bigint", nullable: true),
                archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                partial_retained = table.Column<bool>(type: "boolean", nullable: false),
                overwritten_capture_id = table.Column<Guid>(type: "uuid", nullable: true),
                backup_may_be_retained = table.Column<bool>(type: "boolean", nullable: false),
                backup_verified = table.Column<bool>(type: "boolean", nullable: false),
                originals_retained = table.Column<bool>(type: "boolean", nullable: false),
                target_may_have_changed = table.Column<bool>(type: "boolean", nullable: false),
                backup_archive_bytes = table.Column<long>(type: "bigint", nullable: true),
                backup_archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                backup_proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                backup_captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_tree_overwrite_restoring_operations", x => x.id);
                table.CheckConstraint("ck_tree_overwrite_restoring_archive", "(archive_bytes IS NULL)=(archive_sha256 IS NULL) AND (plan_json IS NOT NULL)=(archive_bytes IS NOT NULL) AND (archive_bytes IS NULL OR (archive_bytes BETWEEN 1 AND 18253611008 AND archive_sha256 ~ '^[A-F0-9]{64}$' AND archive_bytes=(plan_json->>'ArchiveBytes')::bigint AND archive_sha256=plan_json->>'ArchiveSha256'))");
                table.CheckConstraint("ck_tree_overwrite_restoring_backup", "backup_verified=(overwritten_capture_id IS NOT NULL) AND backup_verified=(backup_proof_hash IS NOT NULL) AND backup_verified=(backup_captured_at_utc IS NOT NULL) AND backup_verified=(backup_archive_bytes IS NOT NULL) AND backup_verified=(backup_archive_sha256 IS NOT NULL) AND (NOT backup_verified OR (execution_job_id IS NOT NULL AND overwritten_capture_id=execution_job_id AND backup_may_be_retained AND backup_proof_hash ~ '^[A-F0-9]{64}$' AND backup_archive_bytes BETWEEN 1 AND 18253611008 AND backup_archive_sha256 ~ '^[A-F0-9]{64}$'))");
                table.CheckConstraint("ck_tree_overwrite_restoring_capture", "capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'Format'='tar.gz' AND command_json->>'CaptureKind' IN ('Trash','Overwrite')");
                table.CheckConstraint("ck_tree_overwrite_restoring_effects", "(NOT target_may_have_changed OR backup_verified) AND (NOT originals_retained OR target_may_have_changed) AND (state<>'Succeeded' OR (backup_verified AND originals_retained AND target_may_have_changed)) AND (plan_json IS NOT NULL OR NOT backup_may_be_retained)");
                table.CheckConstraint("ck_tree_overwrite_restoring_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('OverwriteRestoringQueued','OverwriteRestoring','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                table.CheckConstraint("ck_tree_overwrite_restoring_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_tree_overwrite_restoring_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_tree_overwrite_restoring_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.CheckConstraint("ck_tree_overwrite_restoring_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','OverwriteRestoringQueued','OverwriteRestoring','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.CheckConstraint("ck_tree_overwrite_restoring_result", "(state='Succeeded')=(published_version IS NOT NULL) AND (NOT partial_retained OR state IN ('Unknown','Failed','Canceled','Acknowledged'))");
                table.CheckConstraint("ck_tree_overwrite_restoring_state", "state IN ('Queued','Planning','Ready','OverwriteRestoringQueued','OverwriteRestoring','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.CheckConstraint("ck_tree_overwrite_restoring_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','OverwriteRestoringQueued','OverwriteRestoring','Unknown'))");
                table.ForeignKey(
                    name: "FK_file_tree_overwrite_restoring_operations_file_tree_captures~",
                    column: x => x.capture_id,
                    principalTable: "file_tree_captures",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_overwrite_restoring_operations_file_tree_capture~1",
                    column: x => x.overwritten_capture_id,
                    principalTable: "file_tree_captures",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_overwrite_restoring_operations_operation_jobs_exe~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_overwrite_restoring_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_tree_restoring_capture",
            table: "file_tree_restoring_operations",
            sql: "capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'Format'='tar.gz' AND command_json->>'CaptureKind' IN ('Trash','Overwrite')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_tree_capture_kind",
            table: "file_tree_captures",
            sql: "(kind='Trash' AND content_sha256 IS NULL) OR (kind='Overwrite' AND content_sha256 IS NOT NULL AND content_sha256 ~ '^[A-F0-9]{64}$')");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_actor_subject_logi~",
            table: "file_tree_overwrite_restoring_operations",
            columns: ActorColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_capture_id",
            table: "file_tree_overwrite_restoring_operations",
            column: "capture_id");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_execution_job_id",
            table: "file_tree_overwrite_restoring_operations",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_lease_token",
            table: "file_tree_overwrite_restoring_operations",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_next_attempt_at_ut~",
            table: "file_tree_overwrite_restoring_operations",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_overwrite_restoring_operations_overwritten_captur~",
            table: "file_tree_overwrite_restoring_operations",
            column: "overwritten_capture_id");

        migrationBuilder.AddForeignKey(
            name: "FK_file_tree_captures_operation_jobs_intent_id",
            table: "file_tree_captures",
            column: "intent_id",
            principalTable: "operation_jobs",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_tree_overwrite_restoring_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'tree_overwrite_restoring_permanent_intent' USING ERRCODE='23514'; END IF;
              IF ROW(NEW.id,NEW.capture_id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.owner_hash,NEW.request_hash,NEW.source_hash,NEW.target_hash,NEW.command_json,NEW.created_at_utc,NEW.planning_expires_at_utc)
                 IS DISTINCT FROM ROW(OLD.id,OLD.capture_id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.owner_hash,OLD.request_hash,OLD.source_hash,OLD.target_hash,OLD.command_json,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_request_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc,NEW.archive_bytes,NEW.archive_sha256)
                 IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc,OLD.archive_bytes,OLD.archive_sha256) THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_plan_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.execution_expires_at_utc) IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.execution_expires_at_utc) THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_execution_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_started_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.backup_verified AND ROW(NEW.backup_verified,NEW.overwritten_capture_id,NEW.backup_proof_hash,NEW.backup_captured_at_utc,NEW.backup_archive_bytes,NEW.backup_archive_sha256)
                 IS DISTINCT FROM ROW(OLD.backup_verified,OLD.overwritten_capture_id,OLD.backup_proof_hash,OLD.backup_captured_at_utc,OLD.backup_archive_bytes,OLD.backup_archive_sha256) THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_backup_immutable' USING ERRCODE='23514';
              END IF;
              IF (OLD.backup_may_be_retained AND NOT NEW.backup_may_be_retained) OR (OLD.originals_retained AND NOT NEW.originals_retained) OR (OLD.target_may_have_changed AND NOT NEW.target_may_have_changed) THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_effects_monotonic' USING ERRCODE='23514';
              END IF;
              IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'tree_overwrite_restoring_terminal_immutable' USING ERRCODE='23514'; END IF;
              RETURN NEW;
            END $function$;
            CREATE TRIGGER tree_overwrite_restoring_guard BEFORE UPDATE OR DELETE ON file_tree_overwrite_restoring_operations
            FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_overwrite_restoring_guard();
            CREATE FUNCTION whaledeck_tree_capture_origin_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
              IF (NEW.kind='Trash' AND EXISTS (SELECT 1 FROM file_tree_recycling_operations WHERE id=NEW.intent_id AND execution_job_id=NEW.id)) OR
                 (NEW.kind='Overwrite' AND EXISTS (SELECT 1 FROM file_tree_overwrite_restoring_operations WHERE id=NEW.intent_id AND execution_job_id=NEW.id)) THEN RETURN NEW; END IF;
              RAISE EXCEPTION 'tree_capture_origin_required' USING ERRCODE='23514';
            END $function$;
            CREATE TRIGGER tree_capture_origin_guard BEFORE INSERT ON file_tree_captures FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_capture_origin_guard();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
              IF EXISTS (SELECT 1 FROM file_tree_overwrite_restoring_operations) OR EXISTS (SELECT 1 FROM file_tree_captures WHERE kind<>'Trash') OR
                 EXISTS (SELECT 1 FROM file_tree_restoring_operations WHERE command_json->>'CaptureKind'='Overwrite') THEN
                RAISE EXCEPTION 'tree_overwrite_restoring_downgrade_requires_empty_kind';
              END IF;
            END $guard$;
            DROP TRIGGER tree_capture_origin_guard ON file_tree_captures;
            DROP FUNCTION whaledeck_tree_capture_origin_guard();
            DROP TRIGGER tree_overwrite_restoring_guard ON file_tree_overwrite_restoring_operations;
            DROP FUNCTION whaledeck_tree_overwrite_restoring_guard();
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_file_tree_captures_operation_jobs_intent_id",
            table: "file_tree_captures");

        migrationBuilder.DropTable(
            name: "file_tree_overwrite_restoring_operations");

        migrationBuilder.DropCheckConstraint(
            name: "ck_tree_restoring_capture",
            table: "file_tree_restoring_operations");

        migrationBuilder.DropCheckConstraint(
            name: "ck_tree_capture_kind",
            table: "file_tree_captures");

        migrationBuilder.DropColumn(
            name: "content_sha256",
            table: "file_tree_captures");

        migrationBuilder.DropColumn(
            name: "kind",
            table: "file_tree_captures");

        migrationBuilder.AddCheckConstraint(
            name: "ck_tree_restoring_capture",
            table: "file_tree_restoring_operations",
            sql: "capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'Format'='tar.gz' AND command_json->>'CaptureKind'='Trash'");

        migrationBuilder.AddForeignKey(
            name: "FK_file_tree_captures_file_tree_recycling_operations_intent_id",
            table: "file_tree_captures",
            column: "intent_id",
            principalTable: "file_tree_recycling_operations",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}
