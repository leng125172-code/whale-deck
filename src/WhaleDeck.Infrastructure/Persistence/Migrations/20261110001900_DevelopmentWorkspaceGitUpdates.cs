using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceGitUpdates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "conflict_resolution",
            table: "development_workspace_source_imports",
            type: "character varying(24)",
            maxLength: 24,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "previous_import_id",
            table: "development_workspace_source_imports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "update_json",
            table: "development_workspace_source_imports",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "uq_workspace_source_first_publication",
            table: "development_workspace_source_imports",
            column: "workspace_id",
            unique: true,
            filter: "state = 'Published' AND previous_import_id IS NULL");

        migrationBuilder.CreateIndex(
            name: "uq_workspace_source_published_successor",
            table: "development_workspace_source_imports",
            column: "previous_import_id",
            unique: true,
            filter: "state = 'Published' AND previous_import_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_update",
            table: "development_workspace_source_imports",
            sql: "(previous_import_id IS NULL AND update_json IS NULL AND conflict_resolution IS NULL)\nOR (previous_import_id IS NOT NULL AND previous_import_id <> id AND git_repository_id IS NOT NULL\n    AND ((plan_sha256 IS NULL AND update_json IS NULL)\n        OR (plan_sha256 IS NOT NULL AND update_json IS NOT NULL AND jsonb_typeof(update_json) = 'object'))\n    AND ((conflict_resolution IS NULL AND state NOT IN ('Publishing','Published'))\n        OR (conflict_resolution IS NOT NULL AND conflict_resolution IN ('clean-only','retain-and-replace')\n            AND plan_sha256 IS NOT NULL AND state IN ('Publishing','Published','Canceling','Canceled','Failed'))))");

        migrationBuilder.AddForeignKey(
            name: "FK_development_workspace_source_imports_development_workspace_~",
            table: "development_workspace_source_imports",
            column: "previous_import_id",
            principalTable: "development_workspace_source_imports",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_source_imports WHERE previous_import_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'workspace_source_update_history_present';
                END IF;
            END
            $guard$;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_development_workspace_source_imports_development_workspace_~",
            table: "development_workspace_source_imports");

        migrationBuilder.DropIndex(
            name: "uq_workspace_source_first_publication",
            table: "development_workspace_source_imports");

        migrationBuilder.DropIndex(
            name: "uq_workspace_source_published_successor",
            table: "development_workspace_source_imports");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_update",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "conflict_resolution",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "previous_import_id",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "update_json",
            table: "development_workspace_source_imports");
    }
}
