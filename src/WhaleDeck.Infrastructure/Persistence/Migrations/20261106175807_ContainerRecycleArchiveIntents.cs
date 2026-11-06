using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerRecycleArchiveIntents : Migration
{
    private static readonly string[] ActorKeyColumns = ["actor_subject", "idempotency_key"];
    private static readonly string[] ContainerCompletionColumns = ["container_id", "completed_at_utc"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ShapeFunctions);
        migrationBuilder.CreateTable(
            name: "container_recycle_archive_intents",
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
                apply_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reservation_json = table.Column<string>(type: "text", nullable: true),
                reservation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                archive_copied = table.Column<bool>(type: "boolean", nullable: true),
                source_fenced = table.Column<bool>(type: "boolean", nullable: true),
                owned_fence = table.Column<bool>(type: "boolean", nullable: true),
                binding_current = table.Column<bool>(type: "boolean", nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_recycle_archive_intents", x => x.id);
                table.CheckConstraint("ck_container_recycle_archive_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_recycle_archive_apply_entry", "(apply_started_at_utc IS NULL OR (root_started_at_utc IS NOT NULL AND reservation_json IS NOT NULL AND apply_started_at_utc>=root_started_at_utc AND apply_started_at_utc<execution_expires_at_utc)) AND (state<>'Applying' OR apply_started_at_utc IS NOT NULL) AND (root_started_at_utc IS NULL OR (root_started_at_utc>=confirmed_at_utc AND root_started_at_utc<execution_expires_at_utc))");
                table.CheckConstraint("ck_container_recycle_archive_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state NOT IN ('Reserving','Applying','Unknown') OR publish_barrier)");
                table.CheckConstraint("ck_container_recycle_archive_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> (command_json::jsonb->'Purge'->'Plan'->'Command'->'Source'->>'OperationId')::uuid AND execution_job_id <> (command_json::jsonb->'Purge'->>'OperationId')::uuid AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_recycle_archive_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recycle_archive_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_recycle_archive_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_recycle_archive_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_recycle_archive_phase", "(state NOT IN ('Ready','Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_recycle_archive_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_recycle_archive_planned", "plan_json IS NULL OR whaledeck_recycle_archive_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_archive_receipt", "(receipt_json IS NULL AND receipt_hash IS NULL AND root_state IS NULL AND archive_copied IS NULL AND source_fenced IS NULL AND owned_fence IS NULL AND binding_current IS NULL) OR ((receipt_json IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND root_state IN ('Completed','Aborted','Unknown') AND source_fenced IS NOT NULL AND owned_fence IS NOT NULL AND (NOT owned_fence OR source_fenced) AND CASE WHEN root_state='Completed' THEN archive_copied=true AND source_fenced WHEN root_state='Unknown' THEN archive_copied IS NULL AND source_fenced ELSE archive_copied=false AND NOT owned_fence END) IS TRUE)");
                table.CheckConstraint("ck_container_recycle_archive_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'Id'=execution_job_id::text AND receipt_json::jsonb->>'SourceOperationId'=command_json::jsonb->'Purge'->'Plan'->'Command'->'Source'->>'OperationId' AND receipt_json::jsonb->>'Fingerprint'=plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'State'=root_state AND (receipt_json::jsonb->>'ArchiveCopied')::boolean IS NOT DISTINCT FROM archive_copied AND (receipt_json::jsonb->>'SourceFenced')::boolean=source_fenced AND (receipt_json::jsonb->>'OwnedFence')::boolean=owned_fence AND (receipt_json::jsonb->>'BindingCurrent')::boolean IS NOT DISTINCT FROM binding_current AND receipt_json::jsonb->>'PurgeOperationId'=command_json::jsonb->'Purge'->>'OperationId') IS TRUE)");
                table.CheckConstraint("ck_container_recycle_archive_receipt_shape", "receipt_json IS NULL OR whaledeck_recycle_archive_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_archive_requested", "whaledeck_recycle_archive_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.CheckConstraint("ck_container_recycle_archive_reservation_shape", "reservation_json IS NULL OR whaledeck_recycle_archive_receipt_valid(reservation_json::jsonb,plan_json::jsonb)");
                table.CheckConstraint("ck_container_recycle_archive_reserved", "(reservation_json IS NULL AND reservation_hash IS NULL) OR ((reservation_json IS NOT NULL AND reservation_hash ~ '^[A-F0-9]{64}$' AND root_started_at_utc IS NOT NULL AND reservation_json::jsonb->>'State'='Reserved' AND reservation_json::jsonb->>'Id'=execution_job_id::text AND reservation_json::jsonb->>'SourceOperationId'=command_json::jsonb->'Purge'->'Plan'->'Command'->'Source'->>'OperationId' AND reservation_json::jsonb->>'PurgeOperationId'=command_json::jsonb->'Purge'->>'OperationId' AND reservation_json::jsonb->>'Fingerprint'=plan_json::jsonb->>'Fingerprint') IS TRUE)");
                table.CheckConstraint("ck_container_recycle_archive_result", "((state<>'Succeeded' OR root_state='Completed' AND archive_copied AND root_started_at_utc IS NOT NULL AND apply_started_at_utc IS NOT NULL AND reservation_json IS NOT NULL) AND (state<>'Unknown' OR root_state IS NULL OR root_state='Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state='Aborted') AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state='Aborted')) IS TRUE");
                table.CheckConstraint("ck_container_recycle_archive_source", "source_intent_id <> id AND source_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND source_proof_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recycle_archive_state", "state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_recycle_archive_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_recycle_archive_intents_container_recycle_purge_i~",
                    column: x => x.source_intent_id,
                    principalTable: "container_recycle_purge_intents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_archive_intents_operation_jobs_execution_~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_archive_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_actor_subject_idempotency~",
            table: "container_recycle_archive_intents",
            columns: ActorKeyColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_container_id_completed_at~",
            table: "container_recycle_archive_intents",
            columns: ContainerCompletionColumns);

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_execution_job_id",
            table: "container_recycle_archive_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_lease_token",
            table: "container_recycle_archive_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_next_attempt_at_utc_id",
            table: "container_recycle_archive_intents",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_publish_barrier",
            table: "container_recycle_archive_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");

        migrationBuilder.CreateIndex(
            name: "IX_container_recycle_archive_intents_source_intent_id",
            table: "container_recycle_archive_intents",
            column: "source_intent_id",
            unique: true,
            filter: "completed_at_utc IS NULL OR root_state='Unknown' OR archive_copied IS TRUE");
                migrationBuilder.Sql(GuardAndFences);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $g$ BEGIN IF EXISTS(SELECT 1 FROM container_recycle_archive_intents) THEN RAISE EXCEPTION 'container_recycle_archive_downgrade_requires_empty_table'; END IF; END $g$;");
        migrationBuilder.Sql("DROP TRIGGER container_recycle_archive_guard ON container_recycle_archive_intents; DROP TRIGGER whaledeck_recycle_archive_fence_0 ON file_path_locks; DROP TRIGGER whaledeck_recycle_archive_fence_1 ON container_file_upload_operations; DROP TRIGGER whaledeck_recycle_archive_fence_2 ON container_directory_operations; DROP TRIGGER whaledeck_recycle_archive_fence_3 ON container_copy_intents; DROP TRIGGER whaledeck_recycle_archive_fence_4 ON container_move_intents; DROP TRIGGER whaledeck_recycle_archive_fence_5 ON container_permission_intents; DROP TRIGGER whaledeck_recycle_archive_fence_6 ON container_ownership_intents; DROP TRIGGER whaledeck_recycle_archive_fence_7 ON container_recursive_permission_intents; DROP TRIGGER whaledeck_recycle_archive_fence_8 ON container_recursive_ownership_intents; DROP TRIGGER whaledeck_recycle_archive_fence_9 ON container_acl_intents; DROP TRIGGER whaledeck_recycle_archive_fence_10 ON container_recursive_acl_intents; DROP TRIGGER whaledeck_recycle_archive_fence_11 ON container_recursive_acl_restore_intents; DROP TRIGGER whaledeck_recycle_archive_fence_12 ON container_recycle_intents; DROP TRIGGER whaledeck_recycle_archive_fence_13 ON container_recycle_restore_intents; DROP TRIGGER whaledeck_recycle_archive_fence_14 ON container_recycle_archive_intents; DROP TRIGGER whaledeck_recycle_archive_fence_15 ON container_recycle_purge_intents; DROP TRIGGER container_recycle_archive_source_fence ON container_recycle_archive_intents;");
        migrationBuilder.DropTable(name: "container_recycle_archive_intents");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_container_recycle_archive_guard(); DROP FUNCTION whaledeck_container_recycle_archive_fence(); DROP FUNCTION whaledeck_recycle_archive_source_fence(); DROP FUNCTION whaledeck_recycle_archive_receipt_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_archive_plan_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recycle_archive_command_valid(jsonb,text,text,text);");
    }
}
