using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleRestoreCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _file = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerRecycleRestoreCapabilityTests()
    {
        File.WriteAllBytes(_file, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
    }
    private string Sign(string method, string hash, DateTimeOffset? expiry = null, bool legacy = false)
    {
        var expires = (expiry ?? DateTimeOffset.UtcNow.AddSeconds(30)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var input = legacy ? $"{expires}.{nonce}.{method}" : ContainerRecycleRestoreBinding.Payload(expires, nonce, method, hash);
        return expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input)));
    }
    [Fact]
    public void FourIndependentUnaryMethodsPreserveCapabilityBoundary()
    {
        var methods = ContainerRecycleRestoreService.Descriptor.Methods;
        Assert.Equal<string>(["Apply", "Discard", "Inspect", "Plan"], methods.Select(m => m.Name).Order());
        Assert.All(methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
    }
    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void EachMethodRequiresContentBoundShortLivedSingleUseAuthority(string action)
    {
        var method = ContainerRecycleRestoreBinding.Service + action; var hash = ContainerRecycleRestoreBinding.Hash(ContainerRecycleRestorePolicyTests.Request());
        var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method));
        Assert.False(_validator.Validate(Sign(method, hash, legacy: true), method));
        Assert.False(_validator.ValidateContainerRecycleRestore(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerRecycleRestore(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(-1)), method, hash));
        Assert.False(_validator.ValidateContainerRecycleRestore(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(2)), method, hash));
        Assert.False(_validator.ValidateContainerDirectory(token, method, hash));
        Assert.False(_validator.ValidateContainerCopy(token, method, hash)); Assert.False(_validator.ValidateContainerMove(token, method, hash));
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleRestore(token, ContainerRecycleRestoreBinding.Service + (action == "Plan" ? "Apply" : "Plan"), hash));
        Assert.True(_validator.ValidateContainerRecycleRestore(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleRestore(token, method, hash));
    }
    [Theory]
    [InlineData("source-id")][InlineData("source-owner")][InlineData("source-path")][InlineData("source-bytes")][InlineData("source-attribute")][InlineData("history")][InlineData("private")][InlineData("retained")][InlineData("ctime")][InlineData("parent")][InlineData("fingerprint")][InlineData("operation")][InlineData("owner")][InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("key")]
    public void SignatureBindsEveryNestedSourceAndIndependentRestoreAuthority(string field)
    {
        var original=ContainerRecycleRestorePolicyTests.Request();var changed=original.Clone();
        switch(field){
            case "source-id":changed.Plan.Command.Source.OperationId="dd0a8865343e49bdb01a72680131bf39";break;
            case "source-owner":changed.Plan.Command.Source.Owner=new('F',64);break;
            case "source-path":changed.Plan.Command.Source.Plan.Command.Location.Path="work/other";break;
            case "source-bytes":changed.Plan.Command.Source.Plan.Sha256=new('4',64);break;
            case "source-attribute":changed.Plan.Command.Source.Plan.Attributes[0].Value=Google.Protobuf.ByteString.CopyFromUtf8("different");break;
            case "history":changed.Plan.Command.HistoryDigest=new('1',64);break;
            case "private":changed.Plan.PrivateIdentity.Inode++;break;case "retained":changed.Plan.RetainedIdentity.Inode++;break;case "ctime":changed.Plan.RetainedChangedAtUnixNs++;break;
            case "parent":changed.Plan.ParentVersion=new('2',64);break;case "fingerprint":changed.Plan.Fingerprint=new('3',64);break;
            case "operation":changed.OperationId="dd0a8865343e49bdb01a72680131bf39";break;case "owner":changed.Owner=new('F',64);break;
            case "actor":changed.Context.ActorSubject="other";break;case "trace":changed.Context.TraceId="other";break;
            case "job":changed.Context.JobId="dd0a8865343e49bdb01a72680131bf39";break;case "key":changed.Context.IdempotencyKey="dd0a8865343e49bdb01a72680131bf39";break;
        }
        var method=ContainerRecycleRestoreBinding.Service+"Apply";var hash=ContainerRecycleRestoreBinding.Hash(original);var token=Sign(method,hash);
        Assert.False(_validator.ValidateContainerRecycleRestore(token,method,ContainerRecycleRestoreBinding.Hash(changed)));Assert.True(_validator.ValidateContainerRecycleRestore(token,method,hash));
        Assert.NotEqual(ContainerRecycleBinding.Hash(original),hash);Assert.NotEqual(ContainerRecursiveAclRestoreBinding.Hash(original),hash);
    }
    public void Dispose() { File.Delete(_file); CryptographicOperations.ZeroMemory(_key); }
}
