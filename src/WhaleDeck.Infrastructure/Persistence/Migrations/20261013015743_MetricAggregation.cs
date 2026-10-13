using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MetricAggregation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<double>(
            name: "sum",
            table: "metric_rollups",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<double>(
            name: "minimum",
            table: "metric_rollups",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<double>(
            name: "maximum",
            table: "metric_rollups",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<double>(
            name: "average",
            table: "metric_rollups",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AddColumn<double>(
            name: "first",
            table: "metric_rollups",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "first_sampled_at_utc",
            table: "metric_rollups",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "idle_count",
            table: "metric_rollups",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<double>(
            name: "last",
            table: "metric_rollups",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_sampled_at_utc",
            table: "metric_rollups",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "observed_sample_count",
            table: "metric_rollups",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "unavailable_count",
            table: "metric_rollups",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "warmup_count",
            table: "metric_rollups",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<double>(
            name: "weight_sum",
            table: "metric_rollups",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "weighted_sum",
            table: "metric_rollups",
            type: "double precision",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "metric_aggregation_pending",
            columns: table => new
            {
                window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_metric_aggregation_pending", x => x.window_start_utc);
            });

        migrationBuilder.CreateTable(
            name: "metric_aggregation_state",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                next_hour_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_metric_aggregation_state", x => x.key);
            });

        migrationBuilder.CreateTable(
            name: "metric_aggregation_windows",
            columns: table => new
            {
                window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                completed_through_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_metric_aggregation_windows", x => x.window_start_utc);
            });
        migrationBuilder.Sql(global::WhaleDeck.Infrastructure.Persistence.MetricAggregation.MigrationUp);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(global::WhaleDeck.Infrastructure.Persistence.MetricAggregation.MigrationDown);
        // Derived-only data has no legacy representation; never invent zero values or alter raw samples.
        migrationBuilder.Sql("DELETE FROM metric_rollups WHERE resolution = '1h' OR minimum IS NULL OR maximum IS NULL OR average IS NULL OR sum IS NULL;");
        migrationBuilder.DropTable(
            name: "metric_aggregation_pending");

        migrationBuilder.DropTable(
            name: "metric_aggregation_state");

        migrationBuilder.DropTable(
            name: "metric_aggregation_windows");

        migrationBuilder.DropColumn(
            name: "first",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "first_sampled_at_utc",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "idle_count",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "last",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "last_sampled_at_utc",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "observed_sample_count",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "unavailable_count",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "warmup_count",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "weight_sum",
            table: "metric_rollups");

        migrationBuilder.DropColumn(
            name: "weighted_sum",
            table: "metric_rollups");

        migrationBuilder.AlterColumn<double>(
            name: "sum",
            table: "metric_rollups",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "minimum",
            table: "metric_rollups",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "maximum",
            table: "metric_rollups",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "average",
            table: "metric_rollups",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);
    }
}
