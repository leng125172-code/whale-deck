using Microsoft.EntityFrameworkCore;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>Fixed-schema UTC daily metric partitions; no caller-supplied identifiers or DDL.</summary>
public static class MetricPartitions
{
    public static string MigrationUp => ReadResource("Up");
    public static string MigrationDown => ReadResource("Down");

    public static Task<int> MaintainAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<int>($"SELECT public.wd_maintain_metric_partitions({now.ToUniversalTime()}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    private static string ReadResource(string direction)
    {
        using var stream = typeof(MetricPartitions).Assembly.GetManifestResourceStream($"WhaleDeck.Infrastructure.Persistence.Sql.MetricPartitions.{direction}.sql")
            ?? throw new InvalidOperationException("The metric partition migration resource is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
