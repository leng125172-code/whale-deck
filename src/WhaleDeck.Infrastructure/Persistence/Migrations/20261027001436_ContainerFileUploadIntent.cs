using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ContainerFileUploadIntent : Migration
{
    private static readonly string[] ActorColumns = ["actor_subject", "idempotency_key"];
    private static readonly string[] TargetColumns = ["container_id", "completed_at_utc"];
    private static readonly string[] DueColumns = ["next_attempt_at_utc", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "container_file_upload_operations",
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
                target_proof = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expected_parent_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expected_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                previous_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                receive_token = table.Column<Guid>(type: "uuid", nullable: true),
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
                previous_retained = table.Column<bool>(type: "boolean", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_file_upload_operations", x => x.id);
                table.CheckConstraint("ck_container_upload_barrier", "(NOT publish_barrier OR completed_at_utc IS NULL) AND (root_started_at_utc IS NULL OR publish_barrier OR completed_at_utc IS NOT NULL) AND (state <> 'Publishing' OR (publish_barrier AND root_started_at_utc IS NOT NULL))");
                table.CheckConstraint("ck_container_upload_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND target_proof ~ '^[A-F0-9]{64}$' AND expected_parent_version ~ '^[A-F0-9]{64}$' AND plan_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND (expected_version ~ '^[A-F0-9]{64}$' OR (expected_version='ABSENT' AND previous_size_bytes=0)) AND (published_version IS NULL OR published_version ~ '^[A-F0-9]{64}$')");
                table.CheckConstraint("ck_container_upload_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(actor_subject) BETWEEN 1 AND 256 AND length(trace_id) BETWEEN 1 AND 128");
                table.CheckConstraint("ck_container_upload_lease", "(lease_token IS NULL) = (lease_expires_at_utc IS NULL) AND (state <> 'Receiving' OR receive_token IS NOT NULL)");
                table.CheckConstraint("ck_container_upload_limits", "size_bytes BETWEEN 0 AND 1073741824 AND previous_size_bytes BETWEEN 0 AND 1073741824 AND version >= 0 AND expires_at_utc = created_at_utc + INTERVAL '15 minutes' AND created_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00'");
                table.CheckConstraint("ck_container_upload_state", "state IN ('AwaitingUpload','Receiving','Ready','Publishing','Unknown','Succeeded','Superseded','Canceled','Failed')");
                table.CheckConstraint("ck_container_upload_terminal", "(completed_at_utc IS NULL) = (state IN ('AwaitingUpload','Receiving','Ready','Publishing','Unknown'))");
                table.ForeignKey(
                    name: "FK_container_file_upload_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_container_file_upload_operations_actor_subject_idempotency_~",
            table: "container_file_upload_operations",
            columns: ActorColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_container_file_upload_operations_container_id_completed_at_~",
            table: "container_file_upload_operations",
            columns: TargetColumns);

        migrationBuilder.CreateIndex(
            name: "IX_container_file_upload_operations_next_attempt_at_utc_id",
            table: "container_file_upload_operations",
            columns: DueColumns,
            filter: "completed_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_container_file_upload_operations_publish_barrier",
            table: "container_file_upload_operations",
            column: "publish_barrier",
            unique: true,
            filter: "publish_barrier");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "container_file_upload_operations");
    }
}
