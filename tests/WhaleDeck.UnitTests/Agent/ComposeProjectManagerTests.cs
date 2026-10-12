using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ComposeProjectManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "whaledeck-compose-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("compose:whaledeck")]
    [InlineData("application:../core")]
    [InlineData("application:/data/core")]
    [InlineData("application:foo/bar")]
    [InlineData("application:foo\\bar")]
    [InlineData("application:")]
    [InlineData("application:demo\n")]
    public void UnapprovedIdentifiersFailClosed(string id) =>
        Assert.Throws<InvalidOperationException>(() => ComposeProjectManager.ParseResourceId(id));

    [Fact]
    public void ParseHashesRejectsMalformedDuplicateEmptyAndOversizedOutput()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, ComposeProjectManager.ParseServiceHashes("app " + hash)["app"]);
        foreach (var invalid in new[] { "", "app secret", "../app " + hash, $"app {hash}\napp {hash}",
                     string.Join('\n', Enumerable.Range(0, 65).Select(index => $"app{index} {hash}")) })
            Assert.Throws<InvalidOperationException>(() => ComposeProjectManager.ParseServiceHashes(invalid));
    }

    [Fact]
    public void DifferencesShowChangedMissingAndRetainedServicesWithoutEnvironmentValues()
    {
        var hash = new string('a', 64);
        var desired = new Dictionary<string, string> { ["web"] = hash, ["db"] = hash, ["new"] = hash };
        var members = new[] { Container("web", hash), Container("db", new string('b', 64)), Container("old", hash) };
        var changes = ComposeProjectManager.DescribeDifferences(desired, members);
        Assert.Contains("web: Unchanged; affected containers: 1", changes);
        Assert.Contains("db: ConfigurationChanged; affected containers: 1", changes);
        Assert.Contains("new: Missing; affected containers: 0", changes);
        Assert.Contains("old: Orphaned; retained (no automatic deletion)", changes);
        Assert.DoesNotContain(hash, string.Join('\n', changes));
    }

    [Fact]
    public void FingerprintBindsPrivateEnvironmentAndInvalidatesSingleUsePlan()
    {
        var directory = Installation();
        var first = ComposeProjectManager.ConfigurationFingerprint(directory);
        var plans = new PlanStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Agent:PlanDirectory"] = Path.Combine(_root, "plans") }).Build());
        var parameters = new Dictionary<string, string>();
        var plan = plans.Create("application:demo", "redeploy", parameters, first);
        File.WriteAllText(Path.Combine(directory, ".env"), "PRIVATE_EXAMPLE=changed");
        var second = ComposeProjectManager.ConfigurationFingerprint(directory);
        Assert.NotEqual(first, second);
        Assert.Throws<InvalidOperationException>(() => plans.VerifyAndConsume(plan.PlanHash, "application:demo", "redeploy", parameters, second));
        Assert.DoesNotContain("PRIVATE_EXAMPLE", File.ReadAllText(Path.Combine(_root, "plans", plan.PlanHash + ".json")));
        plans.VerifyAndConsume(plan.PlanHash, "application:demo", "redeploy", parameters, first);
        Assert.Throws<InvalidOperationException>(() => plans.VerifyAndConsume(plan.PlanHash, "application:demo", "redeploy", parameters, first));
    }

    [Fact]
    public void MissingOrOversizedInputsAreRejected()
    {
        var directory = Installation();
        Assert.Equal(directory, ComposeProjectManager.ResolveDirectory(_root, "demo"));
        File.WriteAllText(Path.Combine(directory, "compose.yml"), new string('x', 65537));
        Assert.Throws<InvalidOperationException>(() => ComposeProjectManager.ResolveDirectory(_root, "demo"));
        File.Delete(Path.Combine(directory, "manifest.json"));
        Assert.Throws<InvalidOperationException>(() => ComposeProjectManager.ResolveDirectory(_root, "demo"));
    }

    [Fact]
    public void MixedServiceStatesAreNotReportedAsStopped()
    {
        Assert.Equal("Missing", ComposeProjectManager.State([]));
        Assert.Equal("Partial", ComposeProjectManager.State([Container("web", "", "running"), Container("db", "", "exited")]));
        Assert.Equal("Restarting", ComposeProjectManager.State([Container("web", "", "restarting")]));
    }

    [Fact]
    public void HealthRequiresEveryDesiredServiceNotJustOneNamedContainer()
    {
        var running = new[] { Container("web", ""), Container("db", "") };
        Assert.True(ComposeProjectManager.IsHealthy(running, ["web", "db"]));
        Assert.False(ComposeProjectManager.IsHealthy(running, ["web", "db", "missing"]));
        running[1].Status = "Up (health: starting)";
        Assert.False(ComposeProjectManager.IsHealthy(running, ["web", "db"]));
        running[1].Status = "Up (unhealthy)";
        Assert.False(ComposeProjectManager.IsHealthy(running, ["web", "db"]));
    }

    [Theory]
    [InlineData("""{"name":"database-platform","services":{"app":{}}}""")]
    [InlineData("""{"name":"whaledeck-app-demo","services":{"app":{"container_name":"database-platform-postgres"}}}""")]
    [InlineData("""{"name":"whaledeck-app-demo","services":{"app":{"container_name":"whaledeck-api"}}}""")]
    [InlineData("""{"name":"whaledeck-app-demo","services":{"app":{"labels":{"io.whaledeck.protected":"true"}}}}""")]
    public void ConfigurationCannotRetargetCoreResources(string json)
    {
        var registry = new ResourceRegistry(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Agent:ResourceRegistryFile"] = Path.Combine(_root, "absent.json") }).Build());
        Assert.Throws<InvalidOperationException>(() => ComposeProjectManager.ValidateProjectIdentity(json, "whaledeck-app-demo", registry));
    }

    [Theory]
    [InlineData("whaledeck-api")]
    [InlineData("whaledeck-worker")]
    [InlineData("whaledeck-gateway")]
    public void ControlPlaneNamesRemainProtectedWithoutDockerLabels(string name)
    {
        var registry = new ResourceRegistry(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Agent:ResourceRegistryFile"] = Path.Combine(_root, "absent.json") }).Build());
        Assert.True(registry.IsProtectedContainer(name, null));
    }

    private string Installation()
    {
        var directory = Path.Combine(_root, "demo");
        Directory.CreateDirectory(directory);
        foreach (var file in new[] { "compose.yml", ".env", "manifest.json" }) File.WriteAllText(Path.Combine(directory, file), "{}");
        return directory;
    }

    private static ContainerListResponse Container(string service, string hash, string state = "running") => new()
    {
        State = state, Labels = new Dictionary<string, string>
        { ["com.docker.compose.service"] = service, ["com.docker.compose.config-hash"] = hash }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }
}
