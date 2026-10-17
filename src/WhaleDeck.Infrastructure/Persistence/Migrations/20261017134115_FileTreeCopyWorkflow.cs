using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class FileTreeCopyWorkflow : Migration
    {
        private static readonly string[] IntentKeyColumns = ["actor_subject", "login_binding", "idempotency_key"];
        private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_tree_copy_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    partial_retained = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_tree_copy_operations", x => x.id);
                    table.CheckConstraint("ck_tree_copy_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_json IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (root_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('CopyQueued','Copying','Unknown','Succeeded','Acknowledged') OR execution_job_id IS NOT NULL)");
                    table.CheckConstraint("ck_tree_copy_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND source_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                    table.CheckConstraint("ck_tree_copy_lease", "(lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                    table.CheckConstraint("ck_tree_copy_limits", "version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(command_json::text)<=32768 AND jsonb_typeof(command_json)='object' AND (plan_json IS NULL OR (octet_length(plan_json::text)<=32768 AND jsonb_typeof(plan_json)='object')) AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$'");
                    table.CheckConstraint("ck_tree_copy_plan", "(plan_json IS NULL)=(plan_hash IS NULL) AND (plan_json IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc>created_at_utc) AND (state NOT IN ('Ready','CopyQueued','Copying','Unknown','Succeeded','Acknowledged') OR plan_json IS NOT NULL)");
                    table.CheckConstraint("ck_tree_copy_result", "(state='Succeeded')=(published_version IS NOT NULL) AND (NOT partial_retained OR state IN ('Unknown','Failed','Canceled','Acknowledged'))");
                    table.CheckConstraint("ck_tree_copy_state", "state IN ('Queued','Planning','Ready','CopyQueued','Copying','Unknown','Succeeded','Failed','Canceled','Expired','Acknowledged')");
                    table.CheckConstraint("ck_tree_copy_terminal", "(completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','CopyQueued','Copying','Unknown'))");
                    table.ForeignKey(
                        name: "FK_file_tree_copy_operations_operation_jobs_execution_job_id",
                        column: x => x.execution_job_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_tree_copy_operations_operation_jobs_id",
                        column: x => x.id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_tree_copy_operations_actor_subject_login_binding_idemp~",
                table: "file_tree_copy_operations",
                columns: IntentKeyColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_tree_copy_operations_execution_job_id",
                table: "file_tree_copy_operations",
                column: "execution_job_id",
                unique: true,
                filter: "execution_job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_tree_copy_operations_lease_token",
                table: "file_tree_copy_operations",
                column: "lease_token",
                unique: true,
                filter: "lease_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_tree_copy_operations_next_attempt_at_utc_id",
                table: "file_tree_copy_operations",
                columns: DueColumns,
                filter: "completed_at_utc IS NULL");

            migrationBuilder.Sql("""
                CREATE FUNCTION whaledeck_tree_copy_guard() RETURNS trigger
                LANGUAGE plpgsql AS $function$
                BEGIN
                  IF TG_OP='DELETE' THEN
                    RAISE EXCEPTION 'tree_copy_permanent_intent' USING ERRCODE='23514';
                  END IF;
                  IF ROW(NEW.id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.owner_hash,NEW.request_hash,NEW.source_hash,NEW.target_hash,NEW.command_json,NEW.created_at_utc,NEW.planning_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.owner_hash,OLD.request_hash,OLD.source_hash,OLD.target_hash,OLD.command_json,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                    RAISE EXCEPTION 'tree_copy_request_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
                    RAISE EXCEPTION 'tree_copy_plan_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.execution_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.execution_expires_at_utc) THEN
                    RAISE EXCEPTION 'tree_copy_execution_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
                    RAISE EXCEPTION 'tree_copy_started_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
                    RAISE EXCEPTION 'tree_copy_terminal_immutable' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $function$;
                CREATE TRIGGER tree_copy_guard BEFORE UPDATE OR DELETE ON file_tree_copy_operations
                FOR EACH ROW EXECUTE FUNCTION whaledeck_tree_copy_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM file_tree_copy_operations) THEN
                    RAISE EXCEPTION 'tree_copy_downgrade_requires_empty_table';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "file_tree_copy_operations");
            migrationBuilder.Sql("DROP FUNCTION whaledeck_tree_copy_guard();");
        }
    }
