using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class FileMutationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_mutation_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_file_mutation_operations", x => x.id);
                    table.CheckConstraint("ck_file_mutation_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND owner_hash ~ '^[A-F0-9]{64}$' AND request_hash ~ '^[A-F0-9]{64}$'");
                    table.CheckConstraint("ck_file_mutation_limits", "version >= 0 AND expires_at_utc > created_at_utc AND octet_length(plan_json::text) <= 32768");
                    table.CheckConstraint("ck_file_mutation_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed','Acknowledged')");
                    table.CheckConstraint("ck_file_mutation_terminal", "(completed_at_utc IS NULL) = (state IN ('Pending','Running','Unknown'))");
                    table.ForeignKey(
                        name: "FK_file_mutation_operations_operation_jobs_id",
                        column: x => x.id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_path_locks",
                columns: table => new
                {
                    path_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_path_locks", x => x.path_hash);
                    table.CheckConstraint("ck_file_path_hash", "path_hash ~ '^[A-F0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_file_path_locks_operation_jobs_operation_id",
                        column: x => x.operation_id,
                        principalTable: "operation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_mutation_operations_actor_subject_created_at_utc",
                table: "file_mutation_operations",
                columns: ["actor_subject", "created_at_utc"]);

            migrationBuilder.CreateIndex(
                name: "IX_file_mutation_operations_lease_token",
                table: "file_mutation_operations",
                column: "lease_token",
                unique: true,
                filter: "lease_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_mutation_operations_next_attempt_at_utc_id",
                table: "file_mutation_operations",
                columns: ["next_attempt_at_utc", "id"],
                filter: "completed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_file_path_locks_operation_id",
                table: "file_path_locks",
                column: "operation_id");

            // Adopt existing in-flight uploads, including Unknown. Migration
            // and both workflow repositories use the same transaction gate.
            migrationBuilder.Sql("""
                SELECT pg_advisory_xact_lock(41307341);
                INSERT INTO file_path_locks(path_hash,operation_id)
                  SELECT target_hash,id FROM file_upload_operations WHERE completed_at_utc IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_mutation_operations");

            migrationBuilder.DropTable(
                name: "file_path_locks");
        }
    }
