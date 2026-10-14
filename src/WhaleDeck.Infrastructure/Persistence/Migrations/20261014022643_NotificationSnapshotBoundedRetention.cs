using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationSnapshotBoundedRetention : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_notification_read_snapshots_subject",
            table: "notification_read_snapshots");

        migrationBuilder.AddColumn<bool>(
            name: "is_current",
            table: "notification_read_snapshots",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshots_subject",
            table: "notification_read_snapshots",
            column: "subject",
            unique: true,
            filter: "is_current");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never delete history to satisfy the old full unique index during downgrade.
        migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT subject FROM notification_read_snapshots GROUP BY subject HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Snapshot downgrade requires bounded cleanup of superseded intents' USING ERRCODE='55000';
                    END IF;
                END $$;
                """);
        migrationBuilder.DropIndex(
            name: "IX_notification_read_snapshots_subject",
            table: "notification_read_snapshots");

        migrationBuilder.DropColumn(
            name: "is_current",
            table: "notification_read_snapshots");

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshots_subject",
            table: "notification_read_snapshots",
            column: "subject",
            unique: true);
    }
}
