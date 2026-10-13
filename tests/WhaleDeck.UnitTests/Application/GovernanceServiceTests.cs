using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class GovernanceServiceTests
{
    private readonly GovernanceService _service = new(new NoOpRepository());

    [Fact]
    public async Task ScheduleRejectsArbitraryTaskType()
    {
        var command = new SaveScheduledTaskCommand(null, "Shell", "bad", "Cron", "0 20 * * 0", "Asia/Shanghai", "{}", "Forbid", 300, true, null);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveScheduleAsync("admin", command, default));
    }

    [Fact]
    public async Task BackupRejectsUnregisteredTarget()
    {
        var command = new SaveBackupPolicyCommand(null, "database-platform.postgres", true, "0 2 * * *", "Asia/Shanghai", 14, 14,
            "/tmp", "Default", true, 80, 90, null);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveBackupPolicyAsync(command, default));
    }

    [Theory]
    [InlineData("gzip", true, "quiesced", false)]
    [InlineData("zstd", true, "quiesced", true)]
    [InlineData("gzip", false, "quiesced", true)]
    [InlineData("gzip", true, "shell", true)]
    public async Task ApplicationPolicyRejectsUnconfirmedOrUnsupportedArchiveModes(string compression, bool verify, string consistency, bool confirmed)
    {
        var command = new SaveBackupPolicyCommand(null, "application:demo", true, "0 2 * * *", "Asia/Shanghai", 14, 14,
            "database-platform.backup.hdd", compression, verify, 80, 90, null, consistency, confirmed);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveBackupPolicyAsync(command, default));
    }

    [Fact]
    public async Task BackupPolicyRejectsInvalidTimezoneBeforePersistence()
    {
        var command = new SaveBackupPolicyCommand(null, "database-platform.postgres", true, "0 2 * * *", "Not/AZone", 14, 14,
            "database-platform.backup.hdd", "Default", true, 80, 90, null);
        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => _service.SaveBackupPolicyAsync(command, default));
    }

    [Theory]
    [InlineData("secret.value")]
    [InlineData("unknown")]
    public async Task SettingsUseAllowList(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveSettingAsync(key, "admin", new("{}", null), default));
    }

    [Fact]
    public async Task AlertRejectsInvalidJson()
    {
        var command = new SaveAlertRuleCommand(null, "CpuUtilization", "{}", "not-json", 60, "Warning", true, null);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAlertRuleAsync(new("admin", "unit-trace"), command, default));
    }

    [Fact]
    public async Task AlertRequiresActorAndRequestTrace()
    {
        var command = new SaveAlertRuleCommand(null, "CpuUtilization", "{}", "{\"value\":90}", 60, "Warning", true, null);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAlertRuleAsync(new("", "trace"), command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAlertAsync(Guid.NewGuid(), new("admin", ""), null, default));
    }

    [Fact]
    public async Task AuditRejectsInvertedTimeRange()
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ListAuditAsync(
            100, null, null, null, now, now.AddMinutes(-1), false, default));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("action")]
    [InlineData("result")]
    public async Task AuditRejectsOversizedFilters(string filter)
    {
        var value = new string('x', 257);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ListAuditAsync(
            100,
            filter == "actor" ? value : null,
            filter == "action" ? value : null,
            filter == "result" ? value : null,
            null,
            null,
            false,
            default));
    }

    private sealed class NoOpRepository : IGovernanceRepository
    {
        public Task<IReadOnlyCollection<ScheduledTask>> ListSchedulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ScheduledTask>>([]);
        public Task<ScheduledTask> SaveScheduleAsync(string actorSubject, SaveScheduledTaskCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ScheduledTask> TriggerScheduleAsync(Guid id, string actorSubject, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task DeleteScheduleAsync(Guid id, long expectedVersion, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<ScheduledTaskRun>> ListScheduleRunsAsync(Guid? scheduleId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ScheduledTaskRun>>([]);
        public Task<IReadOnlyCollection<BackupPolicy>> ListBackupPoliciesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<BackupPolicy>>([]);
        public Task<IReadOnlyCollection<BackupRecord>> ListBackupRecordsAsync(string? instanceResourceId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<BackupRecord>>([]);
        public Task<string> ResolveResourceExternalIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult("database-platform.postgres");
        public Task<BackupPolicy> SaveBackupPolicyAsync(SaveBackupPolicyCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyCollection<AlertRule>> ListAlertRulesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AlertRule>>([]);
        public Task<AlertRule> SaveAlertRuleAsync(AlertWriteContext context, SaveAlertRuleCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyCollection<AlertEvent>> ListAlertsAsync(bool includeRecovered, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AlertEvent>>([]);
        public Task<IReadOnlyCollection<AlertEventHistory>> ListAlertHistoryAsync(Guid? alertEventId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AlertEventHistory>>([]);
        public Task<AlertEvent> UpdateAlertAsync(Guid id, AlertWriteContext context, DateTimeOffset? silencedUntilUtc, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyCollection<PlatformSetting>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformSetting>>([]);
        public Task<PlatformSetting> SaveSettingAsync(string key, string actorSubject, SavePlatformSettingCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyCollection<AuditEvent>> ListAuditAsync(int take, string? actorSubject, string? action, string? result, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AuditEvent>>([]);
    }
}
