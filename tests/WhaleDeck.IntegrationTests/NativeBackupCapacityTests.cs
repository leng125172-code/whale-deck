using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;
using WhaleDeck.Worker;

namespace WhaleDeck.IntegrationTests;

public sealed class NativePostgresFactAttribute : FactAttribute
{
    public NativePostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION")))
            Skip = "Requires an explicitly provisioned isolated PostgreSQL database; not a mocked integration pass.";
    }
}

public sealed class NativeBackupCapacityTests
{
    [NativePostgresFact]
    public async Task PolicyCapacityStateIsPersistedDeduplicatedAndRecoveredInRealPostgres()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WHALEDECK_NATIVE_TEST_CONNECTION"));
        if (connection.Database is null || !Regex.IsMatch(connection.Database, "^wdv_capacity_[a-f0-9]{16}$"))
            throw new InvalidOperationException("Native test requires its isolated database namespace.");
        await using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var resource = new ManagedResource { ResourceType = "ComposeApplication", ExternalId = "application:capacity-native", DisplayName = "Native isolated capacity", ProtectionLevel = "Managed", Source = "Validation" };
        var policy = new BackupPolicy
        {
            InstanceResourceId = resource.Id,
            TargetDirectoryId = BackupTargetCapacityDto.FixedTarget,
            ScheduleExpression = "0 20 * * 0",
            CapacityWarningPercent = 80,
            CapacityCriticalPercent = 90
        };
        db.ManagedResources.Add(resource); db.BackupPolicies.Add(policy); await db.SaveChangesAsync();
        var proxy = DispatchProxy.Create<IAgentGateway, CapacityAgentProxy>();
        var sample = (CapacityAgentProxy)proxy;
        sample.Current = Capacity(81);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        var alarm = await db.AlertEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Warning", alarm.Severity); Assert.Equal(1, alarm.OccurrenceCount);
        Assert.Equal(BackupCapacityAlerts.RuleId(policy.Id), alarm.RuleId);
        var repository = new GovernanceRepository(db);
        await repository.UpdateAlertAsync(alarm.Id, new("native-validation", "native-backup-acknowledge"), null, default);
        sample.Current = Capacity(91);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        alarm = await db.AlertEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Critical", alarm.Severity); Assert.Equal("Active", alarm.State); Assert.Null(alarm.AcknowledgedAtUtc);
        await repository.UpdateAlertAsync(alarm.Id, new("native-validation", "native-backup-silence"), DateTimeOffset.UtcNow.AddHours(1), default);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        Assert.Equal("Silenced", (await db.AlertEvents.AsNoTracking().SingleAsync()).State);
        db.ChangeTracker.Clear();
        await db.AlertEvents.Where(item => item.Id == alarm.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SilencedUntilUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        Assert.Equal("Active", (await db.AlertEvents.AsNoTracking().SingleAsync()).State);
        sample.Current = BackupTargetCapacityDto.Unknown("synthetic-unavailable");
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        alarm = await db.AlertEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Active", alarm.State); Assert.Equal("Critical", alarm.Severity);
        Assert.False(BackupCapacityPolicy.ReadDetail(alarm)!.Available);
        sample.Current = Capacity(79);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        alarm = await db.AlertEvents.AsNoTracking().SingleAsync();
        Assert.Equal("Recovered", alarm.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAlertAsync(alarm.Id, new("native-validation", "native-backup-recovered"), null, default));
        sample.Current = Capacity(81);
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        Assert.Equal(2, await db.AlertEvents.CountAsync());
        db.ChangeTracker.Clear();
        await db.BackupPolicies.Where(item => item.Id == policy.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsEnabled, false));
        sample.Current = BackupTargetCapacityDto.Unknown("synthetic-unavailable");
        await BackupCapacityAlerts.EvaluateAsync(db, proxy, default);
        Assert.Equal(2, await db.AlertEvents.CountAsync(item => item.State == "Recovered"));
        Assert.False((await db.AlertRules.AsNoTracking().SingleAsync()).IsEnabled);
        Assert.Contains(await db.AlertEventHistory.AsNoTracking().ToArrayAsync(), item => item.State == "SeverityChanged");
        Assert.Contains(await db.AlertEventHistory.AsNoTracking().ToArrayAsync(), item => item.State == "SilenceExpired");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAlertRuleAsync(new("native-validation", "native-backup-rule"), new SaveAlertRuleCommand(
            alarm.RuleId, "CpuUtilization", "{}", "{\"value\":50}", 60, "Warning", true, 0), default));
    }

    private static BackupTargetCapacityDto Capacity(int usedPercent) => new(BackupTargetCapacityDto.FixedTarget, true,
        100L * 1024 * 1024 * 1024, usedPercent * 1024L * 1024 * 1024, (100 - usedPercent) * 1024L * 1024 * 1024, DateTimeOffset.UtcNow, null);

    public class CapacityAgentProxy : DispatchProxy
    {
        public BackupTargetCapacityDto Current { get; set; } = BackupTargetCapacityDto.Unknown("uninitialized");
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == nameof(IAgentGateway.GetBackupTargetCapacityAsync)
            ? Task.FromResult(Current) : throw new NotSupportedException("Native capacity test never executes other Agent methods.");
    }
}
