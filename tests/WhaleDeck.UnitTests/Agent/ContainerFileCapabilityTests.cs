using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerFileCapabilityTests
{
    [Theory]
    [InlineData("InspectTarget")][InlineData("List")][InlineData("Inspect")][InlineData("Download")]
    public void EachContainerReadMethodRequiresItsOwnUnreplayedShortCapability(string name)
    {
        const string service = "/whaledeck.agent.v1.ContainerFileService/";
        var path = Path.GetTempFileName();
        var key = RandomNumberGenerator.GetBytes(32); // Synthetic fixture, never the installed Agent key.
        try
        {
            File.WriteAllBytes(path, key);
            var validator = new CapabilityValidator(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Agent:CapabilityKeyFile"] = path }).Build());
            var nonce = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds() + "." + Guid.NewGuid().ToString("N");
            var token = nonce + "." + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(nonce + "." + service + name)));
            Assert.False(validator.Validate(null, service + name));
            Assert.False(validator.Validate(token, "/whaledeck.agent.v1.FileTransferService/" + name));
            Assert.True(validator.Validate(token, service + name));
            Assert.False(validator.Validate(token, service + name));
            Assert.False(validator.Validate(token, service + (name == "List" ? "Download" : "List")));
        }
        finally { CryptographicOperations.ZeroMemory(key); File.Delete(path); }
    }
}
