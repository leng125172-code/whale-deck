using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhaleDeck.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MetricTimeBrin : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
        migrationBuilder.CreateIndex(
            name: "IX_metric_samples_sampled_at_utc_brin",
            table: "metric_samples",
            column: "sampled_at_utc")
            .Annotation("Npgsql:IndexMethod", "brin");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropIndex(
        name: "IX_metric_samples_sampled_at_utc_brin",
        table: "metric_samples");
}
