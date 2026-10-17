using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class FileHistoryAndRecycle : Migration
    {
        private static readonly string[] ActorIndexColumns = ["actor_subject", "created_at_utc"];
        private static readonly string[] DueIndexColumns = ["next_attempt_at_utc", "id"];
        private static readonly string[] ObservationIndexColumns = ["availability", "last_observed_at_utc"];
        private static readonly string[] CaptureIndexColumns = ["root_id", "capture_kind", "capture_state", "captured_at_utc", "id"];
        private static readonly string[] TargetIndexColumns = ["root_id", "target_hash", "captured_at_utc", "id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    root_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    target_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    capture_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    capture_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    different_source_device = table.Column<bool>(type: "boolean", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_full_verification_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_versions", x => x.id);
                    table.CheckConstraint("ck_file_version_actor", "(source_job_id IS NULL)=(created_by_subject IS NULL) AND (source_job_id IS NULL OR source_job_id=id)");
                    table.CheckConstraint("ck_file_version_availability", "availability IN ('Present','Missing','Incomplete','Invalid','Purged') AND ((capture_state='Purged')=(availability='Purged'))");
                    table.CheckConstraint("ck_file_version_capture", "(capture_kind='Trash' AND capture_state IN ('Pending','Trashed','Restored','Canceled','Purged')) OR (capture_kind='Overwrite' AND capture_state IN ('Available','Purged'))");
                    table.CheckConstraint("ck_file_version_hashes", "target_hash ~ '^[A-F0-9]{64}$' AND source_version ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("ck_file_version_limits", "size_bytes BETWEEN 0 AND 1073741824 AND version>=0 AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND captured_at_utc >= TIMESTAMPTZ '1970-01-01 00:00:00+00'");
                    table.ForeignKey(
                        name: "FK_file_versions_operation_jobs_last_operation_id",
                        column: x => x.last_operation_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_versions_operation_jobs_source_job_id",
                        column: x => x.source_job_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_archive_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    root_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    target_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    archive_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    plan_json = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_retained = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_archive_operations", x => x.id);
                    table.CheckConstraint("ck_file_archive_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND target_hash ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND (expected_version='ABSENT' OR expected_version ~ '^[A-F0-9]{64}$')");
                    table.CheckConstraint("ck_file_archive_kind", "(kind='trash' AND archive_version_id IS NULL) OR (kind IN ('restore','purge') AND archive_version_id IS NOT NULL)");
                    table.CheckConstraint("ck_file_archive_limits", "size_bytes BETWEEN 0 AND 1073741824 AND version>=0 AND expires_at_utc>created_at_utc AND octet_length(plan_json::text)<=32768 AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096");
                    table.CheckConstraint("ck_file_archive_source", "(kind='trash' AND expected_version<>'ABSENT') OR kind='restore' OR (kind='purge' AND expected_version='ABSENT')");
                    table.CheckConstraint("ck_file_archive_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                    table.CheckConstraint("ck_file_archive_terminal", "(completed_at_utc IS NULL)=(state IN ('Pending','Running','Unknown'))");
                    table.ForeignKey(
                        name: "FK_file_archive_operations_file_versions_archive_version_id",
                        column: x => x.archive_version_id,
                        principalTable: "file_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_file_archive_operations_operation_jobs_id",
                        column: x => x.id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_operations_actor_subject_created_at_utc",
                table: "file_archive_operations",
                columns: ActorIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_operations_archive_version_id",
                table: "file_archive_operations",
                column: "archive_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_operations_lease_token",
                table: "file_archive_operations",
                column: "lease_token",
                unique: true,
                filter: "lease_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_operations_next_attempt_at_utc_id",
                table: "file_archive_operations",
                columns: DueIndexColumns,
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_archive_operations_target_hash",
                table: "file_archive_operations",
                column: "target_hash",
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_availability_last_observed_at_utc",
                table: "file_versions",
                columns: ObservationIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_last_operation_id",
                table: "file_versions",
                column: "last_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_root_id_capture_kind_capture_state_captured_a~",
                table: "file_versions",
                columns: CaptureIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_root_id_target_hash_captured_at_utc_id",
                table: "file_versions",
                columns: TargetIndexColumns);

            migrationBuilder.CreateIndex(
                name: "IX_file_versions_source_job_id",
                table: "file_versions",
                column: "source_job_id",
                unique: true,
                filter: "source_job_id IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE FUNCTION whaledeck_file_version_guard() RETURNS trigger
                LANGUAGE plpgsql AS $function$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF ROW(NEW.id,NEW.root_id,NEW.relative_path,NEW.target_hash,NEW.source_version,
                               NEW.sha256,NEW.size_bytes,NEW.capture_kind,NEW.captured_at_utc,NEW.different_source_device)
                           IS DISTINCT FROM
                           ROW(OLD.id,OLD.root_id,OLD.relative_path,OLD.target_hash,OLD.source_version,
                               OLD.sha256,OLD.size_bytes,OLD.capture_kind,OLD.captured_at_utc,OLD.different_source_device) THEN
                            RAISE EXCEPTION 'file_version_identity_immutable' USING ERRCODE='23514';
                        END IF;
                        IF OLD.source_job_id IS NOT NULL AND
                           ROW(NEW.source_job_id,NEW.created_by_subject) IS DISTINCT FROM ROW(OLD.source_job_id,OLD.created_by_subject) THEN
                            RAISE EXCEPTION 'file_version_actor_immutable' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    IF NEW.source_job_id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM operation_jobs WHERE id=NEW.source_job_id AND actor_subject=NEW.created_by_subject
                    ) THEN
                        RAISE EXCEPTION 'file_version_actor_mismatch' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END
                $function$;
                CREATE TRIGGER file_version_guard BEFORE INSERT OR UPDATE ON file_versions
                FOR EACH ROW EXECUTE FUNCTION whaledeck_file_version_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_archive_operations");

            migrationBuilder.DropTable(
                name: "file_versions");

            migrationBuilder.Sql("DROP FUNCTION whaledeck_file_version_guard();");
        }
    }
