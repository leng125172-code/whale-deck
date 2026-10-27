using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ContainerRestorationAttributePolicy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "restore_attributes",
            table: "container_file_upload_operations",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddCheckConstraint(
            name: "ck_container_upload_restore_attributes",
            table: "container_file_upload_operations",
            sql: "NOT restore_attributes OR history_source_json IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_container_upload_restore_attributes",
            table: "container_file_upload_operations");

        migrationBuilder.DropColumn(
            name: "restore_attributes",
            table: "container_file_upload_operations");
    }
}
