using System.Text.Json.Nodes;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ApplicationConfigurationSnapshotTests
{
    [Fact]
    public void SnapshotIncludesParametersAndNamesButNotExistingSecretsOrCommands()
    {
        var model = JsonNode.Parse("""
            {"name":"whaledeck-app-demo","services":{"app":{"image":"nginx:alpine","cpus":1.5,"mem_limit":268435456,
              "environment":{"PASSWORD":"never-display-this"},"command":["secret-command"],"entrypoint":["secret-entrypoint"],
              "ports":[{"host_ip":"::1","published":"18088","target":80,"protocol":"tcp"}],
              "networks":{"default":null}}},"networks":{"default":{"name":"whaledeck-app-demo_default"}}}
            """)!.AsObject();
        var response = ApplicationConfigurationManager.Describe(model, JsonNode.Parse("{\"dependencies\":[]}")!.AsObject(),
            "OTHER='private-value'\n", "application:demo", new string('a', 64), "/approved/demo", ["bridge"]);
        var json = Google.Protobuf.JsonFormatter.Default.Format(response);
        Assert.Contains("PASSWORD", json);
        Assert.Contains("OTHER", json);
        Assert.DoesNotContain("never-display-this", json);
        Assert.DoesNotContain("private-value", json);
        Assert.DoesNotContain("secret-command", json);
        Assert.DoesNotContain("secret-entrypoint", json);
        Assert.Equal(1.5, response.Services[0].Cpus);
        Assert.Equal(256, response.Services[0].MemoryMb);
        Assert.Equal("[::1]:18088:80/tcp", response.Services[0].Ports[0]);
        Assert.True(response.Services[0].CommandPresent);
    }

    [Theory]
    [InlineData(8080, "127.0.0.1", "tcp")]
    [InlineData(18080, "0.0.0.0", "tcp")]
    [InlineData(18088, "0.0.0.0", "tcp")]
    [InlineData(18088, "127.0.0.1", "tcp")]
    public void ReservedOrConflictingHostPortsAreRejectedBeforeSave(int port, string address, string protocol)
    {
        var model = new JsonObject { ["services"] = new JsonObject { ["app"] = new JsonObject
        { ["ports"] = new JsonArray(new JsonObject { ["published"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture), ["host_ip"] = address, ["protocol"] = protocol }) } } };
        Assert.Throws<InvalidOperationException>(() => ApplicationConfigurationManager.ValidatePublishedPorts(model,
            [("127.0.0.1", 18088, "tcp")]));
    }

    [Fact]
    public void SamePortOnDifferentSpecificAddressesOrProtocolsIsAllowed()
    {
        var model = JsonNode.Parse("""
            {"services":{"app":{"ports":[{"published":"18088","host_ip":"192.168.1.2","protocol":"udp"}]}}}
            """)!.AsObject();
        ApplicationConfigurationManager.ValidatePublishedPorts(model, [("192.168.1.2", 18088, "tcp"), ("192.168.1.3", 18088, "udp")]);
    }

    [Fact]
    public void UninstallRetainsFilesWithoutKeepingTheOldInstallationDiscoverable()
    {
        var root = Path.Combine(Path.GetTempPath(), "whaledeck-retention-test-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(app, "data"));
        try
        {
            foreach (var file in new[] { "compose.yml", ".env", "manifest.json" }) File.WriteAllText(Path.Combine(app, file), "test-only");
            File.WriteAllText(Path.Combine(app, "data", "keep.txt"), "retained");
            var target = ManagedActionExecutor.RetainUninstalledApplication(root, "demo");
            Assert.False(Directory.Exists(app));
            Assert.Equal("retained", File.ReadAllText(Path.Combine(target, "data", "keep.txt")));
            Assert.StartsWith(Path.Combine(root, ".retained") + Path.DirectorySeparatorChar, target);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
