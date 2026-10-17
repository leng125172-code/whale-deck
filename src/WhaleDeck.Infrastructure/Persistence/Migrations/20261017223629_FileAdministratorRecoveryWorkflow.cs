using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class FileAdministratorRecoveryWorkflow : Migration
    {
        private static readonly string[] IdentityColumns = ["actor_subject", "login_binding", "idempotency_key"];
        private static readonly string[] OriginalColumns = ["kind", "original_intent_id"];
        private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_recovery_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    original_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_execution_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_version = table.Column<long>(type: "bigint", nullable: false),
                    original_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_json = table.Column<string>(type: "jsonb", nullable: false),
                    disposition = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planning_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    root_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    observation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    observation_json = table.Column<string>(type: "jsonb", nullable: true),
                    observation_binding_current = table.Column<bool>(type: "boolean", nullable: true),
                    ready_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    execution_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fence_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    original_resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_recovery_operations", x => x.id);
                    table.CheckConstraint("ck_file_recovery_execution", "(execution_job_id IS NULL)=(execution_expires_at_utc IS NULL) AND (execution_job_id IS NULL OR (plan_hash IS NOT NULL AND execution_job_id<>id AND execution_expires_at_utc>created_at_utc)) AND (fence_started_at_utc IS NULL OR execution_job_id IS NOT NULL) AND (state NOT IN ('DispositionQueued','Fencing','Unknown','Disposed') OR execution_job_id IS NOT NULL) AND (lease_token IS NULL)=(lease_expires_at_utc IS NULL)");
                    table.CheckConstraint("ck_file_recovery_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND original_binding_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (plan_hash IS NULL OR plan_hash ~ '^[A-F0-9]{64}$') AND (observation_hash IS NULL OR observation_hash ~ '^[A-F0-9]{64}$')");
                    table.CheckConstraint("ck_file_recovery_kind", "kind IN ('upload','mutation','archive','tree-copy','tree-move') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
                    table.CheckConstraint("ck_file_recovery_limits", "version>=0 AND original_version>=0 AND planning_expires_at_utc=created_at_utc+INTERVAL '15 minutes' AND octet_length(original_json::text)<=262144 AND jsonb_typeof(original_json)='object' AND idempotency_key ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' AND (observation_json IS NULL OR octet_length(observation_json::text)<=16384)");
                    table.CheckConstraint("ck_file_recovery_plan", "(plan_hash IS NULL)=(observation_hash IS NULL) AND (plan_hash IS NULL)=(observation_json IS NULL) AND (plan_hash IS NULL)=(root_state IS NULL) AND (plan_hash IS NULL)=(observation_binding_current IS NULL) AND (plan_hash IS NULL)=(ready_at_utc IS NULL) AND (plan_hash IS NULL)=(ready_expires_at_utc IS NULL) AND (ready_expires_at_utc IS NULL OR ready_expires_at_utc=ready_at_utc+INTERVAL '5 minutes') AND (root_state IS NULL OR root_state IN ('Missing','Aborted','Unknown','Completed')) AND (state NOT IN ('Ready','DispositionQueued','Fencing','Unknown','Disposed') OR plan_hash IS NOT NULL)");
                    table.CheckConstraint("ck_file_recovery_resolution", "(state='Disposed')=(original_resolution IS NOT NULL) AND (original_resolution IS NULL OR original_resolution IN ('Acknowledged','Succeeded','Superseded','Failed'))");
                    table.CheckConstraint("ck_file_recovery_state", "state IN ('Queued','Planning','Ready','DispositionQueued','Fencing','Unknown','Disposed','Failed','Canceled','Expired') AND (completed_at_utc IS NULL)=(state IN ('Queued','Planning','Ready','DispositionQueued','Fencing','Unknown'))");
                    table.ForeignKey(
                        name: "FK_file_recovery_operations_operation_jobs_execution_job_id",
                        column: x => x.execution_job_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_recovery_operations_operation_jobs_id",
                        column: x => x.id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_recovery_operations_operation_jobs_original_execution_~",
                        column: x => x.original_execution_job_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_actor_subject_login_binding_idempo~",
                table: "file_recovery_operations",
                columns: IdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_execution_job_id",
                table: "file_recovery_operations",
                column: "execution_job_id",
                unique: true,
                filter: "execution_job_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_kind_original_intent_id",
                table: "file_recovery_operations",
                columns: OriginalColumns,
                unique: true,
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_lease_token",
                table: "file_recovery_operations",
                column: "lease_token",
                unique: true,
                filter: "lease_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_next_attempt_at_utc_id",
                table: "file_recovery_operations",
                columns: DueColumns,
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_recovery_operations_original_execution_job_id",
                table: "file_recovery_operations",
                column: "original_execution_job_id");
            migrationBuilder.Sql("""
                CREATE FUNCTION whaledeck_file_recovery_guard() RETURNS trigger
                LANGUAGE plpgsql AS $function$
                BEGIN
                  IF TG_OP='DELETE' THEN
                    RAISE EXCEPTION 'file_recovery_permanent_intent' USING ERRCODE='23514';
                  END IF;
                  IF ROW(NEW.id,NEW.actor_subject,NEW.login_binding,NEW.idempotency_key,NEW.kind,NEW.original_intent_id,NEW.original_execution_job_id,NEW.original_version,NEW.original_binding_hash,NEW.original_json,NEW.disposition,NEW.reason_code,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.id,OLD.actor_subject,OLD.login_binding,OLD.idempotency_key,OLD.kind,OLD.original_intent_id,OLD.original_execution_job_id,OLD.original_version,OLD.original_binding_hash,OLD.original_json,OLD.disposition,OLD.reason_code,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
                    RAISE EXCEPTION 'file_recovery_request_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.plan_hash IS NOT NULL AND ROW(NEW.plan_hash,NEW.root_state,NEW.observation_hash,NEW.observation_json,NEW.observation_binding_current,NEW.ready_at_utc,NEW.ready_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.plan_hash,OLD.root_state,OLD.observation_hash,OLD.observation_json,OLD.observation_binding_current,OLD.ready_at_utc,OLD.ready_expires_at_utc) THEN
                    RAISE EXCEPTION 'file_recovery_plan_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.execution_expires_at_utc)
                     IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.execution_expires_at_utc) THEN
                    RAISE EXCEPTION 'file_recovery_execution_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.fence_started_at_utc IS NOT NULL AND NEW.fence_started_at_utc IS DISTINCT FROM OLD.fence_started_at_utc THEN
                    RAISE EXCEPTION 'file_recovery_fence_immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
                    RAISE EXCEPTION 'file_recovery_terminal_immutable' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $function$;
                CREATE TRIGGER file_recovery_guard BEFORE UPDATE OR DELETE ON file_recovery_operations
                FOR EACH ROW EXECUTE FUNCTION whaledeck_file_recovery_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM file_recovery_operations) THEN
                    RAISE EXCEPTION 'file_recovery_downgrade_requires_empty_table';
                  END IF;
                END $guard$;
                """);
            migrationBuilder.DropTable(
                name: "file_recovery_operations");
            migrationBuilder.Sql("DROP FUNCTION whaledeck_file_recovery_guard();");
        }
    }
