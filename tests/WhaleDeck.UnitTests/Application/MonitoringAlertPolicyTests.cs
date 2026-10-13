using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class MonitoringAlertPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);
    private static SaveAlertRuleCommand Command(string threshold = "{\"value\":85,\"criticalValue\":95,\"recoveryValue\":80}", string selector = "{}") =>
        new(null, "CpuUtilization", selector, threshold, 60, "Warning", true, null);

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"value\":101}")]
    [InlineData("{\"value\":85,\"criticalValue\":70}")]
    [InlineData("{\"value\":85,\"recoveryValue\":90}")]
    [InlineData("{\"value\":85,\"operator\":\"<\"}")]
    [InlineData("{\"value\":85,\"value\":90}")]
    [InlineData("{\"value\":85,\"minimumCoveragePercent\":49}")]
    public void RejectInvalidThreshold(string threshold) => Assert.ThrowsAny<Exception>(() => MonitoringAlertPolicy.Parse(Command(threshold)));

    [Theory]
    [InlineData("{\"unknown\":1}")]
    [InlineData("{\"resourceIds\":[]}")]
    [InlineData("{\"resourceIds\":[null]}")]
    [InlineData("{\"resourceIds\":[\"a\"],\"resourceIds\":[\"b\"]}")]
    public void RejectInvalidSelector(string selector) => Assert.ThrowsAny<Exception>(() => MonitoringAlertPolicy.Parse(Command(selector: selector)));

    [Fact]
    public void SelectorsMatchStableResourceOrExternalIdentityAndDevice()
    {
        var resource = new ManagedResource { ResourceType = "Host", ExternalId = "local", DisplayName = "host", Source = "Test", ProtectionLevel = "Managed" };
        var policy = MonitoringAlertPolicy.Parse(Command(selector: "{\"resourceIds\":[\"local\"],\"deviceIds\":[\"nvme0n1\"]}"));
        Assert.True(policy.Includes(resource));
        Assert.True(policy.IncludesDevice("{\"DeviceId\":\"nvme0n1\"}"));
        Assert.False(policy.IncludesDevice("{\"DeviceId\":\"sda\"}"));
        Assert.False(policy.Includes(OtherResource()));
        static ManagedResource OtherResource() => new() { ResourceType = "Host", ExternalId = "other", DisplayName = "other", Source = "Test", ProtectionLevel = "Managed" };
    }

    [Theory]
    [InlineData(7, -6, -6, 50)]
    [InlineData(10, -30, -30, 50)]
    [InlineData(10, -12, -6, 50)]
    [InlineData(10, 6, 6, 50)]
    [InlineData(10, -6, -6, double.NaN)]
    public void InsufficientStaleInvalidOrFutureDataIsUnknown(long samples, int validAge, int observedAge, double value)
    {
        var policy = MonitoringAlertPolicy.Parse(Command());
        Assert.Null(policy.Metric(Guid.NewGuid(), value, samples, Now.AddSeconds(validAge), Now.AddSeconds(observedAge), 60, Now, "Warning", true).Triggered);
    }

    [Fact]
    public void HysteresisEscalationAndUnknownLifecycle()
    {
        var policy = MonitoringAlertPolicy.Parse(Command());
        Assert.False(policy.Metric(Guid.NewGuid(), 83, 10, Now, Now, 60, Now, "Warning", false).Triggered);
        Assert.True(policy.Metric(Guid.NewGuid(), 83, 10, Now, Now, 60, Now, "Warning", true).Triggered);
        var item = new AlertEvent { RuleId = Guid.NewGuid(), Fingerprint = "test", Severity = "Warning", SummaryCode = "ALERT_CPUUTILIZATION",
            State = "Acknowledged", AcknowledgedBySubject = "admin", AcknowledgedAtUtc = Now.AddMinutes(-1), LastOccurredAtUtc = Now };
        var critical = policy.Metric(Guid.NewGuid(), 96, 10, Now, Now, 60, Now, "Warning", true);
        Assert.Contains("SeverityChanged", MonitoringAlertPolicy.Apply(item, critical, 60, Now));
        Assert.Equal("Active", item.State); Assert.Equal("Critical", item.Severity); Assert.Null(item.AcknowledgedBySubject);
        item.State = "Silenced"; item.SilencedUntilUtc = Now.AddSeconds(-1);
        Assert.Contains("SilenceExpired", MonitoringAlertPolicy.Apply(item, new(null, null, "Warning", Reason: "TelemetryUnknown"), 60, Now));
        Assert.Equal("Active", item.State); Assert.Equal("Critical", item.Severity); Assert.Null(item.RecoveredAtUtc);
        Assert.Contains("Recovered", MonitoringAlertPolicy.Apply(item, new(null, false, "Warning"), 60, Now));
        Assert.Equal("Recovered", item.State);
    }
}
