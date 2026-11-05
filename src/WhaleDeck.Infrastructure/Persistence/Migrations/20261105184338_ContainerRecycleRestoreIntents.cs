using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerRecycleRestoreIntents : Migration
{
    /// <inheritdoc />
    private static readonly string[] ActorKeyColumns = ["actor_subject", "idempotency_key"];
    private static readonly string[] ContainerCompletionColumns = ["container_id", "completed_at_utc"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ShapeFunctions);
        migrationBuilder.CreateTable(
            name: "container_recycle_restore_intents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                source_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                restored_by_this_operation = table.Column<bool>(type: "boolean", nullable: true),
                source_retained = table.Column<bool>(type: "boolean", nullable: true),
                source_restored = table.Column<bool>(type: "boolean", nullable: true),
                binding_current = table.Column<bool>(type: "boolean", nullable: true),
                history_available = table.Column<bool>(type: "boolean", nullable: true),
                history_verified = table.Column<bool>(type: "boolean", nullable: false),
                history_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_recycle_restore_intents", x => x.id);
                table.CheckConstraint("ck_container_recycle_restore_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_recycle_restore_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state <> 'Applying' OR publish_barrier)");
                table.CheckConstraint("ck_container_recycle_restore_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> (command_json::jsonb->'Source'->>'OperationId')::uuid AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_recycle_restore_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recycle_restore_history", "(NOT history_verified OR history_digest = command_json::jsonb->>'HistoryDigest') AND ((history_digest IS NOT NULL) = history_verified) AND (history_digest IS NULL OR history_digest ~ '^[A-F0-9]{64}$') AND (execution_job_id IS NOT NULL OR root_state IS NULL AND NOT history_verified AND history_digest IS NULL) AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state = 'Aborted')");
                table.CheckConstraint("ck_container_recycle_restore_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_recycle_restore_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_recycle_restore_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_recycle_restore_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_recycle_restore_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_recycle_restore_planned", "plan_json IS NULL OR whaledeck_recycle_restore_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_restore_receipt", "(root_state IS NULL AND NOT history_verified AND history_digest IS NULL AND receipt_json IS NULL AND receipt_hash IS NULL AND source_state IS NULL AND source_retained IS NULL AND source_restored IS NULL AND restored_by_this_operation IS NULL AND binding_current IS NULL AND history_available IS NULL) OR ((root_state IN ('Completed','Aborted','Unknown') AND receipt_json IS NOT NULL AND receipt_hash IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND octet_length(receipt_json) BETWEEN 1 AND 65536 AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND binding_current IS NOT NULL AND history_available IS NOT NULL AND (NOT history_available OR history_verified) AND (NOT binding_current OR root_state = 'Completed' AND history_available) AND (source_state IS NULL OR source_state IN ('Trashed','Restored','Aborted','Unknown')) AND CASE WHEN root_state='Unknown' THEN history_verified AND source_retained IS NULL AND source_restored IS NULL AND restored_by_this_operation IS NULL WHEN root_state='Completed' THEN history_verified AND source_state='Restored' AND source_retained=false AND source_restored=true AND restored_by_this_operation=true ELSE restored_by_this_operation=false AND CASE WHEN history_verified THEN source_state='Trashed' AND source_retained=true AND source_restored=false ELSE source_state IS NULL AND source_retained IS NULL AND source_restored IS NULL END END) IS TRUE)");
                table.CheckConstraint("ck_container_recycle_restore_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id' = execution_job_id::text AND receipt_json::jsonb->>'SourceOperationId' = command_json::jsonb->'Source'->>'OperationId' AND receipt_json::jsonb->>'Fingerprint' = plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'TargetProof' = plan_json::jsonb->>'TargetProof' AND receipt_json::jsonb->>'State' = root_state AND (receipt_json::jsonb->>'SourceState') IS NOT DISTINCT FROM source_state AND (receipt_json::jsonb->>'SourceRetained')::boolean IS NOT DISTINCT FROM source_retained AND (receipt_json::jsonb->>'SourceRestored')::boolean IS NOT DISTINCT FROM source_restored AND (receipt_json::jsonb->>'RestoredByThisOperation')::boolean IS NOT DISTINCT FROM restored_by_this_operation AND (receipt_json::jsonb->>'BindingCurrent')::boolean = binding_current AND (receipt_json::jsonb->>'HistoryAvailable')::boolean = history_available AND (receipt_json::jsonb->>'HistoryVerified')::boolean = history_verified AND receipt_json::jsonb->>'HistoryDigest' = COALESCE(history_digest,'')) IS TRUE)");
                table.CheckConstraint("ck_container_recycle_restore_receipt_shape", "receipt_json IS NULL OR whaledeck_recycle_restore_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_restore_requested", "whaledeck_recycle_restore_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.CheckConstraint("ck_container_recycle_restore_result", "((state <> 'Succeeded' OR root_state = 'Completed' AND binding_current) AND (state <> 'Superseded' OR root_state = 'Completed' AND NOT binding_current) AND (state NOT IN ('Succeeded','Superseded') OR history_verified AND root_started_at_utc IS NOT NULL) AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state = 'Aborted')) IS TRUE");
                table.CheckConstraint("ck_container_recycle_restore_source", "source_intent_id <> id AND source_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND source_proof_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recycle_restore_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_recycle_restore_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_recycle_restore_intents_container_recycle_intents~",
                    column: x => x.source_intent_id,
                    principalTable: "container_recycle_intents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_restore_intents_operation_jobs_execution_~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_restore_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_actor_subject_idempotency~",
            table: "container_recycle_restore_intents",
            columns: ActorKeyColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_container_id_completed_at~",
            table: "container_recycle_restore_intents",
            columns: ContainerCompletionColumns);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_execution_job_id",
            table: "container_recycle_restore_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_lease_token",
            table: "container_recycle_restore_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_next_attempt_at_utc_id",
            table: "container_recycle_restore_intents",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_publish_barrier",
            table: "container_recycle_restore_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_restore_intents_source_intent_id",
            table: "container_recycle_restore_intents",
            column: "source_intent_id");
        migrationBuilder.Sql(GuardAndFences);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $guard$ BEGIN IF EXISTS (SELECT 1 FROM container_recycle_restore_intents) THEN RAISE EXCEPTION 'container_recycle_restore_downgrade_requires_empty_table'; END IF; END $guard$;");
        migrationBuilder.Sql("""
            DROP TRIGGER whaledeck_recycle_restore_fence_0 ON file_path_locks;
            DROP TRIGGER whaledeck_recycle_restore_fence_1 ON container_file_upload_operations;
            DROP TRIGGER whaledeck_recycle_restore_fence_2 ON container_directory_operations;
            DROP TRIGGER whaledeck_recycle_restore_fence_3 ON container_copy_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_4 ON container_move_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_5 ON container_permission_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_6 ON container_ownership_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_7 ON container_recursive_permission_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_8 ON container_recursive_ownership_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_9 ON container_acl_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_10 ON container_recursive_acl_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_11 ON container_recursive_acl_restore_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_12 ON container_recycle_restore_intents;
            DROP TRIGGER whaledeck_recycle_restore_fence_13 ON container_recycle_intents;
            DROP TRIGGER container_recycle_restore_guard ON container_recycle_restore_intents;
            DROP FUNCTION whaledeck_container_recycle_restore_fence();
            DROP FUNCTION whaledeck_container_recycle_restore_guard();
            """);
        migrationBuilder.DropTable(
            name: "container_recycle_restore_intents");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_recycle_restore_receipt_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_restore_plan_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_restore_command_valid(jsonb,text,text,text);");
    }
}
