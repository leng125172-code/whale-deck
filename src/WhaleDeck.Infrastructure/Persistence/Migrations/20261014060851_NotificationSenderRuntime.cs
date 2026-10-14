using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationSenderRuntime : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "configuration_revision",
            table: "notification_deliveries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_delivery_at_utc",
            table: "notification_channels",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "notification_delivery_attempts",
            columns: table => new
            {
                delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                attempt_number = table.Column<int>(type: "integer", nullable: false),
                started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_delivery_attempts", x => new { x.delivery_id, x.attempt_number });
                table.CheckConstraint("ck_notification_attempt_number", "attempt_number BETWEEN 1 AND 5");
                table.CheckConstraint("ck_notification_attempt_result", "(finished_at_utc IS NULL AND result_code IS NULL) OR (finished_at_utc IS NOT NULL AND result_code IS NOT NULL AND finished_at_utc >= started_at_utc AND result_code IN ('Accepted','RetryableNotSent','Rejected','Unknown','LeaseExpiredAfterSend'))");
                table.ForeignKey(
                    name: "FK_notification_delivery_attempts_notification_deliveries_deli~",
                    column: x => x.delivery_id,
                    principalTable: "notification_deliveries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_lease_token",
            table: "notification_deliveries",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_delivery_configuration",
            table: "notification_deliveries",
            sql: "configuration_revision IS NULL OR configuration_revision ~ '^[A-F0-9]{64}$'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_delivery_attempts");

        migrationBuilder.DropIndex(
            name: "IX_notification_deliveries_lease_token",
            table: "notification_deliveries");

        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_delivery_configuration",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "configuration_revision",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "next_delivery_at_utc",
            table: "notification_channels");
    }
}
