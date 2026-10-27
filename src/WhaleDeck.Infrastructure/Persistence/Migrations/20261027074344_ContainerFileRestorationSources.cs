using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ContainerFileRestorationSources : Migration
{
    private static readonly string[] HistoryTimeColumns = ["container_id", "captured_at_unix_ms", "id"];
    private static readonly string[] HistoryIdentityColumns = ["container_id", "id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "history_source_json",
            table: "container_file_upload_operations",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "container_file_versions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                container_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                original_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                relative_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                source_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                capture_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                captured_at_unix_ms = table.Column<long>(type: "bigint", nullable: false),
                different_source_device = table.Column<bool>(type: "boolean", nullable: false),
                availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                last_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_container_file_versions", x => x.id);
                table.CheckConstraint("ck_container_history_availability", "availability IN ('Present','Incomplete','Missing','Invalid')");
                table.CheckConstraint("ck_container_history_hashes", "source_version ~ '^[A-F0-9]{64}$' AND sha256 ~ '^[A-F0-9]{64}$' AND capture_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_container_history_identity", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND container_id ~ '^[a-f0-9]{64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096 AND length(original_instance_id) BETWEEN 22 AND 64");
                table.CheckConstraint("ck_container_history_limits", "size_bytes BETWEEN 0 AND 1073741824 AND captured_at_unix_ms BETWEEN 0 AND 253402300799999 AND version>=0");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_container_upload_history_source",
            table: "container_file_upload_operations",
            sql: "history_source_json IS NULL OR octet_length(history_source_json) BETWEEN 1 AND 32768");

        migrationBuilder.CreateIndex(
            name: "IX_container_file_versions_container_id_captured_at_unix_ms_id",
            table: "container_file_versions",
            columns: HistoryTimeColumns);

        migrationBuilder.CreateIndex(
            name: "IX_container_file_versions_container_id_id",
            table: "container_file_versions",
            columns: HistoryIdentityColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "container_file_versions");

        migrationBuilder.DropCheckConstraint(
            name: "ck_container_upload_history_source",
            table: "container_file_upload_operations");

        migrationBuilder.DropColumn(
            name: "history_source_json",
            table: "container_file_upload_operations");
    }
}
