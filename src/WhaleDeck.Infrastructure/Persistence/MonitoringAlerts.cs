using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

/// <summary>One grouped query per metric rule, with explicit unknown observations and durable lifecycle.</summary>
public static class MonitoringAlerts
{
    public static Task EvaluateAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => EvaluatePassAsync(db, now, cancellationToken));

    private static async Task EvaluatePassAsync(PlatformDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(82592831620706) AS \"Value\"").SingleAsync(cancellationToken)) return;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='2s'; SET LOCAL statement_timeout='15s';", cancellationToken);
        var rules = await db.AlertRules.AsNoTracking().Where(item => item.RuleType != BackupCapacityPolicy.RuleType).ToArrayAsync(cancellationToken);
        var resources = await db.ManagedResources.AsNoTracking().ToArrayAsync(cancellationToken);
        var ruleIds = rules.Select(item => item.Id).ToArray();
        var active = await db.AlertEvents.Where(item => ruleIds.Contains(item.RuleId) && item.State != "Recovered").ToArrayAsync(cancellationToken);
        foreach (var rule in rules)
        {
            var events = active.Where(item => item.RuleId == rule.Id).ToArray();
            MonitoringAlertPolicy config;
            try
            {
                config = MonitoringAlertPolicy.Parse(new SaveAlertRuleCommand(rule.Id, rule.RuleType, rule.ResourceSelectorJson,
                    rule.ThresholdJson, rule.EvaluationWindowSeconds, rule.Severity, rule.IsEnabled, rule.Version));
            }
            catch (Exception error) when (error is ArgumentException or JsonException)
            {
                // Legacy invalid rules cannot abort all telemetry/other rules or clear existing risks.
                foreach (var item in events) Apply(db, item, new(item.ResourceId, null, item.Severity, Reason: "RuleInvalid"), rule, now);
                continue;
            }
            var selected = resources.Where(config.Includes).ToArray();
            var observations = rule.IsEnabled ? await ObserveAsync(db, rule, config, selected, events, now, cancellationToken) : [];
            foreach (var observation in observations.Where(item => item.Triggered == true))
            {
                var existing = events.SingleOrDefault(item => item.ResourceId == observation.ResourceId);
                if (existing is not null) continue;
                existing = new AlertEvent
                {
                    RuleId = rule.Id, ResourceId = observation.ResourceId,
                    Fingerprint = MonitoringAlertPolicy.Fingerprint(rule.Id, observation.ResourceId),
                    Severity = observation.Severity, SummaryCode = $"ALERT_{rule.RuleType.ToUpperInvariant()}",
                    FirstOccurredAtUtc = now, LastOccurredAtUtc = now
                };
                _ = MonitoringAlertPolicy.Apply(existing, observation, rule.EvaluationWindowSeconds, now);
                db.AlertEvents.Add(existing); Record(db, existing, "Active", now);
            }
            foreach (var item in events)
            {
                var resource = resources.SingleOrDefault(value => value.Id == item.ResourceId);
                var observation = !rule.IsEnabled ? new(item.ResourceId, false, item.Severity, Reason: "RuleDisabled") :
                    item.SummaryCode != $"ALERT_{rule.RuleType.ToUpperInvariant()}" ? new(item.ResourceId, false, item.Severity, Reason: "RuleChanged") :
                    resource is not null && !config.Includes(resource) ? new(item.ResourceId, false, item.Severity, Reason: "ResourceExcluded") :
                    observations.SingleOrDefault(value => value.ResourceId == item.ResourceId) ?? new(item.ResourceId, null, item.Severity, Reason: "TelemetryUnknown");
                Apply(db, item, observation, rule, now);
            }
        }
        TouchChangedEvents(db);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static void TouchChangedEvents(PlatformDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        foreach (var entry in db.ChangeTracker.Entries<AlertEvent>().Where(item => item.State == EntityState.Modified))
            entry.Entity.Version++;
    }

    private static async Task<AlertObservation[]> ObserveAsync(PlatformDbContext db, AlertRule rule, MonitoringAlertPolicy config,
        ManagedResource[] selected, AlertEvent[] active, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-rule.EvaluationWindowSeconds);
        if (MonitoringAlertPolicy.IsMetricRule(rule.RuleType))
        {
            var kind = rule.RuleType switch { "CpuUtilization" => "cpu.utilization", "MemoryUtilization" => "memory.utilization", "LoadAverage" => "load.normalized", _ => "disk.utilization" };
            var resourceIds = selected.Select(item => item.Id).ToArray();
            var series = (await db.MetricSeries.AsNoTracking().Where(item => resourceIds.Contains(item.ResourceId) && item.MetricKind == kind)
                .ToArrayAsync(cancellationToken)).Where(item => config.IncludesDevice(item.DimensionsJson)).ToArray();
            var ids = series.Select(item => item.Id).ToArray();
            // Parent-table time predicates prune daily partitions; invalid quality/NaN/infinity never contribute.
            var aggregates = await db.MetricSamples.AsNoTracking().Where(item => ids.Contains(item.SeriesId) && item.SampledAtUtc > cutoff && item.SampledAtUtc <= now)
                .GroupBy(item => item.SeriesId).Select(group => new
                {
                    Id = group.Key,
                    Average = group.Where(item => (item.Quality == "Good" || item.Quality == "Idle") && item.ValueDouble >= 0 && item.ValueDouble <= double.MaxValue).Average(item => item.ValueDouble),
                    Count = group.LongCount(item => (item.Quality == "Good" || item.Quality == "Idle") && item.ValueDouble >= 0 && item.ValueDouble <= double.MaxValue),
                    LatestObserved = group.Max(item => (DateTimeOffset?)item.SampledAtUtc),
                    LatestValid = group.Where(item => (item.Quality == "Good" || item.Quality == "Idle") && item.ValueDouble >= 0 && item.ValueDouble <= double.MaxValue).Max(item => (DateTimeOffset?)item.SampledAtUtc)
                }).ToDictionaryAsync(item => item.Id, cancellationToken);
            return series.GroupBy(item => item.ResourceId).Select(group =>
            {
                var values = group.Select(item => aggregates.TryGetValue(item.Id, out var aggregate)
                    ? config.Metric(item.ResourceId, aggregate.Average, aggregate.Count, aggregate.LatestValid, aggregate.LatestObserved,
                        rule.EvaluationWindowSeconds, now, rule.Severity, active.Any(alert => alert.ResourceId == item.ResourceId))
                    : new AlertObservation(item.ResourceId, null, rule.Severity, Reason: "TelemetryUnknown")).ToArray();
                // All devices must be healthy to recover; any risk suffices to trigger, unknown never hides risk.
                return values.Where(item => item.Triggered == true).OrderByDescending(item => item.Severity == "Critical").ThenByDescending(item => item.Value).FirstOrDefault() ??
                    values.FirstOrDefault(item => item.Triggered is null) ?? values.MaxBy(item => item.Value)!;
            }).ToArray();
        }
        if (rule.RuleType == "AgentOffline")
        {
            var host = selected.SingleOrDefault(item => item.ResourceType == "Host" && item.ExternalId == "local");
            return host is null ? [] : [new(host.Id, host.LastSeenAtUtc > now ? null : host.LastSeenAtUtc < cutoff, rule.Severity, ObservedAtUtc: host.LastSeenAtUtc,
                Reason: host.LastSeenAtUtc > now ? "TelemetryUnknown" : "Observed")];
        }
        if (rule.RuleType is "ContainerHealth" or "SystemdHealth")
            return selected.Where(item => item.ResourceType == (rule.RuleType == "ContainerHealth" ? "Container" : "SystemdUnit"))
                .Select(item => MonitoringAlertPolicy.ObserveState(rule.RuleType, item, now, rule.Severity)).ToArray();
        var failures = await db.BackupRecords.AsNoTracking().Where(item => item.Status == "Failed" && item.CompletedAtUtc > cutoff && item.CompletedAtUtc <= now)
            .Select(item => item.InstanceResourceId).Distinct().ToArrayAsync(cancellationToken);
        var cleanup = await db.OperationJobs.AsNoTracking().Where(item => item.JobType == "applications.cleanup-backups" && item.State == "Failed" &&
            item.CompletedAtUtc > cutoff && item.CompletedAtUtc <= now && item.ResourceId != null).Select(item => item.ResourceId!.Value).Distinct().ToArrayAsync(cancellationToken);
        var failed = failures.Concat(cleanup).ToHashSet();
        return selected.Select(item => new AlertObservation(item.Id, failed.Contains(item.Id), rule.Severity)).ToArray();
    }

    private static void Apply(PlatformDbContext db, AlertEvent item, AlertObservation observation, AlertRule rule, DateTimeOffset now)
    {
        foreach (var state in MonitoringAlertPolicy.Apply(item, observation, rule.EvaluationWindowSeconds, now)) Record(db, item, state, now);
    }
    private static void Record(PlatformDbContext db, AlertEvent item, string state, DateTimeOffset now) => db.AlertEventHistory.Add(
        new AlertEventHistory { AlertEventId = item.Id, State = state, ActorSubject = "system:alert-evaluator", OccurredAtUtc = now });
}
