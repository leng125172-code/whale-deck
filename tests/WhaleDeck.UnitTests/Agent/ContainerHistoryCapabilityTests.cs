using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerHistoryCapabilityTests : IDisposable
{
    private static readonly string[] Actions = ["List", "ReadSource"];
    private readonly string _keyFile = Path.GetTempFileName();
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly CapabilityValidator _validator;

    public ContainerHistoryCapabilityTests()
    {
        File.WriteAllBytes(_keyFile, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Agent:CapabilityKeyFile"] = _keyFile }).Build());
    }

    [Fact]
    public void ProtocolExposesOnlyListAndServerStreamingReadSource()
    {
        var methods = ContainerHistoryService.Descriptor.Methods;
        Assert.Equal(Actions.Order(), methods.Select(method => method.Name).Order());
        foreach (var method in methods)
        {
            Assert.False(method.IsClientStreaming);
            Assert.Equal(method.Name == "ReadSource", method.IsServerStreaming);
        }
    }

    [Theory]
    [InlineData("List")]
    [InlineData("ReadSource")]
    public void EachMethodRequiresItsOwnContentBoundSingleUseSignature(string action)
    {
        var method = ContainerHistoryBinding.Service + action;
        var hash = ContainerHistoryBinding.Hash(Request(action));
        var token = SignHistory(method, hash);
        Assert.False(_validator.ValidateContainerHistory(null, method, hash));
        Assert.False(_validator.Validate(token, method));
        foreach (var other in Actions.Where(candidate => candidate != action))
            Assert.False(_validator.ValidateContainerHistory(token, ContainerHistoryBinding.Service + other, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, new string('B', 64)));
        Assert.True(_validator.ValidateContainerHistory(token, method, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, hash));
    }

    [Fact]
    public void InvalidHistoryAttemptsDoNotConsumeTheOriginalValidNonce()
    {
        var method = ContainerHistoryBinding.Service + "List";
        var hash = ContainerHistoryBinding.Hash(ListRequest());

        Assert.False(_validator.ValidateContainerHistory(null, method, hash));

        var expires = DateTimeOffset.UtcNow.AddSeconds(45);
        var nonce = Nonce();
        var valid = SignHistory(method, hash, expires, nonce);
        foreach (var invalidHash in new string?[] { null, "", "ABC", hash.ToLowerInvariant(), new('A', 63) })
            Assert.False(_validator.ValidateContainerHistory(valid, method, invalidHash));
        Assert.True(_validator.ValidateContainerHistory(valid, method, hash));

        expires = DateTimeOffset.UtcNow.AddSeconds(45);
        nonce = Nonce();
        valid = SignHistory(method, hash, expires, nonce);
        Assert.False(_validator.ValidateContainerHistory(valid, ContainerHistoryBinding.Service + "ReadSource", hash));
        Assert.True(_validator.ValidateContainerHistory(valid, method, hash));

        expires = DateTimeOffset.UtcNow.AddSeconds(45);
        nonce = Nonce();
        valid = SignHistory(method, hash, expires, nonce);
        Assert.False(_validator.ValidateContainerHistory(valid, method, new('B', 64)));
        Assert.True(_validator.ValidateContainerHistory(valid, method, hash));

        expires = DateTimeOffset.UtcNow.AddSeconds(45);
        nonce = Nonce();
        Assert.False(_validator.ValidateContainerHistory(LegacySign(method, expires, nonce), method, hash));
        Assert.True(_validator.ValidateContainerHistory(SignHistory(method, hash, expires, nonce), method, hash));

        expires = DateTimeOffset.UtcNow.AddSeconds(45);
        nonce = Nonce();
        Assert.False(_validator.ValidateContainerHistory(SignUploadDomain(method, hash, expires, nonce), method, hash));
        Assert.True(_validator.ValidateContainerHistory(SignHistory(method, hash, expires, nonce), method, hash));

        expires = DateTimeOffset.UtcNow.AddSeconds(45);
        nonce = Nonce();
        var unknown = ContainerHistoryBinding.Service + "Unknown";
        Assert.False(_validator.ValidateContainerHistory(SignHistory(unknown, hash, expires, nonce), unknown, hash));
        Assert.True(_validator.ValidateContainerHistory(SignHistory(method, hash, expires, nonce), method, hash));

        nonce = Nonce();
        Assert.False(_validator.ValidateContainerHistory(
            SignHistory(method, hash, DateTimeOffset.UtcNow.AddSeconds(-1), nonce), method, hash));
        Assert.True(_validator.ValidateContainerHistory(
            SignHistory(method, hash, DateTimeOffset.UtcNow.AddSeconds(45), nonce), method, hash));

        nonce = Nonce();
        Assert.False(_validator.ValidateContainerHistory(
            SignHistory(method, hash, DateTimeOffset.UtcNow.AddSeconds(120), nonce), method, hash));
        Assert.True(_validator.ValidateContainerHistory(
            SignHistory(method, hash, DateTimeOffset.UtcNow.AddSeconds(45), nonce), method, hash));
    }

    [Fact]
    public void HistoryAuthorityCannotCrossIntoUploadHostOrOtherServices()
    {
        var historyMethod = ContainerHistoryBinding.Service + "List";
        var historyHash = ContainerHistoryBinding.Hash(ListRequest());
        var token = SignHistory(historyMethod, historyHash);

        Assert.False(_validator.Validate(token, historyMethod));
        Assert.False(_validator.ValidateContainerWrite(
            token, ContainerFileWriteBinding.Service + "InspectTarget", historyHash));
        Assert.False(_validator.Validate(token, "/whaledeck.agent.v1.HostService/GetInfo"));
        Assert.False(_validator.Validate(token, "/whaledeck.agent.v1.FileTreeRestoringService/Apply"));
        Assert.True(_validator.ValidateContainerHistory(token, historyMethod, historyHash));
    }

    [Fact]
    public void KnownRequestsUseDistinctMessageAndAuthorityDomains()
    {
        var list = ListRequest();
        var source = SourceRequest();
        Assert.NotEqual(ContainerHistoryBinding.Hash(list), ContainerHistoryBinding.Hash(source));
        Assert.NotEqual(ContainerHistoryBinding.Hash(list), ContainerFileWriteBinding.Hash(list));
        Assert.Equal(ContainerHistoryBinding.Hash(list), ContainerHistoryBinding.Hash(list.Clone()));
        Assert.Equal(ContainerHistoryBinding.Hash(source), ContainerHistoryBinding.Hash(source.Clone()));
    }

    private string SignHistory(string method, string hash, DateTimeOffset? expiration = null, string? nonce = null) =>
        Sign(ContainerHistoryBinding.Payload, method, hash, expiration ?? DateTimeOffset.UtcNow.AddSeconds(45), nonce ?? Nonce());

    private string SignUploadDomain(string method, string hash, DateTimeOffset expiration, string nonce) =>
        Sign(ContainerFileWriteBinding.Payload, method, hash, expiration, nonce);

    private string Sign(Func<string, string, string, string, string> payload, string method, string hash,
        DateTimeOffset expiration, string nonce)
    {
        var expires = expiration.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload(expires, nonce, method, hash)));
        return expires + "." + nonce + "." + Convert.ToHexString(mac);
    }

    private string LegacySign(string method, DateTimeOffset expiration, string nonce)
    {
        var prefix = expiration.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "." + nonce;
        return prefix + "." + Convert.ToHexString(HMACSHA256.HashData(
            _key, Encoding.UTF8.GetBytes(prefix + "." + method)));
    }

    private static string Nonce() => Guid.NewGuid().ToString("N");

    private static ContainerFileLocation Location(string path) => new()
    {
        ContainerId = new('a', 64),
        InstanceId = "777:2026-10-22T01:02:03.123456789Z",
        Path = path
    };

    private static ContainerHistoryListRequest ListRequest() => new()
    {
        Location = Location("work"),
        After = "",
        Take = 10,
        ExpectedVersion = ""
    };

    private static ContainerHistorySourceRequest SourceRequest() => new()
    {
        Location = Location("work/中文.txt"),
        CaptureId = new('a', 32),
        ExpectedCaptureHash = new('A', 64)
    };

    private static IMessage Request(string action) => action switch
    {
        "List" => ListRequest(),
        "ReadSource" => SourceRequest(),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        File.Delete(_keyFile);
    }
}
