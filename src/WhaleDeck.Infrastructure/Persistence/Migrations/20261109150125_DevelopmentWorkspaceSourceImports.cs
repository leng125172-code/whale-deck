using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceSourceImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "development_workspace_source_imports",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                workspace_json = table.Column<string>(type: "jsonb", nullable: false),
                format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                archive_bytes = table.Column<int>(type: "integer", nullable: false),
                archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                received_bytes = table.Column<int>(type: "integer", nullable: false),
                plan_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                content_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                file_count = table.Column<int>(type: "integer", nullable: true),
                total_bytes = table.Column<long>(type: "bigint", nullable: true),
                code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                lease = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_source_imports", x => x.id);
                table.CheckConstraint("ck_workspace_source_archive", "format IN ('zip','tar','tar.gz') AND archive_bytes BETWEEN 1 AND 134217728 AND archive_sha256 ~ '^[a-f0-9]{64}$' AND received_bytes BETWEEN 0 AND archive_bytes");
                table.CheckConstraint("ck_workspace_source_plan", "(plan_sha256 IS NULL AND content_sha256 IS NULL AND file_count IS NULL AND total_bytes IS NULL\n    AND state NOT IN ('Staged','Publishing','Published'))\nOR (plan_sha256 IS NOT NULL AND plan_sha256 ~ '^[a-f0-9]{64}$'\n    AND content_sha256 IS NOT NULL AND content_sha256 ~ '^[a-f0-9]{64}$'\n    AND file_count IS NOT NULL AND file_count BETWEEN 1 AND 9999\n    AND total_bytes IS NOT NULL AND total_bytes BETWEEN 0 AND 1073741824)");
                table.CheckConstraint("ck_workspace_source_state", "state IN ('Uploading','Ready','Staged','Publishing','Canceling','Published','Canceled','Failed')\nAND (state IN ('Published','Canceled','Failed')) = (finished_at_utc IS NOT NULL)\nAND (lease IS NULL) = (lease_until_utc IS NULL)\nAND (finished_at_utc IS NULL OR lease IS NULL)\nAND (state NOT IN ('Ready','Staged','Publishing','Published') OR received_bytes = archive_bytes)");
                table.ForeignKey(
                    name: "FK_development_workspace_source_imports_development_workspaces~",
                    column: x => x.workspace_id,
                    principalTable: "development_workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "development_workspace_source_chunks",
            columns: table => new
            {
                import_id = table.Column<Guid>(type: "uuid", nullable: false),
                offset = table.Column<int>(type: "integer", nullable: false),
                sealed_content = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspace_source_chunks", x => new { x.import_id, x.offset });
                table.CheckConstraint("ck_workspace_source_chunk", "\"offset\" BETWEEN 0 AND 133169152 AND \"offset\" % 1048576 = 0 AND octet_length(sealed_content) BETWEEN 30 AND 1048605");
                table.ForeignKey(
                    name: "FK_development_workspace_source_chunks_development_workspace_s~",
                    column: x => x.import_id,
                    principalTable: "development_workspace_source_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_development_workspace_source_imports_available_at_utc_id",
            table: "development_workspace_source_imports",
            columns: ["available_at_utc", "id"],
            filter: "finished_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_workspace_source_pending",
            table: "development_workspace_source_imports",
            column: "workspace_id",
            unique: true,
            filter: "finished_at_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_source_imports) THEN
                    RAISE EXCEPTION 'workspace_source_import_history_present';
                END IF;
            END $$;
            """);

        migrationBuilder.DropTable(
            name: "development_workspace_source_chunks");

        migrationBuilder.DropTable(
            name: "development_workspace_source_imports");
    }
}
