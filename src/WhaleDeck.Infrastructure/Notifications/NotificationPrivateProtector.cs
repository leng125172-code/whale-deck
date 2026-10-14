using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Notifications;

public sealed class NotificationPrivateProtector(IConfiguration configuration) : INotificationPrivateProtector
{
    private byte[] Key() => File.ReadAllBytes(configuration["Agent:CapabilityKeyFile"] ?? "/run/secrets/agent-capability.key");
    private string Mac(string domain, string value)
    {
        var key = Key();
        try { if (key.Length != 32) throw new InvalidOperationException("Private plan signing is unavailable."); return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(domain + "\n" + value))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public string Fingerprint(string owner, Guid channelId, string action, string input) => Mac("whaledeck-private-input-v1", owner + ":" + channelId.ToString("D") + ":" + action + ":" + input);
    public string Sign(NotificationPrivatePlanClaims claims)
    { var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims)); return payload + "." + Mac("whaledeck-private-plan-v1", payload); }
    public NotificationPrivatePlanClaims Verify(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan) || plan.Length > 4096) throw new ArgumentException("Invalid private plan.");
        var pieces = plan.Split('.');
        if (pieces.Length != 2 || pieces[1].Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(pieces[1]), Encoding.ASCII.GetBytes(Mac("whaledeck-private-plan-v1", pieces[0]))))
            throw new ArgumentException("Invalid private plan signature.");
        try
        {
            var claims = JsonSerializer.Deserialize<NotificationPrivatePlanClaims>(Convert.FromBase64String(pieces[0])) ?? throw new ArgumentException("Invalid private plan.");
            if (claims.ChannelId == Guid.Empty || claims.ChannelVersion is < 0 or >= long.MaxValue - 2 || string.IsNullOrWhiteSpace(claims.Actor) ||
                claims.Action is not ("apply" or "remove") || claims.InputFingerprint.Length != 64 || claims.Provider is not ("Webhook" or "Email" or "DingTalk" or "Feishu" or "WeCom")) throw new ArgumentException("Invalid private plan.");
            return claims;
        }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("Invalid private plan."); }
    }
}
