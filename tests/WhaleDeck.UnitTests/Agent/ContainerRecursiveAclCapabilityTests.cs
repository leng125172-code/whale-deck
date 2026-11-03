using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecursiveAclCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _file = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerRecursiveAclCapabilityTests()
    {
        File.WriteAllBytes(_file, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
    }
    private string Sign(string method, string hash, DateTimeOffset? expiry = null, bool legacy = false)
    {
        var expires = (expiry ?? DateTimeOffset.UtcNow.AddSeconds(30)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var input = legacy ? $"{expires}.{nonce}.{method}" : ContainerRecursiveAclBinding.Payload(expires, nonce, method, hash);
        return expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input)));
    }
    [Fact]
    public void FourIndependentUnaryMethodsPreserveCapabilityBoundary()
    {
        var methods = ContainerRecursiveAclService.Descriptor.Methods;
        Assert.Equal<string>(["Apply", "Discard", "Inspect", "Plan"], methods.Select(m => m.Name).Order());
        Assert.All(methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
    }
    [Theory]
    [InlineData("Plan")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void EachMethodRequiresContentBoundShortLivedSingleUseAuthority(string action)
    {
        var method = ContainerRecursiveAclBinding.Service + action; var hash = ContainerRecursiveAclBinding.Hash(ContainerRecursiveAclPolicyTests.Request());
        var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method));
        Assert.False(_validator.Validate(Sign(method, hash, legacy: true), method));
        Assert.False(_validator.ValidateContainerRecursiveAcl(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerRecursiveAcl(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(-1)), method, hash));
        Assert.False(_validator.ValidateContainerRecursiveAcl(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(2)), method, hash));
        Assert.False(_validator.ValidateContainerPermission(token, method, hash)); Assert.False(_validator.ValidateContainerOwnership(token, method, hash));
        Assert.False(_validator.ValidateContainerDirectory(token, method, hash));
        Assert.False(_validator.ValidateContainerRecursivePermission(token, method, hash));
        Assert.False(_validator.ValidateContainerRecursiveOwnership(token, method, hash));Assert.False(_validator.ValidateContainerAcl(token,method,hash));
        Assert.False(_validator.ValidateContainerCopy(token, method, hash)); Assert.False(_validator.ValidateContainerMove(token, method, hash));
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, hash));
        Assert.False(_validator.ValidateContainerRecursiveAcl(token, ContainerRecursiveAclBinding.Service + (action == "Plan" ? "Apply" : "Plan"), hash));
        Assert.True(_validator.ValidateContainerRecursiveAcl(token, method, hash));
        Assert.False(_validator.ValidateContainerRecursiveAcl(token, method, hash));
    }
    [Theory]
    [InlineData("file-entry")][InlineData("directory-entry")][InlineData("acl-type")][InlineData("file-presence")][InlineData("directory-presence")][InlineData("manifest")][InlineData("entries")][InlineData("files")][InlineData("directories")][InlineData("changes")]
    [InlineData("root")][InlineData("ctime")][InlineData("path")][InlineData("instance")][InlineData("container")][InlineData("parent")][InlineData("parentVersion")][InlineData("proof")][InlineData("fingerprint")]
    [InlineData("operation")][InlineData("owner")][InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("key")][InlineData("uid")][InlineData("gid")][InlineData("expectedVersion")]
    public void FullManifestAuthorityAndOriginalExecutionAreContentBound(string field)
    {
        var original = ContainerRecursiveAclPolicyTests.Request(); var changed = original.Clone();
        switch (field)
        {
            case "file-entry": changed.Plan.Command.FileEntries.Entries[1].Permissions=0;break;
            case "acl-type":changed.Plan.Command.AclType="default";break;
            case "file-presence":changed.Plan.Command.FileEntries=null;break;
            case "directory-presence":changed.Plan.Command.DirectoryEntries=null;break;
            case "directory-entry": changed.Plan.Command.DirectoryEntries.Entries[1].Id=0;break;
            case "manifest": changed.Plan.ManifestDigest = new('E', 64); break;
            case "entries": changed.Plan.EntryCount++; break;
            case "files": changed.Plan.FileCount++; break;
            case "directories": changed.Plan.DirectoryCount++; break;
            case "changes": changed.Plan.ChangeCount++; break;
            case "root": changed.Plan.RootIdentity.Inode++; break;
            case "ctime": changed.Plan.RootChangedAtUnixNs++; break;
            case "path": changed.Plan.Command.Location.Path = "work/other"; break;
            case "instance": changed.Plan.Command.Location.InstanceId = "888:2026-10-27T01:02:03Z"; break;
            case "container": changed.Plan.Command.Location.ContainerId = new('b', 64); break;
            case "parent": changed.Plan.ParentIdentity.Inode++; break;
            case "parentVersion": changed.Plan.ParentVersion = new('E', 64); break;
            case "proof": changed.Plan.TargetProof = new('E', 64); break;
            case "fingerprint": changed.Plan.Fingerprint = new('E', 64); break;
            case "operation": changed.OperationId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "owner": changed.Owner = new('E', 64); break;
            case "actor": changed.Context.ActorSubject = "other"; break;
            case "trace": changed.Context.TraceId = "other"; break;
            case "job": changed.Context.JobId = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "key": changed.Context.IdempotencyKey = "cc0a8865343e49bdb01a72680131bf39"; break;
            case "uid": changed.Plan.OwnerUid++; break;
            case "gid": changed.Plan.OwnerGid++; break;
            case "expectedVersion": changed.Plan.Command.ExpectedVersion = new('E', 64); break;
        }
        var method = ContainerRecursiveAclBinding.Service + "Apply"; var hash = ContainerRecursiveAclBinding.Hash(original); var token = Sign(method, hash);
        Assert.False(_validator.ValidateContainerRecursiveAcl(token, method, ContainerRecursiveAclBinding.Hash(changed)));
        Assert.True(_validator.ValidateContainerRecursiveAcl(token, method, hash));
        Assert.NotEqual(ContainerAclBinding.Hash(original),hash);Assert.NotEqual(ContainerRecursiveOwnershipBinding.Hash(original),hash);Assert.NotEqual(ContainerRecursivePermissionBinding.Hash(original), hash); Assert.NotEqual(ContainerPermissionBinding.Hash(original), hash); Assert.NotEqual(ContainerOwnershipBinding.Hash(original), hash);
    }
    public void Dispose() { File.Delete(_file); CryptographicOperations.ZeroMemory(_key); }
}
