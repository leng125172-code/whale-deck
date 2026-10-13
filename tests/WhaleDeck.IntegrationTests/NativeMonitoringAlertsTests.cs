using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Worker;

namespace WhaleDeck.IntegrationTests;

public sealed class NativeMonitoringAlertsTests
{
    [NativePostgresFact]
    public async Task RealPostgresSelectorQualityLifecycleAndConcurrentUpdates()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_alerts_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native test requires its isolated database namespace.");
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new PlatformDbContext(options);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var now = MetricSampleWriter.NormalizeTimestamp(DateTimeOffset.UtcNow);
        var selected = new ManagedResource { ResourceType = "Host", ExternalId = "local", DisplayName = "isolated host", ProtectionLevel = "Managed", Source = "Test", LastSeenAtUtc = now };
        var excluded = new ManagedResource { ResourceType = "Host", ExternalId = "excluded", DisplayName = "isolated other", ProtectionLevel = "Managed", Source = "Test", LastSeenAtUtc = now };
        var chosen = new MetricSeries { ResourceId = selected.Id, MetricKind = "disk.utilization", Unit = "%", DimensionsJson = "{\"DeviceId\":\"nvme0n1\"}" };
        var other = new MetricSeries { ResourceId = excluded.Id, MetricKind = "disk.utilization", Unit = "%" };
        var rule = new AlertRule { RuleType = "DiskUtilization", ResourceSelectorJson = "{\"resourceIds\":[\"local\"]}",
            ThresholdJson = "{\"value\":85,\"criticalValue\":95,\"recoveryValue\":80}", EvaluationWindowSeconds = 12 };
        db.ManagedResources.AddRange(selected, excluded); db.MetricSeries.AddRange(chosen, other); db.AlertRules.Add(rule);
        foreach (var series in new[] { chosen, other })
            foreach (var age in new[] { 9, 3 }) db.MetricSamples.Add(new MetricSample { SeriesId = series.Id, SampledAtUtc = now.AddSeconds(-age), ValueDouble = series.Id == chosen.Id ? 90 : 99 });
        await db.SaveChangesAsync();
        async Task Evaluate()
        {
            db.ChangeTracker.Clear(); await MonitoringAlerts.EvaluateAsync(db, now, default);
        }
        async Task<AlertEvent> Active() => await db.AlertEvents.AsNoTracking().SingleAsync(item => item.State != "Recovered");
        async Task Values(double? value, string quality = "Good") => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metric_samples SET value_double={value}, quality={quality} WHERE series_id={chosen.Id}");
        await Evaluate(); var alarm = await Active();
        Assert.Equal(selected.Id, alarm.ResourceId); Assert.Equal("Warning", alarm.Severity);
        await Evaluate(); Assert.Equal(1, (await Active()).OccurrenceCount);
        await new GovernanceRepository(db).UpdateAlertAsync(alarm.Id, new("native-admin", "native-acknowledge"), null, default);
        await Values(96); await Evaluate(); alarm = await Active();
        Assert.Equal("Active", alarm.State); Assert.Equal("Critical", alarm.Severity); Assert.Null(alarm.AcknowledgedBySubject);
        Assert.Equal(100, MonitoringAlertPolicy.ReadDetail(alarm)!.CoveragePercent);
        await new GovernanceRepository(db).UpdateAlertAsync(alarm.Id, new("native-admin", "native-silence"), now.AddHours(1), default);
        await Evaluate(); Assert.Equal("Silenced", (await Active()).State);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_events SET silenced_until_utc={now.AddSeconds(-1)}, version=version+1 WHERE id={alarm.Id}");
        await Values(null, "Unavailable"); await Evaluate(); alarm = await Active();
        Assert.Equal("Active", alarm.State); Assert.Equal("Critical", alarm.Severity); Assert.Null(alarm.RecoveredAtUtc);
        Assert.Equal("TelemetryUnknown", MonitoringAlertPolicy.ReadDetail(alarm)!.Reason);
        await Values(double.NaN); await Evaluate(); Assert.Null(MonitoringAlertPolicy.ReadDetail(await Active())!.Triggered);
        await Values(83); await Evaluate(); Assert.Equal("Active", (await Active()).State); // recovery threshold, not trigger threshold
        await Values(60); await Evaluate(); Assert.Empty(await db.AlertEvents.AsNoTracking().Where(item => item.State != "Recovered").ToArrayAsync());
        await Values(90); await Evaluate(); var retriggered = await Active();
        Assert.NotEqual(alarm.Id, retriggered.Id); Assert.Equal(alarm.Fingerprint, retriggered.Fingerprint);
        await using (var concurrent = new PlatformDbContext(options))
        {
            var stale = await concurrent.AlertEvents.SingleAsync(item => item.Id == retriggered.Id);
            await new GovernanceRepository(db).UpdateAlertAsync(retriggered.Id, new("native-admin", "native-race"), now.AddHours(1), default);
            stale.State = "Recovered"; stale.Version++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => concurrent.SaveChangesAsync());
        }
        await using (var duplicate = new PlatformDbContext(options))
        {
            duplicate.AlertEvents.Add(new AlertEvent { RuleId = rule.Id, ResourceId = selected.Id, Fingerprint = retriggered.Fingerprint, Severity = "Warning", SummaryCode = "duplicate" });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        // Malformed legacy configuration neither throws nor clears risk; disabled valid configuration is explicit recovery.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_rules SET threshold_json='{{}}'::jsonb WHERE id={rule.Id}");
        await Evaluate(); Assert.Equal("RuleInvalid", MonitoringAlertPolicy.ReadDetail(await Active())!.Reason);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_rules SET threshold_json='{{\"value\":85}}'::jsonb, is_enabled=false WHERE id={rule.Id}");
        await Evaluate(); Assert.Empty(await db.AlertEvents.AsNoTracking().Where(item => item.State != "Recovered").ToArrayAsync());
        Assert.Contains(await db.AlertEventHistory.AsNoTracking().Select(item => item.State).ToArrayAsync(), item => item == "SilenceExpired");
        Assert.Equal(2, await db.AlertEvents.CountAsync());
        var audit = await db.AuditEvents.AsNoTracking().Where(item => item.ActorSubject == "native-admin").ToArrayAsync();
        Assert.Equal(3, audit.Length);
        Assert.All(audit, item => { Assert.Null(item.DetailJson); Assert.Equal("alert", item.TargetType); Assert.StartsWith("native-", item.TraceId); });
        Assert.Contains(audit, item => item.Action == "alerts.acknowledge");
        Assert.Contains(audit, item => item.Action == "alerts.silence");

        // New state/load rules use the same real PostgreSQL lifecycle, without touching production services.
        var container = new ManagedResource { ResourceType = "Container", ExternalId = "isolated-container", DisplayName = "isolated", ProtectionLevel = "Managed", Source = "Test", LastSeenAtUtc = now,
            LabelsJson = "{\"state\":\"running\",\"attributes\":{\"dockerStatus\":\"Up 1 minute (unhealthy)\"}}" };
        var service = new ManagedResource { ResourceType = "SystemdUnit", ExternalId = "isolated-service", DisplayName = "isolated", ProtectionLevel = "Managed", Source = "Test", LastSeenAtUtc = now,
            LabelsJson = "{\"state\":\"failed\",\"attributes\":{\"loadState\":\"loaded\",\"subState\":\"failed\"}}" };
        var loadRule = new AlertRule { RuleType = "LoadAverage", ResourceSelectorJson = "{\"resourceIds\":[\"local\"]}", ThresholdJson = "{\"value\":150,\"criticalValue\":200,\"recoveryValue\":100}", EvaluationWindowSeconds = 12 };
        var containerRule = new AlertRule { RuleType = "ContainerHealth", ResourceSelectorJson = "{\"resourceIds\":[\"isolated-container\"]}" };
        var serviceRule = new AlertRule { RuleType = "SystemdHealth", ResourceSelectorJson = "{\"resourceIds\":[\"isolated-service\"]}" };
        var loadSeries = new MetricSeries { ResourceId = selected.Id, MetricKind = "load.normalized", Unit = "percent" };
        db.ManagedResources.AddRange(container, service); db.AlertRules.AddRange(loadRule, containerRule, serviceRule); db.MetricSeries.Add(loadSeries);
        foreach (var age in new[] { 9, 3 }) db.MetricSamples.Add(new MetricSample { SeriesId = loadSeries.Id, SampledAtUtc = now.AddSeconds(-age), ValueDouble = 250 });
        await db.SaveChangesAsync(); await Evaluate();
        var current = await db.AlertEvents.AsNoTracking().Where(item => item.State != "Recovered").ToArrayAsync();
        Assert.Equal(3, current.Length);
        Assert.Equal("Critical", current.Single(item => item.RuleId == loadRule.Id).Severity);
        Assert.Equal("ContainerUnhealthy", MonitoringAlertPolicy.ReadDetail(current.Single(item => item.RuleId == containerRule.Id))!.Reason);
        Assert.Equal("ServiceStopped", MonitoringAlertPolicy.ReadDetail(current.Single(item => item.RuleId == serviceRule.Id))!.Reason);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE managed_resources SET labels_json='{{\"state\":\"running\",\"attributes\":{{\"dockerStatus\":\"Up 1 minute (health: starting)\"}}}}'::jsonb WHERE id={container.Id}");
        await Evaluate();
        Assert.Null(MonitoringAlertPolicy.ReadDetail(await db.AlertEvents.AsNoTracking().SingleAsync(item => item.RuleId == containerRule.Id && item.State != "Recovered"))!.Triggered);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE managed_resources SET labels_json='{{\"state\":\"running\",\"attributes\":{{\"dockerStatus\":\"Up 1 minute (healthy)\"}}}}'::jsonb WHERE id={container.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE managed_resources SET labels_json='{{\"state\":\"active\",\"attributes\":{{\"loadState\":\"loaded\",\"subState\":\"running\"}}}}'::jsonb WHERE id={service.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metric_samples SET value_double=50 WHERE series_id={loadSeries.Id}");
        await Evaluate(); Assert.Empty(await db.AlertEvents.Where(item => item.State != "Recovered").ToArrayAsync());

        // Run the actual hosted evaluator with no usable Agent at all. Only isolated DB state is changed.
        var offlineRule = new AlertRule { RuleType = "AgentOffline", ResourceSelectorJson = "{\"resourceIds\":[\"local\"]}", EvaluationWindowSeconds = 12 };
        db.AlertRules.Add(offlineRule);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE managed_resources SET last_seen_at_utc={DateTimeOffset.UtcNow.AddMinutes(-2)} WHERE id={selected.Id}");
        var services = new ServiceCollection();
        services.AddDbContext<PlatformDbContext>(builder => builder.UseNpgsql(connection.ConnectionString));
        services.AddScoped<IAgentGateway>(_ => throw new InvalidOperationException("Agent must not be queried by the independent evaluator."));
        await using var provider = services.BuildServiceProvider();
        using var evaluator = new MonitoringAlertWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MonitoringAlertWorker>.Instance);
        await evaluator.StartAsync(default);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && !await db.AlertEvents.AsNoTracking().AnyAsync(item => item.RuleId == offlineRule.Id)) await Task.Delay(250);
            var offline = await db.AlertEvents.AsNoTracking().SingleAsync(item => item.RuleId == offlineRule.Id);
            Assert.Equal("Active", offline.State); Assert.True(MonitoringAlertPolicy.ReadDetail(offline)!.Triggered);
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await evaluator.StopAsync(stop.Token); }
    }
}
