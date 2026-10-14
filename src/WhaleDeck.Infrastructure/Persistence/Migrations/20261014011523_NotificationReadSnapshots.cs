using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationReadSnapshots : Migration
{
    private static readonly string[] PendingSnapshotIndex = ["snapshot_id", "is_processed"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_read_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                is_administrator = table.Column<bool>(type: "boolean", nullable: false),
                item_count = table.Column<long>(type: "bigint", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_read_snapshots", x => x.id);
                table.CheckConstraint("ck_notification_read_snapshot_budget", "item_count >= 0");
            });

        migrationBuilder.CreateTable(
            name: "notification_read_snapshot_items",
            columns: table => new
            {
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_processed = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_read_snapshot_items", x => new { x.snapshot_id, x.notification_id });
                table.ForeignKey(
                    name: "FK_notification_read_snapshot_items_notification_events_notifi~",
                    column: x => x.notification_id,
                    principalTable: "notification_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_notification_read_snapshot_items_notification_read_snapshot~",
                    column: x => x.snapshot_id,
                    principalTable: "notification_read_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshot_items_notification_id",
            table: "notification_read_snapshot_items",
            column: "notification_id");

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshot_items_snapshot_id_is_processed",
            table: "notification_read_snapshot_items",
            columns: PendingSnapshotIndex);

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshots_expires_at_utc",
            table: "notification_read_snapshots",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_snapshots_subject",
            table: "notification_read_snapshots",
            column: "subject",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_read_snapshot_items");

        migrationBuilder.DropTable(
            name: "notification_read_snapshots");
    }
}
