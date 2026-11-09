using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DevelopmentWorkspaceStorageAllocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_shape",
            table: "development_workspaces");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "storage_allocated_at_utc",
            table: "development_workspaces",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "storage_allocation_started_at_utc",
            table: "development_workspaces",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "storage_plan_sha256",
            table: "development_workspaces",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_shape",
            table: "development_workspaces",
            sql: "cpu_millis >= 100 AND memory_mi_b >= 128 AND disk_mi_b >= 256");

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_storage_state",
            table: "development_workspaces",
            sql: "(CASE state\n    WHEN 'Registered' THEN version = 1 AND storage_plan_sha256 IS NULL\n        AND storage_allocation_started_at_utc IS NULL AND storage_allocated_at_utc IS NULL\n    WHEN 'StorageAllocating' THEN version = 2 AND storage_plan_sha256 ~ '^[a-f0-9]{64}$'\n        AND storage_allocation_started_at_utc >= created_at_utc AND storage_allocated_at_utc IS NULL\n    WHEN 'StorageAllocated' THEN version = 3 AND storage_plan_sha256 ~ '^[a-f0-9]{64}$'\n        AND storage_allocation_started_at_utc >= created_at_utc\n        AND storage_allocated_at_utc >= storage_allocation_started_at_utc\n    ELSE FALSE END) IS TRUE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE development_workspaces IN ACCESS EXCLUSIVE MODE;
            DO $f$ BEGIN
                IF EXISTS (SELECT 1 FROM development_workspaces WHERE state <> 'Registered') THEN
                    RAISE EXCEPTION 'development_workspace_storage_history_present' USING ERRCODE='23514';
                END IF;
            END $f$;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_shape",
            table: "development_workspaces");

        migrationBuilder.DropCheckConstraint(
            name: "ck_development_workspace_storage_state",
            table: "development_workspaces");

        migrationBuilder.DropColumn(
            name: "storage_allocated_at_utc",
            table: "development_workspaces");

        migrationBuilder.DropColumn(
            name: "storage_allocation_started_at_utc",
            table: "development_workspaces");

        migrationBuilder.DropColumn(
            name: "storage_plan_sha256",
            table: "development_workspaces");

        migrationBuilder.AddCheckConstraint(
            name: "ck_development_workspace_shape",
            table: "development_workspaces",
            sql: "state = 'Registered' AND version = 1 AND cpu_millis >= 100 AND memory_mi_b >= 128 AND disk_mi_b >= 256");
    }
}
