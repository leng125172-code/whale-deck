using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ApplicationConfigurationPatchTests
{
    private static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "whaledeck-patch-fixture", "demo");
    private static ResourceRegistry Registry() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Agent:ResourceRegistryFile"] = "missing-configuration-test-registry.json" }).Build());
    private static JsonObject Model() => JsonNode.Parse("""
        {"name":"whaledeck-app-demo","x-custom":{"preserve":true},"services":{"app":{
          "image":"nginx:alpine","restart":"unless-stopped","env_file":[".env"],
          "labels":{"custom":"retain"},"healthcheck":{"test":["CMD","health"]},
          "environment":{"INLINE":"must-not-leak"},"deploy":{"resources":{"limits":{"cpus":0.5,"memory":123}}}
        }}}
        """)!.AsObject();
    private static JsonObject Manifest() => JsonNode.Parse("{\"slug\":\"demo\",\"image\":\"nginx:alpine\",\"custom\":\"retained\"}")!.AsObject();
    private static ApplicationConfigurationCandidate Build(string input, JsonObject? source = null) =>
        ApplicationConfigurationPatch.Build(source ?? Model(), "KEY=old\n", Manifest(), JsonNode.Parse(input)!.AsObject(),
            DirectoryPath, Registry(), new HashSet<string>(["ordinary-net"], StringComparer.Ordinal));

    [Fact]
    public void ParameterOverlayPreservesUnrelatedSettingsAndDoesNotModifyItsSource()
    {
        var source = Model();
        var result = Build("""
            {"services":{"app":{"cpus":1.5,"memoryMb":256,"restartPolicy":"on-failure",
              "ports":["127.0.0.1:18088:80/tcp","[::1]:18089:81/udp"],"command":["new","argument"]}},
              "environment":{"set":{"INLINE":"new-secret","KEY":""},"remove":[]},"autoUpdate":true,"apply":true}
            """, source);
        Assert.True(result.ApplyImmediately);
        Assert.Equal(1.5, result.Model["services"]!["app"]!["cpus"]!.GetValue<double>());
        Assert.Equal(256 * 1024 * 1024, result.Model["services"]!["app"]!["deploy"]!["resources"]!["limits"]!["memory"]!.GetValue<long>());
        Assert.Equal("retain", result.Model["services"]!["app"]!["labels"]!["custom"]!.GetValue<string>());
        Assert.NotNull(result.Model["services"]!["app"]!["healthcheck"]);
        Assert.True(result.Model["x-custom"]!["preserve"]!.GetValue<bool>());
        Assert.Equal("must-not-leak", source["services"]!["app"]!["environment"]!["INLINE"]!.GetValue<string>());
        Assert.Null(result.Model["services"]!["app"]!["environment"]!["INLINE"]);
        Assert.Contains("INLINE=\"new-secret\"", result.Environment);
        Assert.DoesNotContain("new-secret", string.Join('\n', result.Changes));
        Assert.Equal("retained", result.Manifest["custom"]!.GetValue<string>());
    }

    [Fact]
    public void NetworksAndOwnedVolumesUseSafeDefinitionsWithoutReplacingUnknownSettings()
    {
        var result = Build("""
            {"services":{"app":{"networks":["ordinary-net"],"volumes":[
              {"type":"volume","source":"whaledeck-app-demo-data","target":"/data","readOnly":false},
              {"type":"tmpfs","target":"/tmp","readOnly":false}]}}}
            """);
        Assert.Equal("whaledeck-app-demo-data", result.Model["volumes"]!["whaledeck-app-demo-data"]!["name"]!.GetValue<string>());
        Assert.Equal("ordinary-net", result.Model["networks"]!.AsObject().Single().Value!["name"]!.GetValue<string>());
        Assert.True(result.Model["networks"]!.AsObject().Single().Value!["external"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{\"services\":{\"app\":{\"privileged\":true}}}")]
    [InlineData("{\"services\":{\"missing\":{\"cpus\":1}}}")]
    [InlineData("{\"services\":{\"app\":{\"networkMode\":\"host\"}}}")]
    [InlineData("{\"services\":{\"app\":{\"networks\":[\"whaledeck-backend\"]}}}")]
    [InlineData("{\"services\":{\"app\":{\"ports\":[\"0.0.0.0:8080:80\"]}}}")]
    [InlineData("{\"services\":{\"app\":{\"ports\":[\"999.1.1.1:8888:80\"]}}}")]
    [InlineData("{\"services\":{\"app\":{\"memoryMb\":-1}}}")]
    [InlineData("{\"services\":{\"app\":{\"volumes\":[{\"type\":\"volume\",\"source\":\"database-platform-secret\",\"target\":\"/data\"}]}}}")]
    [InlineData("{\"services\":{\"app\":{\"volumes\":[{\"type\":\"bind\",\"source\":\"/var/run/docker.sock\",\"target\":\"/socket\"}]}}}")]
    [InlineData("{\"dependencies\":[\"demo\"]}")]
    public void UnsafeOrUnregisteredParametersFailWithoutRevealingCandidateValues(string input) =>
        Assert.Throws<InvalidOperationException>(() => Build(input));

    [Fact]
    public void AlreadyUnsafeModelIsNotSilentlyBlessedByUnrelatedEdits()
    {
        var model = Model();
        model["services"]!["app"]!["privileged"] = true;
        Assert.Throws<InvalidOperationException>(() => Build("{\"services\":{\"app\":{\"cpus\":1}}}", model));
    }

    [Fact]
    public void ClearingCommandOverridesInheritsImageAndZeroLimitsRemoveConflictingDeployLimits()
    {
        var result = Build("{\"services\":{\"app\":{\"command\":null,\"entrypoint\":null,\"cpus\":0,\"memoryMb\":0}}}");
        Assert.Null(result.Model["services"]!["app"]!["command"]);
        Assert.Null(result.Model["services"]!["app"]!["cpus"]);
        Assert.Null(result.Model["services"]!["app"]!["deploy"]!["resources"]!["limits"]!["cpus"]);
        Assert.Null(result.Model["services"]!["app"]!["deploy"]!["resources"]!["limits"]!["memory"]);
    }
}
