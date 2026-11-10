using System.Text.Json.Nodes;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DevelopmentRetirementPlanBindingTests
{
    private static string Plan() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Agent/Fixtures/development-retirement-plan.json"));
    private static (WorkspaceArchiveRequest Request, WorkspaceArchiveReceipt Receipt) Verified()
    {
        // Independent Python canonical vectors, not storage acceptance.
        var value = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Agent/Fixtures/development-archive-protocol.json")))![2]!;
        var input = value["request"]!;
        var request = new WorkspaceArchiveRequest { Storage = new() { WorkspaceId = input["storage"]!["workspaceId"]!.GetValue<string>(),
            StorageRootId = "nvme-main", EnvironmentSha256 = new string('a', 64), DiskMib = 256, ActorSubject = "owner", TraceId = "trace" },
            TaskId = input["taskId"]!.GetValue<string>(), ArchiveId = input["archiveId"]!.GetValue<string>(), Phase = "verify", ExpectedEpoch = 2,
            PlanSha256 = input["planSha256"]!.GetValue<string>(), ReceiptSha256 = input["receiptSha256"]!.GetValue<string>() };
        return (request, DevelopmentArchiveBinding.Parse(request, value["receipt"]!.ToJsonString()));
    }
    [Fact]
    public void IndependentPythonPlanMatchesTheCompleteVerifiedArchiveWithoutAuthorizingExecution()
    {
        var (request, receipt) = Verified();
        Assert.True(DevelopmentRetirementPlanBinding.Matches(request, receipt, Plan()));
        var value = JsonNode.Parse(Plan())!.AsObject(); Assert.False(value["deletionAuthorized"]!.GetValue<bool>());
        Assert.True(value["retainsSource"]!.GetValue<bool>() && value["reservesBudget"]!.GetValue<bool>());
    }
    [Fact]
    public void ChangedOrMissingFactsAndAuthorizationFlagsCannotReuseTheDigest()
    {
        var (request, receipt) = Verified();
        foreach (var property in JsonNode.Parse(Plan())!.AsObject().Select(p => p.Key).ToArray())
        {
            var changed = JsonNode.Parse(Plan())!.AsObject(); changed.Remove(property);
            Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, changed.ToJsonString()));
            changed = JsonNode.Parse(Plan())!.AsObject(); changed[property] = null;
            Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, changed.ToJsonString()));
        }
        foreach (var property in new[] { "retainsSource", "reservesBudget", "deletionAuthorized" })
        {
            var changed = JsonNode.Parse(Plan())!.AsObject(); changed[property] = !changed[property]!.GetValue<bool>();
            Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, changed.ToJsonString()));
        }
        var extra = JsonNode.Parse(Plan())!.AsObject(); extra["executionToken"] = "forbidden";
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, extra.ToJsonString()));
    }
    [Fact]
    public void OtherArchiveSourceReceiptAndNonVerifyStatusAreRejected()
    {
        var (request, receipt) = Verified();
        var other = request.Clone(); other.ArchiveId = Guid.NewGuid().ToString("D");
        Assert.False(DevelopmentRetirementPlanBinding.Matches(other, receipt, Plan()));
        other = request.Clone(); other.Phase = "capture"; other.ReceiptSha256 = "";
        Assert.False(DevelopmentRetirementPlanBinding.Matches(other, receipt, Plan()));
        var changed = receipt.Clone(); changed.Status = "Running";
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, changed, Plan()));
        var value = JsonNode.Parse(Plan())!.AsObject(); value["storage"]!["diskMiB"] = 512;
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, value.ToJsonString()));
    }
    [Fact]
    public void DuplicateKeysWrongTypesFractionalNumbersAndOversizedInputFailClosed()
    {
        var (request, receipt) = Verified();
        var raw = Plan();
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, raw.Insert(1, System.Text.Json.JsonSerializer.Serialize("schemaVersion") + ":1,")));
        var countKey = System.Text.Json.JsonSerializer.Serialize("entryCount");
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, raw.Replace(countKey + ":2", countKey + ":2.0", StringComparison.Ordinal)));
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, new string(' ', 8193)));
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, "[]"));
        Assert.False(DevelopmentRetirementPlanBinding.Matches(request, receipt, "null"));
    }
}
