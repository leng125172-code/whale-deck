using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MonitoringAlertLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "version",
            table: "alert_events",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_alert_events_fingerprint",
            table: "alert_events",
            column: "fingerprint",
            unique: true,
            filter: "state <> 'Recovered'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_alert_events_fingerprint",
            table: "alert_events");

        migrationBuilder.DropColumn(
            name: "version",
            table: "alert_events");
    }
}
