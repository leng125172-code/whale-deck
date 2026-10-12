using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Worker;

namespace WhaleDeck.UnitTests.Application;

public sealed class BackupCapacityPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static BackupPolicy Policy() => new()
    {
        InstanceResourceId = Guid.NewGuid(),
        TargetDirectoryId = BackupTargetCapacityDto.FixedTarget,
        ScheduleExpression = "0 20 * * 0",
        CapacityWarningPercent = 80,
        CapacityCriticalPercent = 90
    };
    private static BackupTargetCapacityDto Capacity(int percent) => new(BackupTargetCapacityDto.FixedTarget, true,
        100L * 1024 * 1024 * 1024, percent * 1024L * 1024 * 1024, (100 - percent) * 1024L * 1024 * 1024, Now, null);
    private static AlertEvent Event() => new()
    {
        RuleId = Guid.NewGuid(),
        Fingerprint = "test",
        Severity = "Warning",
        SummaryCode = "ALERT_BACKUPSTORAGECAPACITY",
        LastOccurredAtUtc = Now
    };

    [Theory]
    [InlineData(79, false, "Warning")]
    [InlineData(80, true, "Warning")]
    [InlineData(89, true, "Warning")]
    [InlineData(90, true, "Critical")]
    [InlineData(100, true, "Critical")]
    public void ThresholdBoundariesArePolicySpecific(int percent, bool triggered, string severity)
    {
        Assert.Equal(triggered, BackupCapacityPolicy.Triggered(Policy(), Capacity(percent), Now));
        Assert.Equal(severity, BackupCapacityPolicy.Severity(Policy(), Capacity(percent), Now));
    }

    [Fact]
    public void SmallFreeCapacityIsCriticalEvenBelowWarningPercent()
    {
        var capacity = Capacity(20) with { FreeBytes = 10 };
        Assert.True(BackupCapacityPolicy.Triggered(Policy(), capacity, Now));
        Assert.Equal("Critical", BackupCapacityPolicy.Severity(Policy(), capacity, Now));
    }

    [Fact]
    public void UnknownOrStaleCapacityDoesNotRecoverOrDowngradeExistingCriticalAlarm()
    {
        foreach (var sample in new[] { BackupTargetCapacityDto.Unknown("unavailable"), Capacity(20) with { ObservedAtUtc = Now.AddMinutes(-2) } })
        {
            var item = Event(); item.Severity = "Critical";
            Assert.Null(BackupCapacityPolicy.Apply(item, Policy(), sample, Now));
            Assert.Equal("Active", item.State);
            Assert.Equal("Critical", item.Severity);
            Assert.False(BackupCapacityPolicy.ReadDetail(item)!.Available);
            Assert.Null(BackupCapacityPolicy.ReadDetail(item)!.UsedPercent);
        }
    }

    [Fact]
    public void FreshBelowThresholdSampleRecoversButDoesNotDeleteHistory()
    {
        var item = Event(); item.State = "Silenced"; item.SilencedUntilUtc = Now.AddHours(1);
        Assert.Equal("Recovered", BackupCapacityPolicy.Apply(item, Policy(), Capacity(79), Now));
        Assert.Equal(Now, item.RecoveredAtUtc);
        Assert.Null(item.SilencedUntilUtc);
    }

    [Fact]
    public void DisabledPolicyExplicitlyResolvesAlarmEvenWhenAgentIsUnavailable()
    {
        var policy = Policy(); policy.IsEnabled = false;
        Assert.Equal("Recovered", BackupCapacityPolicy.Apply(Event(), policy, BackupTargetCapacityDto.Unknown("offline"), Now));
    }

    [Fact]
    public void EscalationRequiresNewAcknowledgementButRespectsExplicitFutureSilence()
    {
        var item = Event(); item.State = "Acknowledged"; item.AcknowledgedAtUtc = Now; item.AcknowledgedBySubject = "admin";
        Assert.Equal("SeverityChanged", BackupCapacityPolicy.Apply(item, Policy(), Capacity(91), Now));
        Assert.Equal("Active", item.State); Assert.Null(item.AcknowledgedAtUtc);
        item = Event(); item.State = "Silenced"; item.SilencedUntilUtc = Now.AddHours(1);
        BackupCapacityPolicy.Apply(item, Policy(), Capacity(91), Now);
        Assert.Equal("Silenced", item.State); Assert.Equal("Critical", item.Severity);
    }

    [Fact]
    public void SilenceExpirationAndRepeatCountAreBounded()
    {
        var item = Event(); item.State = "Silenced"; item.SilencedUntilUtc = Now.AddSeconds(-1);
        Assert.Equal("SilenceExpired", BackupCapacityPolicy.Apply(item, Policy(), Capacity(81), Now));
        Assert.Equal("Active", item.State);
        for (var index = 0; index < 10; index++) BackupCapacityPolicy.Apply(item, Policy(), Capacity(81), Now.AddSeconds(index));
        Assert.Equal(1, item.OccurrenceCount);
        BackupCapacityPolicy.Apply(item, Policy(), Capacity(81), Now.AddSeconds(60));
        Assert.Equal(2, item.OccurrenceCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidOrWrongFilesystemSamplesAreNotCurrent(int percent)
    {
        Assert.False(Capacity(percent).IsCurrent(Now));
        Assert.False((Capacity(20) with { TargetDirectoryId = "root.disk" }).IsCurrent(Now));
    }

    [Fact]
    public void StableRuleIdentityIsIndependentFromPolicyVersionAndDoesNotMergePolicies()
    {
        var id = Guid.NewGuid();
        Assert.Equal(BackupCapacityAlerts.RuleId(id), BackupCapacityAlerts.RuleId(id));
        Assert.NotEqual(BackupCapacityAlerts.RuleId(id), BackupCapacityAlerts.RuleId(Guid.NewGuid()));
    }
}
