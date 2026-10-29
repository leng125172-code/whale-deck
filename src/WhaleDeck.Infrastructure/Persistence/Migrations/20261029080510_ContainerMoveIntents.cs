using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerMoveIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "container_move_intents",
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
                source_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                target_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
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
                published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                private_may_exist = table.Column<bool>(type: "boolean", nullable: false),
                destination_published = table.Column<bool>(type: "boolean", nullable: true),
                source_removed = table.Column<bool>(type: "boolean", nullable: true),
                source_retained = table.Column<bool>(type: "boolean", nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_move_intents", x => x.id);
                table.CheckConstraint("ck_container_move_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_move_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state <> 'Applying' OR publish_barrier)");
                table.CheckConstraint("ck_container_move_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_move_facts", "(state NOT IN ('Succeeded','Superseded') OR (destination_published IS TRUE AND source_removed IS TRUE AND source_retained IS TRUE)) AND (execution_job_id IS NOT NULL OR (destination_published IS NULL AND source_removed IS NULL AND source_retained IS NULL)) AND (root_started_at_utc IS NOT NULL OR (destination_published IS NOT TRUE AND source_removed IS NOT TRUE AND source_retained IS NOT TRUE)) AND (execution_job_id IS NULL OR state NOT IN ('Failed','Canceled') OR (destination_published IS FALSE AND source_removed IS FALSE AND source_retained IS FALSE))");
                table.CheckConstraint("ck_container_move_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_move_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(source_path) BETWEEN 1 AND 4096 AND octet_length(target_path) BETWEEN 1 AND 4096 AND source_path <> target_path AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_move_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_move_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_move_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_move_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_move_result", "(state NOT IN ('Succeeded','Superseded') OR published_version IS NOT NULL AND root_started_at_utc IS NOT NULL AND NOT private_may_exist) AND (published_version IS NULL OR state IN ('Succeeded','Superseded')) AND (state <> 'Unknown' OR private_may_exist)");
                table.CheckConstraint("ck_container_move_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_move_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_move_intents_operation_jobs_execution_job_id",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_move_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_actor_subject_idempotency_key",
            table: "container_move_intents",
            columns: [ "actor_subject", "idempotency_key" ],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_container_id_completed_at_utc",
            table: "container_move_intents",
            columns: [ "container_id", "completed_at_utc" ]);

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_execution_job_id",
            table: "container_move_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_lease_token",
            table: "container_move_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_next_attempt_at_utc_id",
            table: "container_move_intents",
            columns: [ "next_attempt_at_utc", "id" ],
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_move_intents_publish_barrier",
            table: "container_move_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_container_move_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
            BEGIN
              IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_move_permanent_intent' USING ERRCODE='23514'; END IF;
              IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.source_path,NEW.target_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
                IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.source_path,OLD.target_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                RAISE EXCEPTION 'container_move_request_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
                RAISE EXCEPTION 'container_move_plan_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
                IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
                RAISE EXCEPTION 'container_move_execution_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
                RAISE EXCEPTION 'container_move_started_immutable' USING ERRCODE='23514';
              END IF;
              IF (OLD.destination_published IS NOT NULL AND NEW.destination_published IS DISTINCT FROM OLD.destination_published) OR
                (OLD.source_removed IS NOT NULL AND NEW.source_removed IS DISTINCT FROM OLD.source_removed) OR
                (OLD.source_retained IS NOT NULL AND NEW.source_retained IS DISTINCT FROM OLD.source_retained) THEN
                RAISE EXCEPTION 'container_move_fact_immutable' USING ERRCODE='23514';
              END IF;
              IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
                RAISE EXCEPTION 'container_move_terminal_immutable' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $f$;
            CREATE TRIGGER container_move_guard BEFORE UPDATE OR DELETE ON container_move_intents
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_guard();
            CREATE FUNCTION whaledeck_container_move_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
            BEGIN
              PERFORM pg_advisory_xact_lock(41307341);
              IF TG_TABLE_NAME='file_path_locks' THEN
                IF EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
                END IF;
              ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
                IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
                END IF;
              ELSIF TG_TABLE_NAME='container_directory_operations' THEN
                IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
                END IF;
              ELSIF TG_TABLE_NAME='container_copy_intents' THEN
                IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
                END IF;
              ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
                EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
                EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
                EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
                EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier AND id<>NEW.id)) THEN
                RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
              END IF;
              RETURN NEW;
            END $f$;
            CREATE TRIGGER whaledeck_move_host_path_fence BEFORE INSERT OR UPDATE ON file_path_locks
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_fence();
            CREATE TRIGGER whaledeck_move_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_fence();
            CREATE TRIGGER whaledeck_move_directory_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_fence();
            CREATE TRIGGER whaledeck_move_copy_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_fence();
            CREATE TRIGGER whaledeck_move_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_move_fence();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $f$ BEGIN
              IF EXISTS (SELECT 1 FROM container_move_intents) THEN
                RAISE EXCEPTION 'container_move_downgrade_requires_empty_table';
              END IF;
            END $f$;
            DROP TRIGGER whaledeck_move_host_path_fence ON file_path_locks;
            DROP TRIGGER whaledeck_move_upload_fence ON container_file_upload_operations;
            DROP TRIGGER whaledeck_move_directory_fence ON container_directory_operations;
            DROP TRIGGER whaledeck_move_copy_fence ON container_copy_intents;
            """);
        migrationBuilder.DropTable(
            name: "container_move_intents");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_container_move_fence(); DROP FUNCTION whaledeck_container_move_guard();");
    }
}
