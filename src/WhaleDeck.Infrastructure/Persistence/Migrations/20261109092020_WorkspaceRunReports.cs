using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class WorkspaceRunReports : Migration
{
    private static readonly string[] ExpiryIndexColumns = ["retain_until_utc", "run_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "workspace_run_reports",
            columns: table => new
            {
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                finalized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                retain_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                json_content = table.Column<byte[]>(type: "bytea", nullable: true),
                markdown_content = table.Column<byte[]>(type: "bytea", nullable: true),
                json_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                markdown_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workspace_run_reports", x => x.run_id);
                table.CheckConstraint("ck_workspace_report_content", "((expired_at_utc IS NULL AND json_content IS NOT NULL AND markdown_content IS NOT NULL\n  AND octet_length(json_content) BETWEEN 1 AND 524288 AND octet_length(markdown_content) BETWEEN 1 AND 524288\n  AND json_sha256 = encode(sha256(json_content),'hex') AND markdown_sha256 = encode(sha256(markdown_content),'hex'))\nOR (expired_at_utc >= retain_until_utc AND json_content IS NULL AND markdown_content IS NULL)) IS TRUE");
                table.CheckConstraint("ck_workspace_report_digests", "json_sha256 ~ '^[a-f0-9]{64}$' AND markdown_sha256 ~ '^[a-f0-9]{64}$'");
                table.CheckConstraint("ck_workspace_report_identity", "run_id <> '00000000-0000-0000-0000-000000000000'::uuid AND workspace_id <> '00000000-0000-0000-0000-000000000000'::uuid AND job_id <> '00000000-0000-0000-0000-000000000000'::uuid AND run_id <> job_id AND schema_version > 0");
                table.CheckConstraint("ck_workspace_report_lifetime", "retain_until_utc > finalized_at_utc AND retain_until_utc <= finalized_at_utc + INTERVAL '8760 hours'");
                table.ForeignKey(
                    name: "FK_workspace_run_reports_operation_jobs_job_id",
                    column: x => x.job_id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_workspace_run_reports_job_id",
            table: "workspace_run_reports",
            column: "job_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_workspace_run_reports_retain_until_utc_run_id",
            table: "workspace_run_reports",
            columns: ExpiryIndexColumns,
            filter: "expired_at_utc IS NULL");

        migrationBuilder.Sql("""
            CREATE FUNCTION whaledeck_workspace_report_expire_only() RETURNS trigger
            LANGUAGE plpgsql AS $f$
            BEGIN
                IF OLD.expired_at_utc IS NULL AND NEW.expired_at_utc >= OLD.retain_until_utc
                   AND NEW.json_content IS NULL AND NEW.markdown_content IS NULL
                   AND ROW(NEW.run_id, NEW.workspace_id, NEW.job_id, NEW.schema_version,
                           NEW.finalized_at_utc, NEW.retain_until_utc, NEW.json_sha256, NEW.markdown_sha256)
                       IS NOT DISTINCT FROM
                       ROW(OLD.run_id, OLD.workspace_id, OLD.job_id, OLD.schema_version,
                           OLD.finalized_at_utc, OLD.retain_until_utc, OLD.json_sha256, OLD.markdown_sha256)
                THEN
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'workspace_report_immutable' USING ERRCODE='23514';
            END
            $f$;
            CREATE TRIGGER workspace_report_expire_only BEFORE UPDATE ON workspace_run_reports
                FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_report_expire_only();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE workspace_run_reports IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
                IF EXISTS (SELECT 1 FROM workspace_run_reports) THEN
                    RAISE EXCEPTION 'workspace_report_history_present' USING ERRCODE='23514';
                END IF;
            END $f$;
            """);
        migrationBuilder.DropTable(
            name: "workspace_run_reports");
        migrationBuilder.Sql("DROP FUNCTION whaledeck_workspace_report_expire_only();");
    }
}
