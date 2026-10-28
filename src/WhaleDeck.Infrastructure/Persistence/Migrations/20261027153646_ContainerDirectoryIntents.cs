using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class ContainerDirectoryIntents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "container_directory_operations",
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
                plan_json = table.Column<string>(type: "text", nullable: false),
                plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                publish_barrier = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                acknowledgement_json = table.Column<string>(type: "text", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_directory_operations", x => x.id);
                table.CheckConstraint("ck_container_directory_acknowledgement", "(state = 'Acknowledged') = (acknowledged_by IS NOT NULL AND acknowledgement_json IS NOT NULL) AND (acknowledged_by IS NULL) = (acknowledgement_json IS NULL) AND (acknowledgement_json IS NULL OR octet_length(acknowledgement_json) BETWEEN 1 AND 32768)");
                table.CheckConstraint("ck_container_directory_barrier", "(NOT publish_barrier OR completed_at_utc IS NULL) AND (root_started_at_utc IS NULL OR publish_barrier OR completed_at_utc IS NOT NULL) AND (state <> 'Applying' OR (publish_barrier AND root_started_at_utc IS NOT NULL))");
                table.CheckConstraint("ck_container_directory_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND plan_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_directory_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128 AND octet_length(plan_json) BETWEEN 1 AND 32768");
                table.CheckConstraint("ck_container_directory_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_container_directory_lifetime", "expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00' AND version >= 0");
                table.CheckConstraint("ck_container_directory_state", "state IN ('Pending','Applying','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                table.CheckConstraint("ck_container_directory_terminal", "(completed_at_utc IS NULL) = (state IN ('Pending','Applying','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_directory_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_directory_operations_actor_subject_idempotency_key",
            table: "container_directory_operations",
            columns: ["actor_subject", "idempotency_key"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_directory_operations_container_id_completed_at_utc",
            table: "container_directory_operations",
            columns: ["container_id", "completed_at_utc"]);

        migrationBuilder.CreateIndex(
            name: "IX_container_directory_operations_next_attempt_at_utc_id",
            table: "container_directory_operations",
            columns: ["next_attempt_at_utc", "id"],
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_directory_operations_publish_barrier",
            table: "container_directory_operations",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");

        // DB-level backstop also protects rolling upgrades whose old API
        // still inserts host reservations without knowing the new table.
        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_container_directory_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
            BEGIN
              PERFORM pg_advisory_xact_lock(41307341);
              IF TG_TABLE_NAME = 'file_path_locks' THEN
                IF EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE = '55000';
                END IF;
              ELSIF TG_TABLE_NAME = 'container_file_upload_operations' THEN
                IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) THEN
                  RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE = '55000';
                END IF;
              ELSE
                IF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
                    EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier)) THEN
                  RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE = '55000';
                END IF;
              END IF;
              RETURN NEW;
            END $f$;
            CREATE TRIGGER whaledeck_directory_host_path_fence BEFORE INSERT ON file_path_locks
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_directory_fence();
            CREATE TRIGGER whaledeck_directory_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_directory_fence();
            CREATE TRIGGER whaledeck_directory_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
              FOR EACH ROW EXECUTE FUNCTION whaledeck_container_directory_fence();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $f$ BEGIN
              IF EXISTS (SELECT 1 FROM container_directory_operations) THEN
                RAISE EXCEPTION 'CONTAINER_DIRECTORY_HISTORY_REQUIRES_RETENTION';
              END IF;
            END $f$;
            DROP TRIGGER whaledeck_directory_host_path_fence ON file_path_locks;
            DROP TRIGGER whaledeck_directory_upload_fence ON container_file_upload_operations;
            DROP TRIGGER whaledeck_directory_publication_fence ON container_directory_operations;
            DROP FUNCTION whaledeck_container_directory_fence();
            """);
        migrationBuilder.DropTable(
            name: "container_directory_operations");
    }
}
