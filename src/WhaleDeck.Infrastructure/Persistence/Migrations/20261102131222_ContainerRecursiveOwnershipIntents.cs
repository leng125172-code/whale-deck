using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerRecursiveOwnershipIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "container_recursive_ownership_intents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                trace_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                container_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                relative_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                command_json = table.Column<string>(type: "text", nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                planning_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                plan_json = table.Column<string>(type: "text", nullable: true),
                plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ready_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                execution_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                publish_barrier = table.Column<bool>(type: "boolean", nullable: false),
                cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                receipt_json = table.Column<string>(type: "text", nullable: true),
                root_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                completed_count = table.Column<int>(type: "integer", nullable: true),
                changed_count = table.Column<int>(type: "integer", nullable: true),
                aborted_count = table.Column<int>(type: "integer", nullable: true),
                not_started_count = table.Column<int>(type: "integer", nullable: true),
                unknown_count = table.Column<int>(type: "integer", nullable: true),
                outcome_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                binding_current = table.Column<bool>(type: "boolean", nullable: true),
                history_available = table.Column<bool>(type: "boolean", nullable: true),
                history_may_exist = table.Column<bool>(type: "boolean", nullable: false),
                history_verified = table.Column<bool>(type: "boolean", nullable: false),
                history_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_recursive_ownership_intents", x => x.id);
                table.CheckConstraint("ck_container_recursive_ownership_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_recursive_ownership_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state NOT IN ('Applying','Partial') OR publish_barrier)");
                table.CheckConstraint("ck_container_recursive_ownership_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_recursive_ownership_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recursive_ownership_history", "(NOT history_verified OR history_may_exist AND history_digest IS NOT NULL) AND ((history_digest IS NOT NULL) = history_verified) AND (history_digest IS NULL OR history_digest ~ '^[A-F0-9]{64}$') AND (execution_job_id IS NOT NULL OR root_state IS NULL AND NOT history_may_exist AND NOT history_verified AND history_digest IS NULL) AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state = 'Aborted')");
                table.CheckConstraint("ck_container_recursive_ownership_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_recursive_ownership_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_recursive_ownership_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_recursive_ownership_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Partial','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Partial','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_recursive_ownership_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_recursive_ownership_receipt", "(root_state IS NULL AND receipt_json IS NULL AND completed_count IS NULL AND changed_count IS NULL AND aborted_count IS NULL AND not_started_count IS NULL AND unknown_count IS NULL AND outcome_digest IS NULL AND binding_current IS NULL AND history_available IS NULL) OR ((root_state IN ('Completed','Partial','Aborted','Unknown') AND receipt_json IS NOT NULL AND octet_length(receipt_json) BETWEEN 1 AND 65536 AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND completed_count BETWEEN 0 AND 10000 AND changed_count BETWEEN 0 AND completed_count AND aborted_count BETWEEN 0 AND 10000 AND not_started_count BETWEEN 0 AND 10000 AND unknown_count BETWEEN 0 AND 10000 AND binding_current IS NOT NULL AND history_available IS NOT NULL AND outcome_digest IS NOT NULL AND (outcome_digest = '' OR outcome_digest ~ '^[A-F0-9]{64}$') AND completed_count + aborted_count + not_started_count + unknown_count = (plan_json::jsonb->>'EntryCount')::integer AND changed_count <= (plan_json::jsonb->>'ChangeCount')::integer AND (NOT history_available OR history_verified) AND (NOT binding_current OR root_state = 'Completed' AND history_available) AND (root_state <> 'Completed' OR completed_count = (plan_json::jsonb->>'EntryCount')::integer AND changed_count = (plan_json::jsonb->>'ChangeCount')::integer AND history_verified AND outcome_digest ~ '^[A-F0-9]{64}$') AND (root_state <> 'Partial' OR changed_count > 0 AND completed_count < (plan_json::jsonb->>'EntryCount')::integer AND unknown_count = 0 AND history_verified AND outcome_digest ~ '^[A-F0-9]{64}$') AND (root_state <> 'Aborted' OR changed_count = 0 AND unknown_count = 0) AND (root_state <> 'Unknown' OR unknown_count > 0)) IS TRUE)");
                table.CheckConstraint("ck_container_recursive_ownership_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id' = execution_job_id::text AND receipt_json::jsonb->>'ContainerId' = container_id AND receipt_json::jsonb->>'InstanceId' = instance_id AND receipt_json::jsonb->>'Fingerprint' = plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'TargetProof' = plan_json::jsonb->>'TargetProof' AND receipt_json::jsonb->>'State' = root_state AND (receipt_json::jsonb->>'CompletedCount')::integer = completed_count AND (receipt_json::jsonb->>'ChangedCount')::integer = changed_count AND (receipt_json::jsonb->>'AbortedCount')::integer = aborted_count AND (receipt_json::jsonb->>'NotStartedCount')::integer = not_started_count AND (receipt_json::jsonb->>'UnknownCount')::integer = unknown_count AND receipt_json::jsonb->>'OutcomeDigest' = outcome_digest AND (receipt_json::jsonb->>'BindingCurrent')::boolean = binding_current AND (receipt_json::jsonb->>'HistoryAvailable')::boolean = history_available AND (receipt_json::jsonb->>'HistoryVerified')::boolean = history_verified AND (NOT (receipt_json::jsonb->>'HistoryMayExist')::boolean OR history_may_exist) AND receipt_json::jsonb->>'HistoryDigest' = COALESCE(history_digest,'')) IS TRUE)");
                table.CheckConstraint("ck_container_recursive_ownership_requested_owners", "((jsonb_typeof(command_json::jsonb->'Uid') = 'number' AND command_json::jsonb->>'Uid' ~ '^(0|[1-9][0-9]{0,9})$' AND (command_json::jsonb->>'Uid')::numeric BETWEEN 0 AND 4294967294 AND jsonb_typeof(command_json::jsonb->'Gid') = 'number' AND command_json::jsonb->>'Gid' ~ '^(0|[1-9][0-9]{0,9})$' AND (command_json::jsonb->>'Gid')::numeric BETWEEN 0 AND 4294967294) IS TRUE)");
                table.CheckConstraint("ck_container_recursive_ownership_result", "((state NOT IN ('Succeeded','Superseded') OR root_state = 'Completed' AND history_verified AND root_started_at_utc IS NOT NULL AND binding_current = (state = 'Succeeded')) AND (state <> 'Partial' OR root_state = 'Partial' AND history_verified AND publish_barrier AND root_started_at_utc IS NOT NULL) AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state = 'Aborted')) IS TRUE");
                table.CheckConstraint("ck_container_recursive_ownership_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Partial','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_recursive_ownership_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Partial'))");
                table.ForeignKey(
                    name: "FK_container_recursive_ownership_intents_operation_jobs_execut~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recursive_ownership_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_actor_subject_idempot~",
            table: "container_recursive_ownership_intents",
            columns: ["actor_subject", "idempotency_key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_container_id_complete~",
            table: "container_recursive_ownership_intents",
            columns: ["container_id", "completed_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_execution_job_id",
            table: "container_recursive_ownership_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_lease_token",
            table: "container_recursive_ownership_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_next_attempt_at_utc_id",
            table: "container_recursive_ownership_intents",
            columns: ["next_attempt_at_utc", "id"],
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_ownership_intents_publish_barrier",
            table: "container_recursive_ownership_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");
        migrationBuilder.Sql("""
    CREATE FUNCTION whaledeck_container_recursive_ownership_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recursive_ownership_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_ownership_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_ownership_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recursive_ownership_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recursive_ownership_started_immutable' USING ERRCODE='23514';
      END IF;
      IF (OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.completed_count,NEW.changed_count,NEW.aborted_count,NEW.not_started_count,NEW.unknown_count,NEW.outcome_digest,NEW.history_verified,NEW.history_digest)
        IS DISTINCT FROM ROW(OLD.root_state,OLD.completed_count,OLD.changed_count,OLD.aborted_count,OLD.not_started_count,OLD.unknown_count,OLD.outcome_digest,OLD.history_verified,OLD.history_digest)) OR
        (OLD.history_may_exist AND NOT NEW.history_may_exist) OR (OLD.history_verified AND NOT NEW.history_verified) OR
        (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) OR
        (OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR
          (NEW.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) IS DISTINCT FROM
          (OLD.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) OR
          (OLD.receipt_json::jsonb->>'HistoryMayExist')::boolean AND NOT (NEW.receipt_json::jsonb->>'HistoryMayExist')::boolean)) THEN
        RAISE EXCEPTION 'container_recursive_ownership_fact_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Partial','Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Partial','Unknown') THEN
        RAISE EXCEPTION 'container_recursive_ownership_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recursive_ownership_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recursive_ownership_guard BEFORE UPDATE OR DELETE ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_guard();
    CREATE FUNCTION whaledeck_container_recursive_ownership_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_directory_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_copy_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_move_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
        EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier AND id<>NEW.id)) THEN
        RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER whaledeck_recursive_ownership_host_path_fence BEFORE INSERT OR UPDATE ON file_path_locks
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_directory_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_copy_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_move_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_recursive_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    CREATE TRIGGER whaledeck_recursive_ownership_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_ownership_fence();
    """);

    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $f$ BEGIN
              IF EXISTS (SELECT 1 FROM container_recursive_ownership_intents) THEN
                RAISE EXCEPTION 'container_recursive_ownership_downgrade_requires_empty_table';
              END IF;
            END $f$;
            DROP TRIGGER whaledeck_recursive_ownership_host_path_fence ON file_path_locks;
            DROP TRIGGER whaledeck_recursive_ownership_upload_fence ON container_file_upload_operations;
            DROP TRIGGER whaledeck_recursive_ownership_directory_fence ON container_directory_operations;
            DROP TRIGGER whaledeck_recursive_ownership_copy_fence ON container_copy_intents;
            DROP TRIGGER whaledeck_recursive_ownership_move_fence ON container_move_intents;
            DROP TRIGGER whaledeck_recursive_ownership_permission_fence ON container_permission_intents;
            DROP TRIGGER whaledeck_recursive_ownership_ownership_fence ON container_ownership_intents;
            DROP TRIGGER whaledeck_recursive_ownership_recursive_permission_fence ON container_recursive_permission_intents;
            """);
        migrationBuilder.DropTable(
            name: "container_recursive_ownership_intents");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_container_recursive_ownership_fence(); DROP FUNCTION whaledeck_container_recursive_ownership_guard();");
    }
}
