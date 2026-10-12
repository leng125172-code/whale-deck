using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ManagedResourcePlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"whaledeck-managed-plan-{Guid.NewGuid():N}");

    [Fact]
    public void DatabasePlanUsesRegisteredResourceWithoutDockerImageParameters()
    {
        var (planner, plans) = CreatePlanner();
        var request = Request("databases", "create", "database-platform.postgres", new()
        {
            ["name"] = "wdv_postgres",
            ["password"] = "must-not-enter-plan",
            ["inputTicket"] = "must-not-enter-plan-either"
        });

        var response = planner.Create(request);

        Assert.Equal(64, response.PlanHash.Length);
        Assert.Contains("databases:create:database-platform.postgres", response.Changes);
        plans.VerifyAndConsume(response.PlanHash, "database-platform.postgres", "create",
            new Dictionary<string, string> { ["name"] = "wdv_postgres" });
    }

    [Fact]
    public void EmptyApplicationResourceNormalizesToRegisteredAppsDirectory()
    {
        var (planner, _) = CreatePlanner();
        var response = planner.Create(Request("applications", "install", string.Empty,
            new Dictionary<string, string> { ["slug"] = "demo", ["image"] = "alpine:3.22" }));

        Assert.Contains("applications:install:whaledeck.apps", response.Changes);
    }

    [Theory]
    [InlineData("containers")]
    [InlineData("identity")]
    [InlineData("")]
    public void UnsupportedManagedAreaFailsClosed(string area)
    {
        var (planner, _) = CreatePlanner();
        Assert.Throws<InvalidOperationException>(() => planner.Create(Request(area, "create", "database-platform.postgres", new())));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("install")]
    [InlineData("apply-settings")]
    public void PlannedActionsMatchApplicationPolicy(string action) =>
        Assert.True(ManagedResourcePlanner.RequiresPlan(action));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    private (ManagedResourcePlanner Planner, PlanStore Plans) CreatePlanner()
    {
        Directory.CreateDirectory(_root);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:PlanDirectory"] = Path.Combine(_root, "plans"),
            ["Agent:ResourceRegistryFile"] = Path.Combine(_root, "missing-resources.json")
        }).Build();
        var plans = new PlanStore(configuration);
        return (new ManagedResourcePlanner(new ResourceRegistry(configuration), plans), plans);
    }

    private static RegisteredActionRequest Request(
        string area,
        string action,
        string resourceId,
        Dictionary<string, string> parameters)
    {
        var request = new RegisteredActionRequest
        {
            Action = action,
            Resource = new ResourceReference { ResourceId = resourceId, ResourceType = area }
        };
        request.Parameters.Add(parameters);
        return request;
    }
}
