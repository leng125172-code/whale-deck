using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreeOverwriteAdministratorRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_file_recovery_kind", "file_recovery_operations");
        migrationBuilder.AlterColumn<string>(name: "kind", table: "file_recovery_operations", type: "character varying(32)",
            maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "character varying(16)", oldMaxLength: 16);
        migrationBuilder.AddCheckConstraint("ck_file_recovery_kind", "file_recovery_operations",
            "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack','tree-trash','tree-restore','tree-overwrite-restore') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
              IF EXISTS (SELECT 1 FROM file_recovery_operations WHERE kind='tree-overwrite-restore') THEN
                RAISE EXCEPTION 'tree_overwrite_recovery_downgrade_requires_empty_kind';
              END IF;
            END $guard$;
            """);
        migrationBuilder.DropCheckConstraint("ck_file_recovery_kind", "file_recovery_operations");
        migrationBuilder.AlterColumn<string>(name: "kind", table: "file_recovery_operations", type: "character varying(16)",
            maxLength: 16, nullable: false, oldClrType: typeof(string), oldType: "character varying(32)", oldMaxLength: 32);
        migrationBuilder.AddCheckConstraint("ck_file_recovery_kind", "file_recovery_operations",
            "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack','tree-trash','tree-restore') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
    }
}
