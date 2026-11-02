using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerAclCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _file = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerAclCapabilityTests()
    {
        File.WriteAllBytes(_file, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
    }
    private string Sign(string method, string hash, DateTimeOffset? expiry = null, bool legacy = false)
    {
        var expires = (expiry ?? DateTimeOffset.UtcNow.AddSeconds(30)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var input = legacy ? $"{expires}.{nonce}.{method}" : ContainerAclBinding.Payload(expires, nonce, method, hash);
        return expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input)));
    }
    [Fact]
    public void FourIndependentUnaryMethodsPreserveCapabilityBoundary()
    {
        var methods = ContainerAclService.Descriptor.Methods;
        Assert.Equal<string>(["Apply", "Discard", "Inspect", "Plan"], methods.Select(m => m.Name).Order());
        Assert.All(methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
    }
    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void EachMethodRequiresContentBoundShortLivedSingleUseAuthority(string action)
    {
        var method = ContainerAclBinding.Service + action; var hash = ContainerAclBinding.Hash(ContainerAclPolicyTests.Request());
        var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method));
        Assert.False(_validator.Validate(Sign(method, hash, legacy: true), method));
        Assert.False(_validator.ValidateContainerAcl(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerAcl(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(-1)), method, hash));
        Assert.False(_validator.ValidateContainerAcl(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(2)), method, hash));
        Assert.False(_validator.ValidateContainerDirectory(token, method, hash));
        Assert.False(_validator.ValidateContainerPermission(token, method, hash));
        Assert.False(_validator.ValidateContainerOwnership(token, method, hash)); Assert.False(_validator.ValidateContainerRecursiveOwnership(token, method, hash));
        Assert.False(_validator.ValidateContainerCopy(token, method, hash)); Assert.False(_validator.ValidateContainerMove(token, method, hash));
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, hash));
        Assert.False(_validator.ValidateContainerAcl(token, ContainerAclBinding.Service + (action == "Plan" ? "Apply" : "Plan"), hash));
        Assert.True(_validator.ValidateContainerAcl(token, method, hash));
        Assert.False(_validator.ValidateContainerAcl(token, method, hash));
    }
    [Theory]
    [InlineData("acl-id")][InlineData("acl-permissions")][InlineData("path")][InlineData("container")][InlineData("instance")][InlineData("uid")][InlineData("gid")]
    [InlineData("parent")][InlineData("proof")][InlineData("version")][InlineData("fingerprint")][InlineData("operation")][InlineData("owner")]
    [InlineData("expectedVersion")][InlineData("kind")][InlineData("source")][InlineData("changedAt")][InlineData("attributes")][InlineData("noChange")]
    [InlineData("acl-mask")][InlineData("acl-presence")][InlineData("acl-type")][InlineData("acl-source")][InlineData("acl-default")][InlineData("acl-mode")]
    [InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("key")]
    public void SignatureBindsEveryAuthorityAndDestinationField(string field)
    {
        var original = ContainerAclPolicyTests.Request(); var changed = original.Clone();
        switch (field)
        {
            case "acl-mask": changed.Plan.Command.Entries[3].Permissions++; break;
            case "acl-presence": changed.Plan.Command.Entries[1].ClearId(); break;
            case "acl-type": changed.Plan.Command.AclType="default"; break;
            case "acl-source": changed.Plan.AccessAcl[0].Permissions++; break;
            case "acl-default": changed.Plan.DefaultAcl.Add(ContainerAclPolicyTests.Entries()); break;
            case "acl-mode": changed.Plan.TargetMode++; break;
            case "expectedVersion": changed.Plan.Command.ExpectedVersion = new('E', 64); break;
            case "kind": changed.Plan.Kind = "Directory"; break;
            case "source": changed.Plan.SourceIdentity.SizeBytes++; break;
            case "changedAt": changed.Plan.SourceChangedAtUnixNs++; break;
            case "attributes": changed.Plan.AttributesDigest = new('E', 64); break;
            case "noChange": changed.Plan.NoChange = true; break;
            case "acl-id": changed.Plan.Command.Entries[1].Id++; break;
            case "acl-permissions": changed.Plan.Command.Entries[1].Permissions++; break;
            case "path": changed.Plan.Command.Location.Path = "work/elsewhere"; break;
            case "container": changed.Plan.Command.Location.ContainerId = new('b', 64); break;
            case "instance": changed.Plan.Command.Location.InstanceId = "888:2026-10-27T01:02:03Z"; break;
            case "uid": changed.Plan.OwnerUid++; break;
            case "gid": changed.Plan.OwnerGid++; break;
            case "parent": changed.Plan.ParentIdentity.Inode++; break;
            case "proof": changed.Plan.TargetProof = new('E', 64); break;
            case "version": changed.Plan.ParentVersion = new('E', 64); break;
            case "fingerprint": changed.Plan.Fingerprint = new('E', 64); break;
            case "operation": changed.OperationId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "owner": changed.Owner = new('E', 64); break;
            case "actor": changed.Context.ActorSubject = "other"; break;
            case "trace": changed.Context.TraceId = "other"; break;
            case "job": changed.Context.JobId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "key": changed.Context.IdempotencyKey = "cc0a8865343e49bdb01a72680131bf39"; break;
        }
        var method = ContainerAclBinding.Service + "Apply"; var hash = ContainerAclBinding.Hash(original); var token = Sign(method, hash);
        Assert.False(_validator.ValidateContainerAcl(token, method, ContainerAclBinding.Hash(changed)));
        Assert.True(_validator.ValidateContainerAcl(token, method, hash));
        Assert.NotEqual(ContainerFileWriteBinding.Hash(original), hash); Assert.NotEqual(ContainerHistoryBinding.Hash(original), hash);
    }
    public void Dispose() { File.Delete(_file); CryptographicOperations.ZeroMemory(_key); }
}
