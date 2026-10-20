using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreePurgingAdministratorRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations",
            sql: "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack','tree-trash','tree-restore','tree-overwrite-restore','tree-purge') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
              IF EXISTS (SELECT 1 FROM file_recovery_operations WHERE kind='tree-purge') THEN
                RAISE EXCEPTION 'tree_purging_recovery_downgrade_requires_empty_kind';
              END IF;
            END $guard$;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations",
            sql: "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack','tree-trash','tree-restore','tree-overwrite-restore') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
    }
}
