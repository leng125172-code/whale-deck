using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceRegistry : Migration
{
    private static readonly string[] OwnerRegistration = ["owner_subject", "registration_key"];
    private static readonly string[] OwnerSlug = ["owner_subject", "slug"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "development_workspaces",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false, collation: "C"),
                owner_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                storage_root_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                cpu_millis = table.Column<int>(type: "integer", nullable: false),
                memory_mi_b = table.Column<int>(type: "integer", nullable: false),
                disk_mi_b = table.Column<int>(type: "integer", nullable: false),
                environment_json = table.Column<string>(type: "text", nullable: false),
                environment_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                registration_key = table.Column<Guid>(type: "uuid", nullable: false),
                request_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_development_workspaces", x => x.id);
                table.CheckConstraint("ck_development_workspace_manifest", "octet_length(environment_json) BETWEEN 1 AND 4096 AND environment_sha256 = encode(sha256(convert_to(environment_json,'UTF8')),'hex') AND request_sha256 ~ '^[a-f0-9]{64}$'");
                table.CheckConstraint("ck_development_workspace_shape", "state = 'Registered' AND version = 1 AND cpu_millis >= 100 AND memory_mib >= 128 AND disk_mib >= 256");
            });

        migrationBuilder.CreateIndex(
            name: "IX_development_workspaces_owner_subject_registration_key",
            table: "development_workspaces",
            columns: OwnerRegistration,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspaces_owner_subject_slug",
            table: "development_workspaces",
            columns: OwnerSlug);

        migrationBuilder.CreateIndex(
            name: "IX_development_workspaces_slug",
            table: "development_workspaces",
            column: "slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE development_workspaces IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspaces) THEN
                    RAISE EXCEPTION 'development_workspace_history_present' USING ERRCODE='23514';
                END IF;
            END $f$;
            """);
        migrationBuilder.DropTable(
            name: "development_workspaces");
    }
}
