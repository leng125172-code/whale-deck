using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Notifications;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationTransportTests
{
    [Theory]
    [InlineData("127.0.0.1")] [InlineData("::1")] [InlineData("::ffff:127.0.0.1")]
    [InlineData("169.254.169.254")] [InlineData("0.0.0.0")] [InlineData("::")]
    [InlineData("224.0.0.1")] [InlineData("ff02::1")] [InlineData("fe80::1")]
    [InlineData("64:ff9b::a9fe:a9fe")] [InlineData("2002:a9fe:a9fe::1")]
    public void MetadataLoopbackAndTransitionAddressesCannotBeApproved(string address)
    {
        var policy = new NotificationNetworkPolicy();
        Assert.False(policy.Allowed(IPAddress.Parse(address), false)); Assert.False(policy.Allowed(IPAddress.Parse(address), true));
    }

    [Theory]
    [InlineData("10.1.2.3")] [InlineData("172.20.0.1")] [InlineData("192.168.100.13")]
    [InlineData("fd00::1")] [InlineData("::ffff:192.168.22.19")]
    public void PrivateNetworkRequiresExplicitApproval(string address)
    {
        var policy = new NotificationNetworkPolicy();
        Assert.False(policy.Allowed(IPAddress.Parse(address), false)); Assert.True(policy.Allowed(IPAddress.Parse(address), true));
    }

    [Fact]
    public async Task RegistryVersionPinsOnlySelectedBindingAndNeverFormatsSecrets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wdv-notification-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "registry.json");
        try
        {
            var data = """
                {"version":1,"targets":{"approved":{"provider":"Webhook","url":"https://example.test/hook","allowedAddresses":["192.0.2.1"]}},"credentials":{"credential":{"signingSecret":"unit-secret-not-a-real-key"}}}
                """;
            await File.WriteAllTextAsync(path, data); Private(path);
            var reader = new PrivateNotificationConfiguration(path);
            var binding = await reader.ResolveAsync("Webhook", "approved", "credential", default);
            Assert.Matches("^[A-F0-9]{64}$", binding.Revision); Assert.DoesNotContain("unit-secret", binding.ToString());
            Assert.DoesNotContain("unit-secret", binding.Credential!.ToString()); Assert.DoesNotContain("example.test", binding.Target.ToString());
            await File.WriteAllTextAsync(path, data.Replace("unit-secret-not-a-real-key", "different-unit-secret-key", StringComparison.Ordinal)); Private(path);
            var rotated = await reader.ResolveAsync("Webhook", "approved", "credential", default);
            Assert.NotEqual(binding.Revision, rotated.Revision);
            await Assert.ThrowsAsync<NotificationConfigurationException>(() => reader.ResolveAsync("Email", "approved", "credential", default));
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1,\"targets\":{},\"credentials\":{}}")]
    [InlineData("{\"version\":2,\"targets\":{},\"credentials\":{}}")]
    [InlineData("{\"version\":1,\"targets\":{},\"credentials\":{},\"unknown\":true}")]
    [InlineData("{\"version\":1,\"targets\":null,\"credentials\":{}}")]
    [InlineData("{invalid-json")]
    public async Task MalformedRegistryFailsWithFixedRedactedCode(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "wdv-notification-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json); Private(path);
            var error = await Assert.ThrowsAsync<NotificationConfigurationException>(() => new PrivateNotificationConfiguration(path).ResolveAsync("Webhook", "approved", null, default));
            Assert.Equal("ConfigurationInvalid", error.Code); Assert.DoesNotContain(json, error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OversizeAndMissingFilesAreNotBindings()
    {
        var path = Path.Combine(Path.GetTempPath(), "wdv-notification-" + Guid.NewGuid().ToString("N") + ".json");
        var missing = await Assert.ThrowsAsync<NotificationConfigurationException>(() => new PrivateNotificationConfiguration(path).ResolveAsync("Webhook", "approved", null, default));
        Assert.Equal("ConfigurationMissing", missing.Code);
        try
        {
            await File.WriteAllTextAsync(path, new string('x', 65537)); Private(path);
            var invalid = await Assert.ThrowsAsync<NotificationConfigurationException>(() => new PrivateNotificationConfiguration(path).ResolveAsync("Webhook", "approved", null, default));
            Assert.Equal("ConfigurationInvalid", invalid.Code);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("DingTalk", "{\"errcode\":0}", NotificationTransportResult.Accepted)]
    [InlineData("WeCom", "{\"errcode\":40001,\"errmsg\":\"private-response\"}", NotificationTransportResult.Rejected)]
    [InlineData("Feishu", "{\"code\":0}", NotificationTransportResult.Accepted)]
    [InlineData("Feishu", "{\"msg\":\"private-response\"}", NotificationTransportResult.Unknown)]
    [InlineData("WeCom", "[]", NotificationTransportResult.Unknown)]
    [InlineData("DingTalk", "malformed", NotificationTransportResult.Unknown)]
    public void BotAcceptanceUsesProviderCodeNotHttp200(string provider, string json, NotificationTransportResult result) =>
        Assert.Equal(result, NotificationPayload.Result(provider, Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void SignedPayloadsUseDifferentDingTalkAndFeishuAlgorithmsWithoutPrivateActor()
    {
        const string secret = "unit-secret-not-a-real-key"; const long timestamp = 1700000000;
        Assert.Equal(Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("1700000000000\n" + secret))), NotificationPayload.DingTalkSignature("1700000000000", secret));
        Assert.Equal(Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes("1700000000\n" + secret), Array.Empty<byte>())), NotificationPayload.FeishuSignature(timestamp, secret));
        var item = new NotificationEvent { SourceKey = "not-rendered-source", SourceKind = "Alert", SourceId = Guid.NewGuid(), Audience = "Administrator", MessageCode = "ALERT_ACTIVE", Severity = "Error", OccurredAtUtc = DateTimeOffset.UtcNow };
        foreach (var provider in new[] { "Webhook", "DingTalk", "WeCom", "Feishu" })
        {
            var payload = NotificationPayload.Serialize(provider, Guid.NewGuid(), item, timestamp, secret);
            using var parsed = JsonDocument.Parse(payload); Assert.DoesNotContain(secret, Encoding.UTF8.GetString(payload));
            Assert.DoesNotContain(item.SourceKey, Encoding.UTF8.GetString(payload));
        }
    }
    private static void Private(string path)
    { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
}
