using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.Worker;

/// <summary>Host ingestion does not wait for discovery, jobs, backup, catalog or rollup work.</summary>
public sealed partial class HostMetricIngestionWorker(IServiceScopeFactory scopeFactory, ILogger<HostMetricIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset? acknowledged = null;
        var nextPartitionMaintenance = DateTimeOffset.MinValue;
        var nextFailureLog = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var agent = scope.ServiceProvider.GetRequiredService<IAgentGateway>();
                var health = await agent.GetHealthAsync(stoppingToken);
                if (!health.Available) continue;
                var samples = await agent.GetRecentMetricsAsync(acknowledged, stoppingToken);
                var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
                var now = DateTimeOffset.UtcNow;
                if (nextPartitionMaintenance <= now)
                {
                    if (await MetricPartitions.MaintainAsync(db, now, stoppingToken) < 0) continue;
                    nextPartitionMaintenance = now.AddHours(1);
                }
                // A failed save/commit leaves this cursor unchanged. Restart requests the bounded
                // buffer again; PostgreSQL canonical identities make that replay idempotent.
                acknowledged = await PersistAsync(db, samples, acknowledged, health.ObservedAtUtc, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextFailureLog)
                {
                    LogIngestionFailure(logger, exception);
                    nextFailureLog = DateTimeOffset.UtcNow.AddMinutes(1);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task<DateTimeOffset?> PersistAsync(PlatformDbContext db, IReadOnlyCollection<MetricValueDto> samples,
        DateTimeOffset? acknowledged, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        if (samples.Count == 0) return acknowledged;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // This dedicated context is not shared with any other Worker job.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(82592831620706) AS \"Value\"").SingleAsync(cancellationToken))
                return acknowledged;
            var resource = await db.ManagedResources.SingleOrDefaultAsync(item => item.ResourceType == "Host" && item.ExternalId == "local", cancellationToken);
            if (resource is null)
            {
                resource = new ManagedResource { ResourceType = "Host", ExternalId = "local", DisplayName = "Local workstation",
                    ProtectionLevel = "ControlPlane", Source = "Agent" };
                db.ManagedResources.Add(resource);
            }
            await MetricSampleWriter.StageAsync(db, resource.Id, samples, cancellationToken);
            resource.LastSeenAtUtc = observedAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var latest = samples.Max(sample => sample.SampledAtUtc);
            return acknowledged is null || latest > acknowledged.Value ? latest : acknowledged;
        });
    }

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Host metric ingestion failed; retained canonical samples will be retried")]
    private static partial void LogIngestionFailure(ILogger logger, Exception exception);
}
