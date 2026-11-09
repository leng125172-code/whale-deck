using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class DevelopmentSourceBinding
{
    public const uint ProtocolMinor = 68;
    public const uint AbortProtocolMinor = 69;
    public const string Capability = "development.source.archive";
    public const string Service = "/whaledeck.agent.v1.DevelopmentSourceService/";
    public const string Stage = Service + "Stage";
    public const string Inspect = Service + "Inspect";
    public const string Publish = Service + "Publish";
    public const string Discard = Service + "Discard";
    public const string Abort = Service + "Abort";
    public const string Header = "x-whaledeck-development-source-sha256";
    public const uint MaximumArchiveBytes = 128 * 1024 * 1024;
    public const int ChunkBytes = 64 * 1024;
    public const int MaximumHeaderBytes = 2048;
    private const ulong MaximumExpandedBytes = 1024UL * 1024 * 1024;

    public static string Hash(WorkspaceSourceRequest request) =>
        Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-source-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsSupported(CapabilitiesResponse response) =>
        response.ProtocolMajor == 1 && response.ProtocolMinor >= ProtocolMinor && response.Capabilities.Contains(Capability);

    public static bool IsValid(WorkspaceSourceRequest request, string method) =>
        request.Storage is not null && DevelopmentStorageBinding.IsValid(request.Storage) &&
        Guid.TryParseExact(request.ImportId, "N", out var id) && id != Guid.Empty && id.ToString("N") == request.ImportId &&
        request.ArchiveFormat is "zip" or "tar" or "tar.gz" && request.ArchiveBytes is > 0 and <= MaximumArchiveBytes &&
        IsDigest(request.ArchiveSha256) &&
        (method is Stage or Inspect or Abort ? request.PlanSha256.Length == 0 : IsDigest(request.PlanSha256));

    public static byte[] HostInput(WorkspaceSourceRequest request)
    {
        var fields = new Dictionary<string, object>
        {
            ["storage"] = Storage(request.Storage),
            ["importId"] = request.ImportId,
            ["archiveFormat"] = request.ArchiveFormat,
            ["archiveBytes"] = request.ArchiveBytes,
            ["archiveSha256"] = request.ArchiveSha256
        };
        if (request.PlanSha256.Length != 0) fields.Add("planSha256", request.PlanSha256);
        return JsonSerializer.SerializeToUtf8Bytes(fields);
    }

    public static bool Matches(WorkspaceSourceRequest request, WorkspaceSourceReceipt receipt, string method) =>
        receipt.WorkspaceId == request.Storage.WorkspaceId && receipt.ImportId == request.ImportId &&
        receipt.StorageSha256 == Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Storage(request.Storage)))) &&
        receipt.ArchiveFormat == request.ArchiveFormat && receipt.ArchiveBytes == request.ArchiveBytes &&
        receipt.ArchiveSha256 == request.ArchiveSha256 && IsDigest(receipt.PlanSha256) &&
        (request.PlanSha256.Length == 0 || receipt.PlanSha256 == request.PlanSha256) &&
        IsDigest(receipt.ContentSha256) && receipt.FileCount is > 0 and < 10000 &&
        receipt.TotalBytes <= Math.Min(MaximumExpandedBytes, Math.Min(request.ArchiveBytes * 256UL, request.Storage.DiskMib * 1024UL * 1024)) &&
        (method switch
        {
            Stage => receipt.State == "Staged",
            Inspect => receipt.State is "Staged" or "PublishUnconfirmed" or "Published" or "DiscardUnconfirmed" or "Discarded",
            Publish => receipt.State == "Published",
            Discard => receipt.State == "Discarded",
            _ => false
        });

    private static bool IsDigest(string value) =>
        value is { Length: 64 } && value.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9');

    // Same ordinal key order as the host's canonical storage fingerprint.
    private static object Storage(WorkspaceAllocationRequest request) => new
    {
        diskMiB = request.DiskMib,
        environmentSha256 = request.EnvironmentSha256,
        rootId = request.StorageRootId,
        schemaVersion = 1,
        workspaceId = request.WorkspaceId
    };
}
