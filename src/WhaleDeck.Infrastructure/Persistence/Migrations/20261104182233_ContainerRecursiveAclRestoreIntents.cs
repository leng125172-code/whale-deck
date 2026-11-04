using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerRecursiveAclRestoreIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ShapeFunctions);
        migrationBuilder.CreateTable(
            name: "container_recursive_acl_restore_intents",
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
                table.PrimaryKey("PK_container_recursive_acl_restore_intents", x => x.id);
                table.CheckConstraint("ck_container_recursive_acl_restore_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_recursive_acl_restore_barrier", "(NOT publish_barrier OR (completed_at_utc IS NULL AND root_started_at_utc IS NOT NULL AND execution_job_id IS NOT NULL)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL AND (publish_barrier OR completed_at_utc IS NOT NULL)) AND (state NOT IN ('Applying','Partial') OR publish_barrier)");
                table.CheckConstraint("ck_container_recursive_acl_restore_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL) OR (execution_job_id IS NOT NULL AND execution_job_id <> id AND execution_job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND plan_json IS NOT NULL AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc AND execution_expires_at_utc IS NOT NULL AND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash IS NOT NULL AND execution_hash ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_recursive_acl_restore_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_recursive_acl_restore_history", "(NOT history_verified OR history_may_exist AND history_digest IS NOT NULL) AND ((history_digest IS NOT NULL) = history_verified) AND (history_digest IS NULL OR history_digest ~ '^[A-F0-9]{64}$') AND (execution_job_id IS NOT NULL OR root_state IS NULL AND NOT history_may_exist AND NOT history_verified AND history_digest IS NULL) AND (root_started_at_utc IS NOT NULL OR root_state IS NULL OR root_state = 'Aborted')");
                table.CheckConstraint("ck_container_recursive_acl_restore_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(command_json) BETWEEN 1 AND 65536");
                table.CheckConstraint("ck_container_recursive_acl_restore_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_recursive_acl_restore_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0");
                table.CheckConstraint("ck_container_recursive_acl_restore_phase", "(state NOT IN ('Ready','Pending','Applying','Unknown','Partial','Succeeded','Superseded','Acknowledged') OR plan_json IS NOT NULL) AND (state NOT IN ('Pending','Applying','Unknown','Partial','Succeeded','Superseded','Acknowledged') OR execution_job_id IS NOT NULL) AND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_container_recursive_acl_restore_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL) OR (plan_json IS NOT NULL AND octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash IS NOT NULL AND plan_hash ~ '^[A-F0-9]{64}$' AND ready_expires_at_utc IS NOT NULL AND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes')");
                table.CheckConstraint("ck_container_recursive_acl_restore_planned_acl", "plan_json IS NULL OR whaledeck_recursive_acl_restore_plan_valid(plan_json::jsonb,command_json::jsonb)");
                table.CheckConstraint("ck_container_recursive_acl_restore_receipt", "(root_state IS NULL AND receipt_json IS NULL AND receipt_hash IS NULL AND completed_count IS NULL AND changed_count IS NULL AND aborted_count IS NULL AND not_started_count IS NULL AND unknown_count IS NULL AND outcome_digest IS NULL AND binding_current IS NULL AND history_available IS NULL) OR ((root_state IN ('Completed','Partial','Aborted','Unknown') AND receipt_json IS NOT NULL AND receipt_hash IS NOT NULL AND receipt_hash ~ '^[A-F0-9]{64}$' AND octet_length(receipt_json) BETWEEN 1 AND 65536 AND execution_job_id IS NOT NULL AND plan_json IS NOT NULL AND completed_count BETWEEN 0 AND 10000 AND changed_count BETWEEN 0 AND completed_count AND aborted_count BETWEEN 0 AND 10000 AND not_started_count BETWEEN 0 AND 10000 AND unknown_count BETWEEN 0 AND 10000 AND binding_current IS NOT NULL AND history_available IS NOT NULL AND outcome_digest IS NOT NULL AND (outcome_digest = '' OR outcome_digest ~ '^[A-F0-9]{64}$') AND completed_count + aborted_count + not_started_count + unknown_count = (plan_json::jsonb->>'EntryCount')::integer AND changed_count <= (plan_json::jsonb->>'ChangeCount')::integer AND (NOT history_available OR history_verified) AND (NOT binding_current OR root_state = 'Completed' AND history_available) AND (root_state <> 'Completed' OR completed_count = (plan_json::jsonb->>'EntryCount')::integer AND changed_count = (plan_json::jsonb->>'ChangeCount')::integer AND history_verified AND outcome_digest ~ '^[A-F0-9]{64}$') AND (root_state <> 'Partial' OR changed_count > 0 AND completed_count < (plan_json::jsonb->>'EntryCount')::integer AND unknown_count = 0 AND history_verified AND outcome_digest ~ '^[A-F0-9]{64}$') AND (root_state <> 'Aborted' OR changed_count = 0 AND unknown_count = 0) AND (root_state <> 'Unknown' OR unknown_count > 0)) IS TRUE)");
                table.CheckConstraint("ck_container_recursive_acl_restore_receipt_binding", "receipt_json IS NULL OR ((receipt_json::jsonb->>'AclType' = plan_json::jsonb->'RestoreSource'->>'ChangedAclType' AND receipt_json::jsonb->>'Id' = execution_job_id::text AND receipt_json::jsonb->>'ContainerId' = container_id AND receipt_json::jsonb->>'InstanceId' = instance_id AND receipt_json::jsonb->>'Fingerprint' = plan_json::jsonb->>'Fingerprint' AND receipt_json::jsonb->>'TargetProof' = plan_json::jsonb->>'TargetProof' AND receipt_json::jsonb->>'State' = root_state AND (receipt_json::jsonb->>'CompletedCount')::integer = completed_count AND (receipt_json::jsonb->>'ChangedCount')::integer = changed_count AND (receipt_json::jsonb->>'AbortedCount')::integer = aborted_count AND (receipt_json::jsonb->>'NotStartedCount')::integer = not_started_count AND (receipt_json::jsonb->>'UnknownCount')::integer = unknown_count AND receipt_json::jsonb->>'OutcomeDigest' = outcome_digest AND (receipt_json::jsonb->>'BindingCurrent')::boolean = binding_current AND (receipt_json::jsonb->>'HistoryAvailable')::boolean = history_available AND (receipt_json::jsonb->>'HistoryVerified')::boolean = history_verified AND (NOT (receipt_json::jsonb->>'HistoryMayExist')::boolean OR history_may_exist) AND receipt_json::jsonb->>'HistoryDigest' = COALESCE(history_digest,'')) IS TRUE)");
                table.CheckConstraint("ck_container_recursive_acl_restore_receipt_shape", "receipt_json IS NULL OR whaledeck_recursive_acl_restore_receipt_valid(receipt_json::jsonb,plan_json::jsonb)");
                table.CheckConstraint("ck_container_recursive_acl_restore_requested_acl", "whaledeck_recursive_acl_restore_command_valid(command_json::jsonb,container_id,instance_id,relative_path)");
                table.CheckConstraint("ck_container_recursive_acl_restore_result", "((state NOT IN ('Succeeded','Superseded') OR root_state = 'Completed' AND history_verified AND root_started_at_utc IS NOT NULL AND binding_current = (state = 'Succeeded')) AND (state <> 'Partial' OR root_state = 'Partial' AND history_verified AND publish_barrier AND root_started_at_utc IS NOT NULL) AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown') AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed') OR root_state = 'Aborted')) IS TRUE");
                table.CheckConstraint("ck_container_recursive_acl_restore_state", "state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Partial','Succeeded','Superseded','Canceled','Failed','Expired','Acknowledged')");
                table.CheckConstraint("ck_container_recursive_acl_restore_terminal", "(completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Applying','Unknown','Partial'))");
                table.ForeignKey(
                    name: "FK_container_recursive_acl_restore_intents_operation_jobs_exec~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recursive_acl_restore_intents_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_actor_subject_idemp~",
            table: "container_recursive_acl_restore_intents",
            columns: ["actor_subject", "idempotency_key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_container_id_comple~",
            table: "container_recursive_acl_restore_intents",
            columns: ["container_id", "completed_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_execution_job_id",
            table: "container_recursive_acl_restore_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_lease_token",
            table: "container_recursive_acl_restore_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_next_attempt_at_utc~",
            table: "container_recursive_acl_restore_intents",
            columns: ["next_attempt_at_utc", "id"],
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_recursive_acl_restore_intents_publish_barrier",
            table: "container_recursive_acl_restore_intents",
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
      IF EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_downgrade_requires_empty_table';
      END IF;
    END $f$;
    DROP TRIGGER whaledeck_recursive_acl_restore_original_acl_fence ON container_recursive_acl_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_acl_fence ON container_acl_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_recursive_ownership_fence ON container_recursive_ownership_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_host_path_fence ON file_path_locks;
    DROP TRIGGER whaledeck_recursive_acl_restore_upload_fence ON container_file_upload_operations;
    DROP TRIGGER whaledeck_recursive_acl_restore_directory_fence ON container_directory_operations;
    DROP TRIGGER whaledeck_recursive_acl_restore_copy_fence ON container_copy_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_move_fence ON container_move_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_permission_fence ON container_permission_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_ownership_fence ON container_ownership_intents;
    DROP TRIGGER whaledeck_recursive_acl_restore_recursive_permission_fence ON container_recursive_permission_intents;
    """);

    migrationBuilder.DropTable(
        name: "container_recursive_acl_restore_intents");
    migrationBuilder.Sql("DROP FUNCTION whaledeck_container_recursive_acl_restore_fence(); DROP FUNCTION whaledeck_container_recursive_acl_restore_guard(); DROP FUNCTION whaledeck_recursive_acl_restore_receipt_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recursive_acl_restore_plan_valid(jsonb,jsonb); DROP FUNCTION whaledeck_recursive_acl_restore_command_valid(jsonb,text,text,text); DROP FUNCTION whaledeck_recursive_acl_restore_uint(jsonb,numeric); DROP FUNCTION whaledeck_recursive_acl_restore_sint(jsonb); DROP FUNCTION whaledeck_recursive_acl_restore_instance_valid(text);");
    }
}
