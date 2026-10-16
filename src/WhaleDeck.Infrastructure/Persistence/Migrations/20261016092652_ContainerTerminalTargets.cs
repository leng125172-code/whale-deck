using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class ContainerTerminalTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_terminal_target",
                table: "terminal_sessions");

            migrationBuilder.AddColumn<string>(
                name: "instance_id",
                table: "terminal_sessions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shell",
                table: "terminal_sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_kind",
                table: "terminal_sessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Host");

            migrationBuilder.AddCheckConstraint(
                name: "ck_terminal_target",
                table: "terminal_sessions",
                sql: "(target_kind = 'Host' AND target_id = 'whaledeck.host' AND instance_id IS NULL AND shell IS NULL) OR (target_kind = 'Container' AND target_id ~ '^[a-f0-9]{64}$' AND instance_id IS NOT NULL AND length(instance_id) BETWEEN 1 AND 80 AND shell IS NOT NULL AND shell IN ('sh','bash','ash'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_terminal_target",
                table: "terminal_sessions");

            migrationBuilder.DropColumn(
                name: "instance_id",
                table: "terminal_sessions");

            migrationBuilder.DropColumn(
                name: "shell",
                table: "terminal_sessions");

            migrationBuilder.DropColumn(
                name: "target_kind",
                table: "terminal_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_terminal_target",
                table: "terminal_sessions",
                sql: "target_id = 'whaledeck.host'");
        }
    }
