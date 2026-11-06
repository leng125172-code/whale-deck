using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveCapabilityTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string _file = Path.GetTempFileName();
    private readonly CapabilityValidator _validator;
    public ContainerRecycleArchiveCapabilityTests()
    {
        File.WriteAllBytes(_file, _key);
        _validator = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _file }).Build());
    }
    private string Sign(string method, string hash, DateTimeOffset? expiry = null, bool legacy = false)
    {
        var expires = (expiry ?? DateTimeOffset.UtcNow.AddSeconds(30)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var input = legacy ? $"{expires}.{nonce}.{method}" : ContainerRecycleArchiveBinding.Payload(expires, nonce, method, hash);
        return expires + "." + nonce + "." + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input)));
    }
    [Fact]
    public void FiveIndependentUnaryMethodsPreserveCapabilityBoundary()
    {
        var methods = ContainerRecycleArchiveService.Descriptor.Methods;
        Assert.Equal<string>(["Apply", "Discard", "Inspect", "Plan", "Reserve"], methods.Select(m => m.Name).Order());
        Assert.All(methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
    }
    [Theory]
    [InlineData("Plan")][InlineData("Reserve")][InlineData("Apply")][InlineData("Inspect")][InlineData("Discard")]
    public void EachMethodRequiresContentBoundShortLivedSingleUseAuthority(string action)
    {
        var method = ContainerRecycleArchiveBinding.Service + action; var hash = ContainerRecycleArchiveBinding.Hash(ContainerRecycleArchivePolicyTests.Request());
        var token = Sign(method, hash);
        Assert.False(_validator.Validate(token, method));
        Assert.False(_validator.Validate(Sign(method, hash, legacy: true), method));
        Assert.False(_validator.ValidateContainerRecycleArchive(Sign(method, hash, legacy: true), method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchive(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(-1)), method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchive(Sign(method, hash, DateTimeOffset.UtcNow.AddMinutes(2)), method, hash));
        Assert.False(_validator.ValidateContainerDirectory(token, method, hash));
        Assert.False(_validator.ValidateContainerCopy(token, method, hash)); Assert.False(_validator.ValidateContainerMove(token, method, hash));
        Assert.False(_validator.ValidateContainerWrite(token, method, hash));
        Assert.False(_validator.ValidateContainerHistory(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchive(token, ContainerRecycleArchiveBinding.Service + (action == "Plan" ? "Apply" : "Plan"), hash));
        Assert.True(_validator.ValidateContainerRecycleArchive(token, method, hash));
        Assert.False(_validator.ValidateContainerRecycleArchive(token, method, hash));
    }
    [Theory]
    [InlineData("source-id")][InlineData("source-owner")][InlineData("source-path")][InlineData("source-bytes")][InlineData("source-attribute")][InlineData("history")][InlineData("private")][InlineData("retained")][InlineData("ctime")][InlineData("parent")][InlineData("fingerprint")][InlineData("operation")][InlineData("owner")][InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("key")]
    [InlineData("scope")][InlineData("archive")][InlineData("archiveBytes")][InlineData("archiveSha")][InlineData("store")][InlineData("storage")][InlineData("sourceRecord")][InlineData("historyMeta")][InlineData("historyCreated")]
    public void SignatureBindsEveryNestedSourceAndIndependentPurgeAuthority(string field)
    {
        var original=ContainerRecycleArchivePolicyTests.Request();var changed=original.Clone();
        switch(field){
            case "actor":changed.Context.ActorSubject="other";break;case "trace":changed.Context.TraceId="other";break;case "job":changed.Context.JobId="ee0a8865343e49bdb01a72680131bf39";break;case "key":changed.Context.IdempotencyKey="ee0a8865343e49bdb01a72680131bf39";break;
            case "source-id":changed.Plan.Command.Purge.Plan.Command.Source.OperationId="ee0a8865343e49bdb01a72680131bf39";break;case "source-owner":changed.Plan.Command.Purge.Owner=new('A',64);break;
            case "parent":changed.Plan.ParentIdentity.Inode++;break;case "private":changed.Plan.PrivateIdentity=new(){Device=0,Inode=222};break;case "history":changed.Plan.Command.HistoryDigest=new('A',64);break;
            case "operation":changed.OperationId="ee0a8865343e49bdb01a72680131bf39";break;case "owner":changed.Owner=new('B',64);break;case "fingerprint":changed.Plan.Fingerprint=new('C',64);break;
            case "source-path":changed.Plan.Command.Purge.Plan.Command.Source.Plan.Command.Location.Path+="-changed";break;
            case "source-bytes":changed.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value=ByteString.CopyFrom([1,2,3]);break;
            case "source-attribute":changed.Plan.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Name="user.other";break;
            case "retained":changed.Plan.DeletedIdentity.Inode++;break;case "ctime":changed.Plan.DeletedChangedAtUnixNs++;break;
            case "scope":changed.Plan.Command.Mode="All";break;case "archive":changed.Plan.ArchiveIdentity.Inode++;break;case "archiveBytes":changed.Plan.ArchiveBytes++;break;case "archiveSha":changed.Plan.ArchiveSha256=new('A',64);break;case "store":changed.Plan.StoreIdentity.Inode++;break;case "storage":changed.Plan.StorageHash=new('9',64);break;case "sourceRecord":changed.Plan.SourceRecordDigest=new('8',64);break;case "historyMeta":changed.Plan.HistoryMetadataDigest=new('9',64);break;case "historyCreated":changed.Plan.PurgeMarkerDigest=new('9',64);break;
        }
        Assert.NotEqual(ContainerRecycleArchiveBinding.Hash(original),ContainerRecycleArchiveBinding.Hash(changed));
        var method=ContainerRecycleArchiveBinding.Service+"Apply";var hash=ContainerRecycleArchiveBinding.Hash(original);var token=Sign(method,hash);
        Assert.False(_validator.ValidateContainerRecycleArchive(token,method,ContainerRecycleArchiveBinding.Hash(changed)));Assert.True(_validator.ValidateContainerRecycleArchive(token,method,hash));
        Assert.NotEqual(ContainerRecycleBinding.Hash(original),hash);Assert.NotEqual(ContainerRecursiveAclRestoreBinding.Hash(original),hash);
    }
    public void Dispose() { File.Delete(_file); CryptographicOperations.ZeroMemory(_key); }
}
