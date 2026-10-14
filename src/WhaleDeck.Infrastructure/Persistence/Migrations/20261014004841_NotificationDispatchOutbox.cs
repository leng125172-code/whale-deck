using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationDispatchOutbox : Migration
{
    private static readonly string[] EventChannelIndex = ["notification_id", "channel_id"];
    private static readonly string[] DueStateIndex = ["state", "next_attempt_at_utc"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_channels",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                destination_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                credential_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_channels", x => x.id);
                table.CheckConstraint("ck_notification_channel_provider", "provider IN ('Email','Webhook','DingTalk','WeCom','Feishu')");
                table.CheckConstraint("ck_notification_channel_refs", "destination_reference ~ '^[a-z][a-z0-9-]{0,63}$' AND (credential_reference IS NULL OR credential_reference ~ '^[a-z][a-z0-9-]{0,63}$')");
                table.CheckConstraint("ck_notification_channel_version", "version >= 0");
            });

        migrationBuilder.CreateTable(
            name: "notification_dispatch_receipts",
            columns: table => new
            {
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                routed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                delivery_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_dispatch_receipts", x => x.notification_id);
                table.CheckConstraint("ck_notification_receipt", "outcome IN ('Matched','NoRoute','Personal') AND delivery_count >= 0 AND delivery_count <= 32");
                table.ForeignKey(
                    name: "FK_notification_dispatch_receipts_notification_events_notifica~",
                    column: x => x.notification_id,
                    principalTable: "notification_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "notification_deliveries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                channel_version = table.Column<long>(type: "bigint", nullable: false),
                provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                destination_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                credential_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_result_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_deliveries", x => x.id);
                table.CheckConstraint("ck_notification_delivery_attempts", "attempt_count >= 0 AND attempt_count <= 5 AND channel_version >= 0");
                table.CheckConstraint("ck_notification_delivery_lease", "(state IN ('Leased','Sending') AND lease_token IS NOT NULL AND lease_expires_at_utc IS NOT NULL) OR (state NOT IN ('Leased','Sending') AND lease_token IS NULL AND lease_expires_at_utc IS NULL)");
                table.CheckConstraint("ck_notification_delivery_state", "state IN ('Pending','Leased','Sending','RetryWait','Succeeded','Failed','Unknown','Canceled')");
                table.ForeignKey(
                    name: "FK_notification_deliveries_notification_channels_channel_id",
                    column: x => x.channel_id,
                    principalTable: "notification_channels",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_notification_deliveries_notification_events_notification_id",
                    column: x => x.notification_id,
                    principalTable: "notification_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "notification_routes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                minimum_severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                include_recovery = table.Column<bool>(type: "boolean", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_routes", x => x.id);
                table.CheckConstraint("ck_notification_route_kind", "source_kind IN ('all','Alert','Job')");
                table.CheckConstraint("ck_notification_route_severity", "minimum_severity IN ('Info','Warning','Error','Critical')");
                table.CheckConstraint("ck_notification_route_version", "version >= 0");
                table.ForeignKey(
                    name: "FK_notification_routes_notification_channels_channel_id",
                    column: x => x.channel_id,
                    principalTable: "notification_channels",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_channel_id",
            table: "notification_deliveries",
            column: "channel_id");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_lease_expires_at_utc",
            table: "notification_deliveries",
            column: "lease_expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_notification_id_channel_id",
            table: "notification_deliveries",
            columns: EventChannelIndex,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_state_next_attempt_at_utc",
            table: "notification_deliveries",
            columns: DueStateIndex);

        migrationBuilder.CreateIndex(
            name: "IX_notification_routes_channel_id",
            table: "notification_routes",
            column: "channel_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_deliveries");

        migrationBuilder.DropTable(
            name: "notification_dispatch_receipts");

        migrationBuilder.DropTable(
            name: "notification_routes");

        migrationBuilder.DropTable(
            name: "notification_channels");
    }
}
