using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class NotificationInbox : Migration
{
    private static readonly string[] AudienceRecipientSequenceColumns = ["audience", "recipient_subject", "sequence"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                source_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                audience = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                recipient_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                message_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_events", x => x.id);
                table.CheckConstraint("ck_notification_audience", "(audience = 'Administrator' AND recipient_subject IS NULL) OR (audience = 'Subject' AND recipient_subject IS NOT NULL AND length(recipient_subject) > 0)");
            });

        migrationBuilder.CreateTable(
            name: "notification_read_states",
            columns: table => new
            {
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                read_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_read_states", x => new { x.subject, x.notification_id });
                table.ForeignKey(
                    name: "FK_notification_read_states_notification_events_notification_id",
                    column: x => x.notification_id,
                    principalTable: "notification_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_events_audience_recipient_subject_sequence",
            table: "notification_events",
            columns: AudienceRecipientSequenceColumns);

        migrationBuilder.CreateIndex(
            name: "IX_notification_events_expires_at_utc",
            table: "notification_events",
            column: "expires_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_notification_events_sequence",
            table: "notification_events",
            column: "sequence",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_notification_events_source_key",
            table: "notification_events",
            column: "source_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_notification_read_states_notification_id",
            table: "notification_read_states",
            column: "notification_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_read_states");

        migrationBuilder.DropTable(
            name: "notification_events");
    }
}
