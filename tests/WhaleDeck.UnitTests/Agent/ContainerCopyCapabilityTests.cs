using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerCopyCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _path = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerCopyCapabilityTests()
    {
        File.WriteAllBytes(_path, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _path }).Build());
    }
    private string Sign(string method, string hash, int seconds = 30, bool legacy = false)
    {
        var expiry = DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var payload = legacy ? $"{expiry}.{nonce}.{method}" : ContainerCopyBinding.Payload(expiry, nonce, method, hash);
        return expiry + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    }
    [Fact]
    public void FourUnaryMethodsAndTypedOneofAvoidUnboundedManifestOrOpaqueJson()
    {
        var methods = ContainerCopyService.Descriptor.Methods;
        Assert.Equal<string>(["Apply", "Discard", "Inspect", "Plan"], methods.Select(m => m.Name).Order());
        Assert.All(methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
        Assert.Equal(2, ContainerCopyPlan.Descriptor.Oneofs.Single().Fields.Count);
        Assert.DoesNotContain(ContainerCopyPlan.Descriptor.Fields.InFieldNumberOrder(), f => f.Name.Contains("json", StringComparison.Ordinal));
    }
    [Theory][InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void CompleteRequestShortExpiryDomainMethodAndSingleUseAreRequired(string action)
    {
        var method = ContainerCopyBinding.Service + action; var hash = ContainerCopyBinding.Hash(ContainerCopyPolicyTests.Request()); var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method));
        Assert.False(_validator.ValidateContainerCopy(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerCopy(Sign(method, hash, -60), method, hash));
        Assert.False(_validator.ValidateContainerCopy(Sign(method, hash, 120), method, hash));
        Assert.False(_validator.ValidateContainerDirectory(token, method, hash)); Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerCopy(token, ContainerCopyBinding.Service + "Other", hash));
        Assert.False(_validator.ValidateContainerCopy(token, ContainerCopyBinding.Service + (action == "Plan" ? "Apply" : "Plan"), hash));
        Assert.True(_validator.ValidateContainerCopy(token, method, hash)); Assert.False(_validator.ValidateContainerCopy(token, method, hash));
    }
    [Theory]
    [InlineData("source")][InlineData("target")][InlineData("container")][InlineData("instance")][InlineData("uid")][InlineData("gid")]
    [InlineData("ctime")][InlineData("parent")][InlineData("proof")][InlineData("fingerprint")][InlineData("inner")][InlineData("operation")]
    [InlineData("owner")][InlineData("actor")][InlineData("job")][InlineData("key")][InlineData("trace")]
    public void AnyAuthorityOrFrozenPlanMutationInvalidatesTheCapability(string field)
    {
        var original = ContainerCopyPolicyTests.Request(); var changed = original.Clone();
        switch (field)
        {
            case "source": changed.Plan.Command.Source.Path += "x"; break;
            case "target": changed.Plan.Command.TargetPath += "x"; break;
            case "container": changed.Plan.Command.Source.ContainerId = new('b', 64); break;
            case "instance": changed.Plan.Command.Source.InstanceId = "888:2026-10-28T01:02:03Z"; break;
            case "uid": changed.Plan.OwnerUid++; break;
            case "gid": changed.Plan.OwnerGid++; break;
            case "ctime": changed.Plan.SourceChangedAtUnixNs++; break;
            case "parent": changed.Plan.TargetParent.Inode++; break;
            case "proof": changed.Plan.TargetProof = new('E', 64); break;
            case "fingerprint": changed.Plan.Fingerprint = new('E', 64); break;
            case "inner": changed.Plan.FilePlan.Sha256 = new('F', 64); break;
            case "operation": changed.OperationId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "owner": changed.Owner = new('E', 64); break;
            case "actor": changed.Context.ActorSubject = "other"; break;
            case "job": changed.Context.JobId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "key": changed.Context.IdempotencyKey = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "trace": changed.Context.TraceId = "other"; break;
        }
        var method = ContainerCopyBinding.Service + "Apply"; var hash = ContainerCopyBinding.Hash(original); var token = Sign(method, hash);
        Assert.False(_validator.ValidateContainerCopy(token, method, ContainerCopyBinding.Hash(changed)));
        Assert.True(_validator.ValidateContainerCopy(token, method, hash));
        Assert.NotEqual(hash, ContainerDirectoryBinding.Hash(original)); Assert.NotEqual(hash, ContainerFileWriteBinding.Hash(original));
    }
    public void Dispose() { File.Delete(_path); CryptographicOperations.ZeroMemory(_key); }
}
