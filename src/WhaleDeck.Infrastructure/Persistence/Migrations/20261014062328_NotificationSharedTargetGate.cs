using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationSharedTargetGate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_target_rate_gates",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                next_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_target_rate_gates", x => x.id);
                table.CheckConstraint("ck_notification_target_identity", "id ~ '^[A-F0-9]{64}$'");
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_target_rate_gates_next_at_utc",
            table: "notification_target_rate_gates",
            column: "next_at_utc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_target_rate_gates");
    }
}
