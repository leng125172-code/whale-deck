using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261109082000_ArchiveRetirementPreStartClosure")]
public sealed class ArchiveRetirementPreStartClosure : Migration
{
    private const string Table = "container_recycle_archive_retirement_intents";
    private const string Check = "ck_archive_retirement_result";

    protected override void Up(MigrationBuilder migrationBuilder) => Replace(migrationBuilder, "(root_started_at_utc IS NOT NULL AND root_state = 'Aborted') OR (root_started_at_utc IS NULL AND root_state IS NULL)");
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // EF can commit each downgrade step before a later migration refuses
        // to drop the history table. Protect its current constraints up front.
        migrationBuilder.Sql("""
            LOCK TABLE container_recycle_archive_retirement_intents IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
              IF EXISTS(SELECT 1 FROM container_recycle_archive_retirement_intents) THEN
                RAISE EXCEPTION 'archive_retirement_permanent_intent' USING ERRCODE='23514';
              END IF;
            END $f$;
            """);
        Replace(migrationBuilder, "root_state = 'Aborted'");
    }

    private static void Replace(MigrationBuilder migrationBuilder, string abortProof)
    {
        migrationBuilder.DropCheckConstraint(Check, Table);
        migrationBuilder.AddCheckConstraint(Check, Table, """
            ((root_state IS DISTINCT FROM 'Completed' OR apply_started_at_utc IS NOT NULL)
            AND (state <> 'Succeeded' OR root_state = 'Completed')
            AND (state <> 'Unknown' OR root_state IS NULL OR root_state = 'Unknown')
            AND (execution_job_id IS NULL OR state NOT IN ('Canceled','Failed','Expired') OR
            """ + " " + abortProof + """
            )
            AND (state IN ('Succeeded','Unknown','Canceled','Failed','Expired','Acknowledged') OR root_state IS NULL)) IS TRUE
            """);
    }
}
