using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerRecycleIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ShapeFunctions);
        migrationBuilder.CreateTable(
            name: "container_recycle_intents",
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
                receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                root_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                source_retained = table.Column<bool>(type: "boolean", nullable: true),
                source_restored = table.Column<bool>(type: "boolean", nullable: true),
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
                table.PrimaryKey("PK_container_recycle_intents", x => x.id);
                table.CheckConstraint("ck_container_recycle_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_recycle_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state <> 'Applying' OR publish_barrier)");
                table.CheckConstraint("ck_container_recycle_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_recycle_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recycle_history", "(NOT history_verified OR history_may_exist AND history_digest IS NOT NULL) AND ((history_digest IS NOT NULL) = history_verified) AND (history_digest IS NULL OR history_digest ~ '^[A-F0-9]{64}$') AND (execution_job_id IS NOT NULL OR root_state IS NULL AND NOT history_may_exist AND NOT history_verified AND history_digest IS NULL) AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state = 'Aborted')");
                table.CheckConstraint("ck_container_recycle_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_recycle_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_recycle_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_recycle_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_recycle_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_recycle_planned", "plan_json IS NULL OR whaledeck_recycle_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_receipt", "(root_state IS NULL AND receipt_json IS NULL AND receipt_hash IS NULL AND source_retained IS NULL AND source_restored IS NULL AND binding_current IS NULL AND history_available IS NULL) OR ((root_state IN ('Trashed','Restored','Aborted','Unknown') AND receipt_json IS NOT NULL AND receipt_hash IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND octet_length(receipt_json) BETWEEN 1 AND 65536 AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND binding_current IS NOT NULL AND history_available IS NOT NULL AND (NOT history_available OR history_verified) AND (NOT binding_current OR root_state IN ('Trashed','Restored') AND history_available) AND CASE WHEN root_state='Unknown' THEN source_retained IS NULL AND source_restored IS NULL ELSE source_retained IS NOT NULL AND source_restored IS NOT NULL AND source_retained=(root_state='Trashed') AND source_restored=(root_state='Restored') END AND (root_state NOT IN ('Trashed','Restored') OR history_verified)) IS TRUE)");
                table.CheckConstraint("ck_container_recycle_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id' = execution_job_id::text AND receipt_json::jsonb->>'ContainerId' = container_id AND receipt_json::jsonb->>'InstanceId' = instance_id AND receipt_json::jsonb->>'Fingerprint' = plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'TargetProof' = plan_json::jsonb->>'TargetProof' AND receipt_json::jsonb->>'State' = root_state AND (receipt_json::jsonb->>'SourceRetained')::boolean IS NOT DISTINCT FROM source_retained AND (receipt_json::jsonb->>'SourceRestored')::boolean IS NOT DISTINCT FROM source_restored AND (receipt_json::jsonb->>'BindingCurrent')::boolean = binding_current AND (receipt_json::jsonb->>'HistoryAvailable')::boolean = history_available AND (receipt_json::jsonb->>'HistoryVerified')::boolean = history_verified AND (NOT (receipt_json::jsonb->>'HistoryMayExist')::boolean OR history_may_exist) AND receipt_json::jsonb->>'HistoryDigest' = COALESCE(history_digest,'')) IS TRUE)");
                table.CheckConstraint("ck_container_recycle_receipt_shape", "receipt_json IS NULL OR whaledeck_recycle_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_requested", "whaledeck_recycle_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.CheckConstraint("ck_container_recycle_result", "((state <> 'Succeeded' OR root_state = 'Trashed' AND binding_current) AND (state <> 'Superseded' OR root_state = 'Restored' OR root_state = 'Trashed' AND NOT binding_current) AND (state NOT IN ('Succeeded','Superseded') OR history_verified AND root_started_at_utc IS NOT NULL) AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state = 'Aborted')) IS TRUE");
                table.CheckConstraint("ck_container_recycle_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_recycle_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_recycle_intents_operation_jobs_execution_job_id",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_actor_subject_idempotency_key",
            table: "container_recycle_intents",
            columns: ["actor_subject", "idempotency_key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_container_id_completed_at_utc",
            table: "container_recycle_intents",
            columns: ["container_id", "completed_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_execution_job_id",
            table: "container_recycle_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_lease_token",
            table: "container_recycle_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_next_attempt_at_utc_id",
            table: "container_recycle_intents",
            columns: ["next_attempt_at_utc", "id"],
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_intents_publish_barrier",
            table: "container_recycle_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");
        migrationBuilder.Sql(GuardAndFences);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        DO $f$ BEGIN
          IF EXISTS (SELECT 1 FROM container_recycle_intents) THEN
            RAISE EXCEPTION 'container_recycle_downgrade_requires_empty_table';
          END IF;
        END $f$;
        DROP TRIGGER whaledeck_recycle_fence_0 ON file_path_locks;
        DROP TRIGGER whaledeck_recycle_fence_1 ON container_file_upload_operations;
        DROP TRIGGER whaledeck_recycle_fence_2 ON container_directory_operations;
        DROP TRIGGER whaledeck_recycle_fence_3 ON container_copy_intents;
        DROP TRIGGER whaledeck_recycle_fence_4 ON container_move_intents;
        DROP TRIGGER whaledeck_recycle_fence_5 ON container_permission_intents;
        DROP TRIGGER whaledeck_recycle_fence_6 ON container_ownership_intents;
        DROP TRIGGER whaledeck_recycle_fence_7 ON container_recursive_permission_intents;
        DROP TRIGGER whaledeck_recycle_fence_8 ON container_recursive_ownership_intents;
        DROP TRIGGER whaledeck_recycle_fence_9 ON container_acl_intents;
        DROP TRIGGER whaledeck_recycle_fence_10 ON container_recursive_acl_intents;
        DROP TRIGGER whaledeck_recycle_fence_11 ON container_recursive_acl_restore_intents;
        """);

        migrationBuilder.DropTable(
            name: "container_recycle_intents");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_container_recycle_fence(); DROP FUNCTION whaledeck_container_recycle_guard(); DROP FUNCTION whaledeck_recycle_receipt_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_plan_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_command_valid(jsonb,text,text,text); DROP FUNCTION whaledeck_recycle_uint(jsonb,numeric); DROP FUNCTION whaledeck_recycle_sint(jsonb); DROP FUNCTION whaledeck_recycle_instance_valid(text);");
    }
}
