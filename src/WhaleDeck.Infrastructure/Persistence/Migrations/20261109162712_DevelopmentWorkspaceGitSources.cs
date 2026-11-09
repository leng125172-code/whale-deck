using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceGitSources : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_archive",
            table: "development_workspace_source_imports");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_state",
            table: "development_workspace_source_imports");

        migrationBuilder.AddColumn<string>(
            name: "git_commit",
            table: "development_workspace_source_imports",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "git_credential_ref",
            table: "development_workspace_source_imports",
            type: "character varying(48)",
            maxLength: 48,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "git_file_count",
            table: "development_workspace_source_imports",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "git_ref",
            table: "development_workspace_source_imports",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "git_repository_id",
            table: "development_workspace_source_imports",
            type: "character varying(48)",
            maxLength: 48,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "git_resolved_object",
            table: "development_workspace_source_imports",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "git_total_bytes",
            table: "development_workspace_source_imports",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_archive",
            table: "development_workspace_source_imports",
            sql: "received_bytes BETWEEN 0 AND archive_bytes AND (\n    (format IN ('zip','tar','tar.gz') AND archive_bytes BETWEEN 1 AND 134217728 AND archive_sha256 ~ '^[a-f0-9]{64}$')\n    OR (git_repository_id IS NOT NULL AND format = 'tar' AND archive_bytes = 0 AND archive_sha256 = ''\n        AND state IN ('Acquiring','Canceled','Failed'))\n)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_git",
            table: "development_workspace_source_imports",
            sql: "(git_repository_id IS NULL AND git_ref IS NULL AND git_credential_ref IS NULL AND git_resolved_object IS NULL\n    AND git_commit IS NULL AND git_file_count IS NULL AND git_total_bytes IS NULL AND state <> 'Acquiring')\nOR (git_repository_id IS NOT NULL AND git_repository_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$'\n    AND git_ref IS NOT NULL AND char_length(git_ref) BETWEEN 1 AND 255\n    AND (git_credential_ref IS NULL OR git_credential_ref ~ '^[a-z0-9]+(-[a-z0-9]+)*$')\n    AND state <> 'Uploading' AND format = 'tar' AND (\n        (archive_bytes = 0 AND git_resolved_object IS NULL AND git_commit IS NULL\n            AND git_file_count IS NULL AND git_total_bytes IS NULL)\n        OR (archive_bytes > 0 AND git_resolved_object IS NOT NULL AND git_commit IS NOT NULL\n            AND git_resolved_object ~ '^[a-f0-9]{40}([a-f0-9]{24})?$' AND git_resolved_object ~ '[1-9a-f]'\n            AND git_commit ~ '^[a-f0-9]{40}([a-f0-9]{24})?$' AND git_commit ~ '[1-9a-f]'\n            AND length(git_resolved_object) = length(git_commit)\n            AND git_file_count IS NOT NULL AND git_file_count BETWEEN 1 AND 9999\n            AND git_total_bytes IS NOT NULL AND git_total_bytes BETWEEN 0 AND archive_bytes)\n    ))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_state",
            table: "development_workspace_source_imports",
            sql: "state IN ('Uploading','Acquiring','Ready','Staged','Publishing','Canceling','Published','Canceled','Failed')\nAND (state IN ('Published','Canceled','Failed')) = (finished_at_utc IS NOT NULL)\nAND (lease IS NULL) = (lease_until_utc IS NULL)\nAND (finished_at_utc IS NULL OR lease IS NULL)\nAND (state NOT IN ('Ready','Staged','Publishing','Published') OR received_bytes = archive_bytes)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$
            BEGIN
                IF EXISTS (SELECT 1 FROM development_workspace_source_imports WHERE git_repository_id IS NOT NULL) THEN
                    RAISE EXCEPTION 'workspace_source_git_history_present';
                END IF;
            END
            $guard$;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_archive",
            table: "development_workspace_source_imports");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_git",
            table: "development_workspace_source_imports");

        migrationBuilder.DropCheckConstraint(
            name: "ck_workspace_source_state",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_commit",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_credential_ref",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_file_count",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_ref",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_repository_id",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_resolved_object",
            table: "development_workspace_source_imports");

        migrationBuilder.DropColumn(
            name: "git_total_bytes",
            table: "development_workspace_source_imports");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_archive",
            table: "development_workspace_source_imports",
            sql: "format IN ('zip','tar','tar.gz') AND archive_bytes BETWEEN 1 AND 134217728 AND archive_sha256 ~ '^[a-f0-9]{64}$' AND received_bytes BETWEEN 0 AND archive_bytes");

        migrationBuilder.AddCheckConstraint(
            name: "ck_workspace_source_state",
            table: "development_workspace_source_imports",
            sql: "state IN ('Uploading','Ready','Staged','Publishing','Canceling','Published','Canceled','Failed')\nAND (state IN ('Published','Canceled','Failed')) = (finished_at_utc IS NOT NULL)\nAND (lease IS NULL) = (lease_until_utc IS NULL)\nAND (finished_at_utc IS NULL OR lease IS NULL)\nAND (state NOT IN ('Ready','Staged','Publishing','Published') OR received_bytes = archive_bytes)");
    }
}
