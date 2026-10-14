using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationPrivateWorkflow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "private_configuration_blocked",
            table: "notification_channels",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "notification_private_operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                registry_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                channel_version = table.Column<long>(type: "bigint", nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                root_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                input_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_private_operations", x => x.id);
                table.CheckConstraint("ck_private_action", "action IN ('apply','remove') AND channel_version >= 0 AND version >= 0");
                table.CheckConstraint("ck_private_fingerprints", "request_hash ~ '^[A-F0-9]{64}$' AND input_fingerprint ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_private_state", "state IN ('Pending','Running','Unknown','Succeeded','Superseded','Canceled','Failed')");
                table.ForeignKey(
                    name: "FK_notification_private_operations_notification_channels_chann~",
                    column: x => x.channel_id,
                    principalTable: "notification_channels",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_notification_private_operations_operation_jobs_id",
                    column: x => x.id,
                    principalTable: "operation_jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_private_operations_channel_id",
            table: "notification_private_operations",
            column: "channel_id",
            unique: true,
            filter: "state IN ('Pending','Running','Unknown')");

        migrationBuilder.CreateIndex(
            name: "IX_notification_private_operations_lease_token",
            table: "notification_private_operations",
            column: "lease_token",
            unique: true,
            filter: "lease_token IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM notification_private_operations) OR EXISTS (SELECT 1 FROM notification_channels WHERE private_configuration_blocked) THEN
                    RAISE EXCEPTION 'Private configuration history/blocked state must be preserved; downgrade refused' USING ERRCODE='55000';
                  END IF;
                END $$;
                """);
        migrationBuilder.DropTable(
            name: "notification_private_operations");

        migrationBuilder.DropColumn(
            name: "private_configuration_blocked",
            table: "notification_channels");
    }
}
