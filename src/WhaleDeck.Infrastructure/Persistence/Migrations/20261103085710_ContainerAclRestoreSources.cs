using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ContainerAclRestoreSources : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "restore_source_json",
            table: "container_acl_intents",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(ContainerAclRestoreSql.Create);
        migrationBuilder.AddCheckConstraint(
            name: "ck_container_acl_restore_source",
            table: "container_acl_intents",
            sql: "whaledeck_acl_restore_source_valid(restore_source_json,command_json,container_id,instance_id,relative_path) AND (restore_source_json IS NULL OR plan_json IS NULL OR restore_source_json::jsonb->'Entry'->>'Kind'=plan_json::jsonb->>'Kind')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ContainerAclRestoreSql.RequireSafeDown);
        migrationBuilder.DropCheckConstraint(
            name: "ck_container_acl_restore_source",
            table: "container_acl_intents");

        migrationBuilder.Sql(ContainerAclRestoreSql.DropValidation);
        migrationBuilder.DropColumn(
            name: "restore_source_json",
            table: "container_acl_intents");
    }
}
