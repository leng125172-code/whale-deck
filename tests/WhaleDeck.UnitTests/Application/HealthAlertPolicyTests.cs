using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class HealthAlertPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
    private static ManagedResource Resource(string state, string status, string type = "Container", string load = "loaded", string sub = "running") => new()
    {
        ResourceType = type, ExternalId = "synthetic", DisplayName = "synthetic", ProtectionLevel = "Managed", Source = "Test", LastSeenAtUtc = Now,
        LabelsJson = JsonSerializer.Serialize(new { state, attributes = new { dockerStatus = status, loadState = load, subState = sub } })
    };

    [Theory]
    [InlineData("running", "Up 3 minutes (unhealthy)", true, "ContainerUnhealthy")]
    [InlineData("running", "Up 3 minutes (healthy)", false, "ContainerHealthy")]
    [InlineData("running", "Up 3 minutes", false, "ProcessRunningNoHealthcheck")]
    [InlineData("running", "Up 3 minutes (health: starting)", null, "HealthStarting")]
    [InlineData("exited", "Exited (1)", true, "ContainerNotRunning")]
    [InlineData("paused", "Up 3 minutes (Paused)", true, "ContainerNotRunning")]
    [InlineData("running", "unknown", null, "TelemetryUnknown")]
    public void ContainerHealthIsDistinctFromRunningState(string state, string status, bool? triggered, string reason)
    {
        var result = MonitoringAlertPolicy.ObserveState("ContainerHealth", Resource(state, status), Now, "Warning");
        Assert.Equal(triggered, result.Triggered); Assert.Equal(reason, result.Reason);
    }

    [Theory]
    [InlineData("active", "loaded", false)]
    [InlineData("failed", "loaded", true)]
    [InlineData("inactive", "loaded", true)]
    [InlineData("activating", "loaded", null)]
    [InlineData("active", "not-found", null)]
    public void SystemdMissingOrTransitioningIsUnknown(string state, string load, bool? expected) =>
        Assert.Equal(expected, MonitoringAlertPolicy.ObserveState("SystemdHealth", Resource(state, "", "SystemdUnit", load), Now, "Warning").Triggered);

    [Theory]
    [InlineData(-19)]
    [InlineData(1)]
    public void StaleOrFutureResourceNeverClearsRisk(int age)
    {
        var resource = Resource("running", "Up 1 minute (healthy)"); resource.LastSeenAtUtc = Now.AddSeconds(age);
        Assert.Null(MonitoringAlertPolicy.ObserveState("ContainerHealth", resource, Now, "Warning").Triggered);
    }

    [Fact]
    public void InvalidMetadataDoesNotAbortOtherRules()
    {
        var resource = Resource("running", "Up 1 minute (healthy)"); resource.LabelsJson = "{bad-json";
        Assert.Null(MonitoringAlertPolicy.ObserveState("ContainerHealth", resource, Now, "Warning").Triggered);
    }

    [Fact]
    public void NormalizedLoadCanExceedOneHundredPercent()
    {
        var policy = MonitoringAlertPolicy.Parse(new SaveAlertRuleCommand(null, "LoadAverage", "{}", "{\"value\":150,\"criticalValue\":200,\"recoveryValue\":100}", 12, "Warning", true, null));
        var value = policy.Metric(Guid.NewGuid(), 250, 2, Now, Now, 12, Now, "Warning", false);
        Assert.True(value.Triggered); Assert.Equal("Critical", value.Severity);
        Assert.Throws<ArgumentException>(() => MonitoringAlertPolicy.Parse(new(null, "LoadAverage", "{}", "{\"value\":1001}", 12, "Warning", true, null)));
        Assert.Throws<ArgumentException>(() => MonitoringAlertPolicy.Parse(new(null, "CpuUtilization", "{}", "{\"value\":150}", 12, "Warning", true, null)));
        Assert.Null(policy.Metric(Guid.NewGuid(), -1, 2, Now, Now, 12, Now, "Warning", true).Triggered);
    }

    [Fact]
    public void ActiveServiceWithUnknownSubstateCannotClearRisk() =>
        Assert.Null(MonitoringAlertPolicy.ObserveState("SystemdHealth", Resource("active", "", "SystemdUnit", sub: ""), Now, "Warning").Triggered);
}
