using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class CapabilityValidatorTests : IDisposable
{
    private const string Method = "/whaledeck.agent.v1.HostService/GetHostInfo";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"whaledeck-capability-{Guid.NewGuid():N}");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly CapabilityValidator _validator;

    public CapabilityValidatorTests()
    {
        Directory.CreateDirectory(_directory);
        var keyFile = Path.Combine(_directory, "capability.key");
        File.WriteAllBytes(keyFile, _key);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:CapabilityKeyFile"] = keyFile
        }).Build();
        _validator = new CapabilityValidator(configuration);
    }

    [Fact]
    public void ValidCapabilityCanOnlyBeConsumedOnce()
    {
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unique-nonce-0001", Method);

        Assert.True(_validator.Validate(token, Method));
        Assert.False(_validator.Validate(token, Method));
    }

    [Fact]
    public void CapabilityLongerThanSixtySecondsIsRejected()
    {
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(90), "unique-nonce-0002", Method);

        Assert.False(_validator.Validate(token, Method));
    }

    [Fact]
    public void ExpiredOrWrongMethodCapabilityIsRejected()
    {
        var expired = Sign(DateTimeOffset.UtcNow.AddSeconds(-1), "unique-nonce-0003", Method);
        var wrongMethod = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "unique-nonce-0004", Method);

        Assert.False(_validator.Validate(expired, Method));
        Assert.False(_validator.Validate(wrongMethod, "/whaledeck.agent.v1.DockerService/ListContainers"));
    }

    private string Sign(DateTimeOffset expiresAt, string nonce, string method)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(_key);
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{expires}.{nonce}.{method}")));
        return $"{expires}.{nonce}.{signature}";
    }

    [Theory]
    [InlineData("Inspect")]
    [InlineData("Apply")]
    public void PrivateConfigurationMethodsRequireTheirOwnSingleUseCapability(string action)
    {
        var method = "/whaledeck.agent.v1.NotificationConfigurationService/" + action;
        Assert.False(_validator.Validate(null, method));
        Assert.False(_validator.Validate(Sign(DateTimeOffset.UtcNow.AddSeconds(45), "private-wrong-method", Method), method));
        var token = Sign(DateTimeOffset.UtcNow.AddSeconds(45), "private-configuration-nonce", method);
        Assert.True(_validator.Validate(token, method));
        Assert.False(_validator.Validate(token, method));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
