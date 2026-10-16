using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class TerminalGrantScopeTests
{
    [Fact]
    public void RealSignerSeparatesContainerInstanceShellOwnerAndLogin()
    {
        var path = Path.Combine(Path.GetTempPath(), "wd-terminal-signing-test-" + Guid.NewGuid().ToString("N"));
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(path, key);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = path }).Build();
            var signer = new TerminalGrantSigner(configuration); var id = Guid.NewGuid(); var binding = new string('A', 64); var target = new string('a', 64);
            var token = signer.Sign(id, "actor", binding, target, "10:instance", "sh");
            Assert.Equal(token, signer.Sign(id, "actor", binding, target, "10:instance", "sh"));
            Assert.NotEqual(token, signer.Sign(id, "actor", binding));
            Assert.NotEqual(token, signer.Sign(id, "other", binding, target, "10:instance", "sh"));
            Assert.NotEqual(token, signer.Sign(id, "actor", new('B', 64), target, "10:instance", "sh"));
            Assert.NotEqual(token, signer.Sign(id, "actor", binding, new('b', 64), "10:instance", "sh"));
            Assert.NotEqual(token, signer.Sign(id, "actor", binding, target, "11:instance", "sh"));
            Assert.NotEqual(token, signer.Sign(id, "actor", binding, target, "10:instance", "bash"));
        }
        finally { CryptographicOperations.ZeroMemory(key); File.Delete(path); }
    }
}
