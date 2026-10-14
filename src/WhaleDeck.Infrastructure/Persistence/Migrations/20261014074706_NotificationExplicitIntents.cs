using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationExplicitIntents : Migration
{
    private static readonly string[] EventChannelColumns = ["notification_id", "channel_id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_notification_deliveries_notification_id_channel_id",
            table: "notification_deliveries");

        migrationBuilder.AddColumn<string>(
            name: "intent_kind",
            table: "notification_deliveries",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Automatic");

        migrationBuilder.AddColumn<Guid>(
            name: "origin_delivery_id",
            table: "notification_deliveries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "requested_by_subject",
            table: "notification_deliveries",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "version",
            table: "notification_deliveries",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_notification_id_channel_id",
            table: "notification_deliveries",
            columns: EventChannelColumns,
            unique: true,
            filter: "intent_kind <> 'ManualRetry'");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_origin_delivery_id",
            table: "notification_deliveries",
            column: "origin_delivery_id",
            unique: true,
            filter: "origin_delivery_id IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_notification_delivery_intent",
            table: "notification_deliveries",
            sql: "version >= 0 AND ((intent_kind='Automatic' AND requested_by_subject IS NULL AND origin_delivery_id IS NULL) OR (intent_kind='Test' AND requested_by_subject IS NOT NULL AND length(requested_by_subject)>0 AND origin_delivery_id IS NULL) OR (intent_kind='ManualRetry' AND requested_by_subject IS NOT NULL AND length(requested_by_subject)>0 AND origin_delivery_id IS NOT NULL AND origin_delivery_id<>id))");

        migrationBuilder.AddForeignKey(
            name: "FK_notification_deliveries_notification_deliveries_origin_deli~",
            table: "notification_deliveries",
            column: "origin_delivery_id",
            principalTable: "notification_deliveries",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM notification_deliveries WHERE intent_kind <> 'Automatic') THEN
                RAISE EXCEPTION 'Explicit delivery history must be preserved; schema downgrade refused' USING ERRCODE='55000';
              END IF;
            END $$;
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_notification_deliveries_notification_deliveries_origin_deli~",
            table: "notification_deliveries");

        migrationBuilder.DropIndex(
            name: "IX_notification_deliveries_notification_id_channel_id",
            table: "notification_deliveries");

        migrationBuilder.DropIndex(
            name: "IX_notification_deliveries_origin_delivery_id",
            table: "notification_deliveries");

        migrationBuilder.DropCheckConstraint(
            name: "ck_notification_delivery_intent",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "intent_kind",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "origin_delivery_id",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "requested_by_subject",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "version",
            table: "notification_deliveries");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_notification_id_channel_id",
            table: "notification_deliveries",
            columns: EventChannelColumns,
            unique: true);
    }
}
