using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ContainerRecycleArchiveRetirementIntents : Migration
{
    private static readonly string[] ActorKeyColumns = ["actor_subject", "login_binding", "idempotency_key"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ShapeFunctions);
        migrationBuilder.CreateTable(
            name: "container_recycle_archive_retirement_intents",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                archive_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                retain_days = table.Column<int>(type: "integer", nullable: false),
                source_proof_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                trace_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                apply_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                reservation_json = table.Column<string>(type: "text", nullable: true),
                reservation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                receipt_json = table.Column<string>(type: "text", nullable: true),
                receipt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                root_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                retirement_applied = table.Column<bool>(type: "boolean", nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_recycle_archive_retirement_intents", x => x.id);
                table.CheckConstraint("ck_archive_retirement_ack", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL)\nAND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL)\nAND (acknowledgement_json IS NULL OR receipt_json IS NOT NULL AND length(acknowledged_by) > 0\nAND octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_archive_retirement_apply", "(apply_started_at_utc IS NULL OR (reservation_json IS NOT NULL AND apply_started_at_utc >= root_started_at_utc\nAND apply_started_at_utc < execution_expires_at_utc\nAND extract(epoch FROM apply_started_at_utc) * 1000 >= (plan_json::jsonb->>'NotBeforeUnixMs')::numeric) IS TRUE)\nAND (state <> 'Applying' OR apply_started_at_utc IS NOT NULL)");
                table.CheckConstraint("ck_archive_retirement_barrier", "(publish_barrier = (state IN ('Reserving','Applying','Unknown')))\nAND (NOT publish_barrier OR root_started_at_utc IS NOT NULL)\nAND (root_started_at_utc IS NULL OR ((publish_barrier OR completed_at_utc IS NOT NULL)\nAND root_started_at_utc >= confirmed_at_utc AND root_started_at_utc < execution_expires_at_utc) IS TRUE)");
                table.CheckConstraint("ck_archive_retirement_execution", "(execution_job_id IS NULL AND confirmed_at_utc IS NULL AND execution_expires_at_utc IS NULL AND execution_hash IS NULL)\nOR ((execution_job_id IS NOT NULL AND execution_job_id NOT IN (id, archive_intent_id, '00000000-0000-0000-0000-000000000000'::uuid)\nAND plan_json IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc < ready_expires_at_utc\nAND execution_expires_at_utc = confirmed_at_utc + INTERVAL '15 minutes' AND execution_hash ~ '^[A-F0-9]{64}$') IS TRUE)");
                table.CheckConstraint("ck_archive_retirement_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_proof_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_archive_retirement_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND archive_intent_id <> id AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND retain_days BETWEEN 1 AND 3650 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128");
                table.CheckConstraint("ck_archive_retirement_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL) AND (state NOT IN ('Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged') OR lease_token IS NULL)");
                table.CheckConstraint("ck_archive_retirement_lifetime", "created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND planning_expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND version >= 0 AND (completed_at_utc IS NULL OR completed_at_utc >= created_at_utc)");
                table.CheckConstraint("ck_archive_retirement_phase", "(state NOT IN ('Ready','Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)\nAND (state NOT IN ('Pending','Reserving','Applying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)\nAND (state NOT IN ('Queued','Planning','Ready') OR execution_job_id IS NULL)");
                table.CheckConstraint("ck_archive_retirement_plan", "(plan_json IS NULL AND plan_hash IS NULL AND ready_expires_at_utc IS NULL)\nOR ((octet_length(plan_json) BETWEEN 1 AND 65536 AND plan_hash ~ '^[A-F0-9]{64}$'\nAND ready_expires_at_utc > created_at_utc AND ready_expires_at_utc <= planning_expires_at_utc + INTERVAL '5 minutes'\nAND whaledeck_archive_retirement_plan_valid(plan_json::jsonb, retain_days)) IS TRUE)");
                table.CheckConstraint("ck_archive_retirement_receipt", "(receipt_json IS NULL AND receipt_hash IS NULL AND root_state IS NULL AND retirement_applied IS NULL)\nOR ((octet_length(receipt_json) BETWEEN 1 AND 32768 AND receipt_hash ~ '^[A-F0-9]{64}$'\nAND root_state IN ('Completed','Aborted','Unknown') AND receipt_json::jsonb->>'State' = root_state\nAND (receipt_json::jsonb->>'RetirementApplied')::boolean IS NOT DISTINCT FROM retirement_applied\nAND whaledeck_archive_retirement_receipt_valid(receipt_json::jsonb, plan_json::jsonb, execution_job_id)) IS TRUE)");
                table.CheckConstraint("ck_archive_retirement_reservation", "(reservation_json IS NULL AND reservation_hash IS NULL)\nOR ((octet_length(reservation_json) BETWEEN 1 AND 32768 AND reservation_hash ~ '^[A-F0-9]{64}$'\nAND root_started_at_utc IS NOT NULL AND reservation_json::jsonb->>'State' = 'Reserved'\nAND whaledeck_archive_retirement_receipt_valid(reservation_json::jsonb, plan_json::jsonb, execution_job_id)) IS TRUE)");
                table.CheckConstraint("ck_archive_retirement_result", "((root_state IS DISTINCT FROM 'Completed' OR apply_started_at_utc IS NOT NULL)\nAND (state <> 'Succeeded' OR root_state = 'Completed')\nAND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown')\nAND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed','Expired') OR root_state = 'Aborted')\nAND (state IN ('Succeeded','Unknown','Canceled','Failed','Expired','Acknowledged') OR root_state IS NULL)) IS TRUE");
                table.CheckConstraint("ck_archive_retirement_state", "state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown','Succeeded','Canceled','Failed','Expired','Acknowledged') AND (completed_at_utc IS NULL) = (state IN ('Queued','Planning','Ready','Pending','Reserving','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_recycle_archive_retirement_intents_container_recy~",
                    column: x => x.archive_intent_id,
                    principalTable: "container_recycle_archive_intents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_archive_retirement_intents_operation_jobs~",
                    column: x => x.execution_job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_container_recycle_archive_retirement_intents_operation_job~1",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_active_archive",
            table: "container_recycle_archive_retirement_intents",
            column: "archive_intent_id",
            unique: true,
            filter: "completed_at_utc IS NULL OR root_state IN ('Unknown','Completed')");

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_actor_key",
            table: "container_recycle_archive_retirement_intents",
            columns: ActorKeyColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_archive",
            table: "container_recycle_archive_retirement_intents",
            column: "archive_intent_id");

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_barrier",
            table: "container_recycle_archive_retirement_intents",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_due",
            table: "container_recycle_archive_retirement_intents",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_job",
            table: "container_recycle_archive_retirement_intents",
            column: "execution_job_id",
            unique: true,
            filter: "execution_job_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_archive_retirement_lease",
            table: "container_recycle_archive_retirement_intents",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");
        migrationBuilder.Sql(GuardAndFences);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE container_recycle_archive_retirement_intents IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
              IF EXISTS(SELECT 1 FROM container_recycle_archive_retirement_intents) THEN
                RAISE EXCEPTION 'archive_retirement_permanent_intent' USING ERRCODE='23514';
              END IF;
            END $f$;
            """);
        migrationBuilder.Sql(DropFences);
        migrationBuilder.DropTable(name: "container_recycle_archive_retirement_intents");
        migrationBuilder.Sql(DropShapeFunctions);
    }
}
