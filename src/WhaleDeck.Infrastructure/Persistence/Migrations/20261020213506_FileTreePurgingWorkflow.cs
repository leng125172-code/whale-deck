using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreePurgingWorkflow : Migration
{
    private static readonly string[] ActorColumns = ["actor_subject", "login_binding", "idempotency_key"];
    private static readonly string[] CaptureColumns = ["capture_id", "id"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_tree_purging_operations",
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
                receipt_json = table.Column<string>(type: "jsonb", nullable: true),
                receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                receipt_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deletion_started = table.Column<bool>(type: "boolean", nullable: false),
                archive_removed = table.Column<bool>(type: "boolean", nullable: false),
                originals_removed = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_tree_purging_operations", x => x.id);
                table.CheckConstraint("ck_tree_purging_capture", "(capture_id<>'00000000-0000-0000-0000-000000000000'::uuid AND command_json->>'CaptureId'=capture_id::text AND command_json->>'CaptureKind' IN ('Trash','Overwrite') AND command_json->>'Scope' IN ('Archive','Originals','All') AND command_json->>'CatalogueProofHash' ~ '^[A-F0-9]{64}$') IS TRUE");
                table.CheckConstraint("ck_tree_purging_effects", "(NOT deletion_started OR root_started_at_utc IS NOT NULL) AND (NOT archive_removed OR deletion_started AND command_json->>'Scope' IN ('Archive','All')) AND (NOT originals_removed OR deletion_started AND command_json->>'Scope' IN ('Originals','All')) AND (receipt_json IS NOT NULL OR NOT (deletion_started OR archive_removed OR originals_removed)) AND (root_started_at_utc IS NULL OR receipt_json IS NOT NULL OR state IN ('Purging','Unknown'))");
                table.CheckConstraint("ck_tree_purging_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_job_id<>capture_id AND execution_expires_at_utc>created_at_utc AND execution_expires_at_utc<=ready_expires_at_utc+INTERVAL '15 minutes')) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('PurgingQueued','Purging','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL) AND (state<>'Purging' OR root_started_at_utc IS NOT NULL)");
                table.CheckConstraint("ck_tree_purging_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (receipt_hash IS NULL OR receipt_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_tree_purging_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_tree_purging_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND (receipt_json IS NULL OR (octet_length(receipt_json::text)<=32768 AND jsonb_typeof(receipt_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                table.CheckConstraint("ck_tree_purging_outcome", "(receipt_json IS NULL OR (CASE receipt_json->>'State' WHEN 'Completed' THEN state='Succeeded' AND deletion_started AND (command_json->>'Scope' NOT IN ('Archive','All') OR archive_removed) AND (command_json->>'Scope' NOT IN ('Originals','All') OR originals_removed) WHEN 'Unknown' THEN state IN ('Unknown','Acknowledged') AND deletion_started WHEN 'Aborted' THEN state IN ('Failed','Canceled','Acknowledged') AND NOT deletion_started AND NOT archive_removed AND NOT originals_removed ELSE false END) IS TRUE) AND (state NOT IN ('Succeeded','Acknowledged') OR receipt_json IS NOT NULL)");
                table.CheckConstraint("ck_tree_purging_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (plan_json IS NULL OR (plan_json->'Command'=command_json AND (plan_json->>'EntryCount')::integer BETWEEN 1 AND 10000 AND (plan_json->>'TotalBytes')::bigint BETWEEN 0 AND 17179869184 AND (plan_json->>'ArchiveBytes')::bigint BETWEEN 1 AND 18253611008) IS TRUE) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc AND ready_expires_at_utc<=planning_expires_at_utc+INTERVAL '5 minutes') AND (state NOT IN ('Ready','PurgingQueued','Purging','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                table.CheckConstraint("ck_tree_purging_receipt", "(receipt_json IS NULL)=(receipt_hash IS NULL) AND (receipt_json IS NULL)=(receipt_observed_at_utc IS NULL) AND (receipt_json IS NULL OR (execution_job_id IS NOT NULL AND receipt_observed_at_utc>=created_at_utc AND receipt_json->>'OperationId'=execution_job_id::text AND receipt_json->>'Kind'='DirectoryPurge' AND receipt_json->>'CaptureId'=capture_id::text AND receipt_json->>'Scope'=command_json->>'Scope' AND receipt_json->>'Fingerprint'=plan_json->>'Fingerprint' AND (receipt_json->>'EntryCount')::integer=(plan_json->>'EntryCount')::integer AND (receipt_json->>'TotalBytes')::bigint=(plan_json->>'TotalBytes')::bigint AND (receipt_json->>'ArchiveBytes')::bigint=(plan_json->>'ArchiveBytes')::bigint AND (receipt_json->>'DeletionStarted')::boolean=deletion_started AND (receipt_json->>'ArchiveRemoved')::boolean=archive_removed AND (receipt_json->>'OriginalsRemoved')::boolean=originals_removed AND receipt_json->>'State' IN ('Completed','Unknown','Aborted')) IS TRUE)");
                table.CheckConstraint("ck_tree_purging_state", "state IN ('Queued','Planning','Ready','PurgingQueued','Purging','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                table.CheckConstraint("ck_tree_purging_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','PurgingQueued','Purging','Unknown'))");
                table.ForeignKey(
                    name: "FK_file_tree_purging_operations_file_tree_captures_capture_id",
                    column: x => x.capture_id,
                    principalTable: "file_tree_captures",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_purging_operations_operation_jobs_execution_job_id",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_file_tree_purging_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_purging_operations_actor_subject_login_binding_id~",
            table: "file_tree_purging_operations",
            columns: ActorColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_purging_operations_capture_id_id",
            table: "file_tree_purging_operations",
            columns: CaptureColumns);

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_purging_operations_execution_job_id",
            table: "file_tree_purging_operations",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_purging_operations_lease_token",
            table: "file_tree_purging_operations",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_file_tree_purging_operations_next_attempt_at_utc_id",
            table: "file_tree_purging_operations",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_tree_purging_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'tree_purging_permanent_intent' USING ERRCODE='23514'; END IF;
              IF ROW(NEW.id,NEW.capture_id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.owner_hash,NEW.request_hash,NEW.source_hash,NEW.command_json,NEW.created_at_utc,NEW.planning_expires_at_utc)
                 IS DISTINCT FROM ROW(OLD.id,OLD.capture_id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.owner_hash,OLD.request_hash,OLD.source_hash,OLD.command_json,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                RAISE EXCEPTION 'tree_purging_request_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
                RAISE EXCEPTION 'tree_purging_plan_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.execution_expires_at_utc) IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.execution_expires_at_utc) THEN
                RAISE EXCEPTION 'tree_purging_execution_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
                RAISE EXCEPTION 'tree_purging_started_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.receipt_json IS NOT NULL AND ROW(NEW.receipt_json,NEW.receipt_hash,NEW.receipt_observed_at_utc) IS DISTINCT FROM ROW(OLD.receipt_json,OLD.receipt_hash,OLD.receipt_observed_at_utc) THEN
                RAISE EXCEPTION 'tree_purging_receipt_immutable' USING ERRCODE='23514';
              END IF;
              IF (OLD.deletion_started AND NOT NEW.deletion_started) OR (OLD.archive_removed AND NOT NEW.archive_removed) OR (OLD.originals_removed AND NOT NEW.originals_removed) THEN
                RAISE EXCEPTION 'tree_purging_effects_monotonic' USING ERRCODE='23514';
              END IF;
              IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'tree_purging_terminal_immutable' USING ERRCODE='23514'; END IF;
              RETURN NEW;
            END $function$;
            CREATE TRIGGER tree_purging_guard BEFORE UPDATE OR DELETE ON file_tree_purging_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_purging_guard();
            CREATE FUNCTION whaledeck_tree_purging_origin_guard() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
              IF EXISTS (SELECT 1 FROM file_tree_captures c WHERE c.id=NEW.capture_id AND
                c.kind=NEW.command_json->>'CaptureKind' AND c.owner_hash=NEW.command_json->>'CaptureOwner' AND
                c.manifest_sha256=NEW.command_json->>'ManifestSha256' AND c.proof_hash=NEW.command_json->>'CatalogueProofHash' AND
                c.source_hash=NEW.source_hash AND c.source_json->'Source'=NEW.command_json->'Source' AND
                c.source_json->>'ExpectedVersion'=NEW.command_json->>'ExpectedVersion') THEN RETURN NEW; END IF;
              RAISE EXCEPTION 'tree_purging_capture_origin_required' USING ERRCODE='23514';
            END $function$;
            CREATE TRIGGER tree_purging_origin_guard BEFORE INSERT ON file_tree_purging_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_purging_origin_guard();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
              IF EXISTS (SELECT 1 FROM file_tree_purging_operations) THEN RAISE EXCEPTION 'tree_purging_downgrade_requires_empty_kind'; END IF;
            END $guard$;
            DROP TRIGGER tree_purging_origin_guard ON file_tree_purging_operations;
            DROP FUNCTION whaledeck_tree_purging_origin_guard();
            DROP TRIGGER tree_purging_guard ON file_tree_purging_operations;
            DROP FUNCTION whaledeck_tree_purging_guard();
            """);
        migrationBuilder.DropTable(
            name: "file_tree_purging_operations");
    }
}
