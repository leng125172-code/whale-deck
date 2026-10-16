using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class TerminalSessionLifecycle : Migration
{
    private static readonly string[] OwnerKeyColumns = ["actor_subject", "login_binding", "idempotency_key"];
    private static readonly string[] LeaseColumns = ["state", "lease_expires_at_utc"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "terminal_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                login_binding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                grant_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                grant_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                connected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                close_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                exit_code = table.Column<int>(type: "integer", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_terminal_sessions", x => x.id);
                table.CheckConstraint("ck_terminal_expiry", "grant_expires_at_utc > created_at_utc AND grant_expires_at_utc <= created_at_utc + INTERVAL '30 seconds' AND expires_at_utc > created_at_utc AND expires_at_utc <= created_at_utc + INTERVAL '10 minutes'");
                table.CheckConstraint("ck_terminal_hashes", "login_binding ~ '^[A-F0-9]{64}$' AND grant_hash ~ '^[A-F0-9]{64}$'");
                table.CheckConstraint("ck_terminal_state", "state IN ('Issued','Connecting','Active','Closed') AND version >= 0");
                table.CheckConstraint("ck_terminal_target", "target_id = 'whaledeck.host'");
            });

        migrationBuilder.CreateIndex(
            name: "IX_terminal_sessions_actor_subject_login_binding_idempotency_k~",
            table: "terminal_sessions",
            columns: OwnerKeyColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_terminal_sessions_created_at_utc",
            table: "terminal_sessions",
            column: "created_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_terminal_sessions_state_lease_expires_at_utc",
            table: "terminal_sessions",
            columns: LeaseColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "terminal_sessions");
    }
}
