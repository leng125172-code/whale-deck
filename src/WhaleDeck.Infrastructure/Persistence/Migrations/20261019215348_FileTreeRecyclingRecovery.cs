using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileTreeRecyclingRecovery : Migration
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
            sql: "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack','tree-trash') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
        migrationBuilder.Sql("CREATE INDEX ix_file_tree_captures_root_created_id ON file_tree_captures ((source_json->'Source'->>'RootId'),created_at_utc,id);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                DO $guard$ BEGIN
                  IF EXISTS (SELECT 1 FROM file_recovery_operations WHERE kind='tree-trash') THEN RAISE EXCEPTION 'tree_recycling_recovery_downgrade_requires_empty_kind'; END IF;
                END $guard$;
                DROP INDEX ix_file_tree_captures_root_created_id;
                """);
        migrationBuilder.DropCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations");

        migrationBuilder.AddCheckConstraint(
            name: "ck_file_recovery_kind",
            table: "file_recovery_operations",
            sql: "kind IN ('upload','mutation','archive','tree-copy','tree-move','tree-pack','tree-unpack') AND disposition IN ('AcknowledgeUnknown','ReconcileCompleted') AND reason_code IN ('OriginalSessionLost','OperatorHandover','IncidentReview')");
    }
}
