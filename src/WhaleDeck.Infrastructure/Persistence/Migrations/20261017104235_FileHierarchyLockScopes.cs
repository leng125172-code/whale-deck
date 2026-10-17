using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileHierarchyLockScopes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "relative_path",
            table: "file_path_locks",
            type: "character varying(4096)",
            maxLength: 4096,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "root_id",
            table: "file_path_locks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "scope_kind",
            table: "file_path_locks",
            type: "character varying(8)",
            maxLength: 8,
            nullable: false,
            defaultValue: "Legacy");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_path_scope",
            table: "file_path_locks",
            sql: "(scope_kind IN ('Legacy','Token') AND root_id IS NULL AND relative_path IS NULL) OR (scope_kind='Path' AND root_id IS NOT NULL AND relative_path IS NOT NULL AND root_id ~ '^[a-z0-9.-]{1,64}$' AND octet_length(relative_path) BETWEEN 1 AND 4096)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_file_path_scope",
            table: "file_path_locks");

        migrationBuilder.DropColumn(
            name: "relative_path",
            table: "file_path_locks");

        migrationBuilder.DropColumn(
            name: "root_id",
            table: "file_path_locks");

        migrationBuilder.DropColumn(
            name: "scope_kind",
            table: "file_path_locks");
    }
}
