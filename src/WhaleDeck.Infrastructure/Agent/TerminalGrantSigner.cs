using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Agent;

public sealed class TerminalGrantSigner(IConfiguration configuration) : ITerminalGrantSigner
{
    public string Sign(Guid sessionId, string actorSubject, string loginBinding, string targetId = "whaledeck.host", string? instanceId = null, string? shell = null)
    {
        var key = File.ReadAllBytes(configuration["Agent:CapabilityKeyFile"] ?? "/run/secrets/agent-capability.key");
        try
        {
            if (key.Length < 32) throw new InvalidOperationException("TERMINAL_KEY_UNAVAILABLE");
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new { purpose = "WhaleDeck.Terminal.OneTimeGrant.v2", sessionId, actorSubject, loginBinding, targetId, instanceId, shell }))));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
