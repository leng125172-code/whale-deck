using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static class DevelopmentSourceBinding
{
    public const uint ProtocolMinor = 68;
    public const uint AbortProtocolMinor = 69;
    public const uint UpdateProtocolMinor = 71;
    public const uint PreviousProtocolMinor = 72;
    public const uint DiffProtocolMinor = 73;
    public const string Capability = "development.source.archive";
    public const string UpdateCapability = "development.source.update";
    public const string PreviousCapability = "development.source.previous";
    public const string DiffCapability = "development.source.diff";
    public const string Service = "/whaledeck.agent.v1.DevelopmentSourceService/";
    public const string Stage = Service + "Stage";
    public const string Inspect = Service + "Inspect";
    public const string Publish = Service + "Publish";
    public const string Discard = Service + "Discard";
    public const string Abort = Service + "Abort";
    public const string StageUpdate = Service + "StageUpdate";
    public const string InspectUpdate = Service + "InspectUpdate";
    public const string PublishUpdate = Service + "PublishUpdate";
    public const string DiscardUpdate = Service + "DiscardUpdate";
    public const string AbortUpdate = Service + "AbortUpdate";
    public const string ExportPrevious = Service + "ExportPrevious";
    public const string ReadDiff = Service + "ReadDiff";
    public const string Header = "x-whaledeck-development-source-sha256";
    public const uint MaximumArchiveBytes = 128 * 1024 * 1024;
    public const int ChunkBytes = 64 * 1024;
    public const int MaximumHeaderBytes = 2048;
    private const ulong MaximumExpandedBytes = 1024UL * 1024 * 1024;
    // Bound the source bytes plus PAX path headers and padding.
    public const ulong MaximumPreviousArchiveBytes = MaximumExpandedBytes + 64UL * 1024 * 1024;

    public static string Hash(WorkspaceSourceRequest request) =>
        Convert.ToHexString(SHA256.HashData(request.ToByteArray()));

    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-source-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsSupported(CapabilitiesResponse response) =>
        response.ProtocolMajor == 1 && response.ProtocolMinor >= ProtocolMinor && response.Capabilities.Contains(Capability);

    public static bool SupportsUpdate(CapabilitiesResponse response) => IsSupported(response) &&
        response.ProtocolMinor >= UpdateProtocolMinor && response.Capabilities.Contains(UpdateCapability);

    public static bool SupportsPrevious(CapabilitiesResponse response) => SupportsUpdate(response) &&
        response.ProtocolMinor >= PreviousProtocolMinor && response.Capabilities.Contains(PreviousCapability);

    public static bool SupportsDiff(CapabilitiesResponse response) => SupportsUpdate(response) &&
        response.ProtocolMinor >= DiffProtocolMinor && response.Capabilities.Contains(DiffCapability);

    public static bool IsUpdate(string method) => method is StageUpdate or InspectUpdate or PublishUpdate or DiscardUpdate or AbortUpdate or ExportPrevious or ReadDiff;

    public static bool IsMethod(string method) => method is Stage or Inspect or Publish or Discard or Abort || IsUpdate(method);

    public static bool IsStage(string method) => method is Stage or StageUpdate;

    public static bool IsValid(WorkspaceSourceRequest request, string method) =>
        IsMethod(method) &&
        request.Storage is not null && DevelopmentStorageBinding.IsValid(request.Storage) &&
        IsImportId(request.ImportId) &&
        request.ArchiveFormat is "zip" or "tar" or "tar.gz" && request.ArchiveBytes is > 0 and <= MaximumArchiveBytes &&
        IsDigest(request.ArchiveSha256) &&
        (method is Publish or Discard or PublishUpdate or DiscardUpdate or ExportPrevious or ReadDiff
            ? IsDigest(request.PlanSha256) : request.PlanSha256.Length == 0) &&
        (IsUpdate(method) ? request.ArchiveFormat == "tar" && IsImportId(request.PreviousImportId) && request.PreviousImportId != request.ImportId
            : request.PreviousImportId.Length == 0) &&
        (method is PublishUpdate or ExportPrevious
            ? request.ConflictResolution is WorkspaceSourceConflictResolution.CleanOnly or WorkspaceSourceConflictResolution.RetainAndReplace
            : request.ConflictResolution == WorkspaceSourceConflictResolution.Unspecified);

    public static byte[] HostInput(WorkspaceSourceRequest request) => JsonSerializer.SerializeToUtf8Bytes(HostFields(request));

    internal static Dictionary<string, object> HostFields(WorkspaceSourceRequest request)
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
        if (request.PreviousImportId.Length != 0) fields.Add("previousImportId", request.PreviousImportId);
        if (request.ConflictResolution != WorkspaceSourceConflictResolution.Unspecified)
            fields.Add("conflictResolution", request.ConflictResolution switch
            {
                WorkspaceSourceConflictResolution.CleanOnly => "clean-only",
                WorkspaceSourceConflictResolution.RetainAndReplace => "retain-and-replace",
                _ => throw new ArgumentException("WORKSPACE_SOURCE_REQUEST_INVALID", nameof(request))
            });
        return fields;
    }

    public static bool Matches(WorkspaceSourceRequest request, WorkspaceSourceReceipt receipt, string method) =>
        receipt.WorkspaceId == request.Storage.WorkspaceId && receipt.ImportId == request.ImportId &&
        receipt.StorageSha256 == Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Storage(request.Storage)))) &&
        receipt.ArchiveFormat == request.ArchiveFormat && receipt.ArchiveBytes == request.ArchiveBytes &&
        receipt.ArchiveSha256 == request.ArchiveSha256 && IsDigest(receipt.PlanSha256) &&
        (request.PlanSha256.Length == 0 || receipt.PlanSha256 == request.PlanSha256) &&
        IsDigest(receipt.ContentSha256) && receipt.FileCount is > 0 and < 10000 &&
        receipt.TotalBytes <= Math.Min(MaximumExpandedBytes, Math.Min(request.ArchiveBytes * 256UL, request.Storage.DiskMib * 1024UL * 1024)) &&
        receipt.PreviousImportId == request.PreviousImportId &&
        (IsUpdate(method) ? receipt.Update is { } update && IsDigest(update.PreviousContentSha256) &&
            IsDigest(update.WorkContentSha256) && (update.Dirty || update.WorkContentSha256 == update.PreviousContentSha256) &&
            update.AddedEntries < 10000 && update.RemovedEntries < 10000 && update.ChangedEntries < 10000 &&
            (method != PublishUpdate || request.ConflictResolution != WorkspaceSourceConflictResolution.CleanOnly || !update.Dirty)
            : receipt.Update is null) &&
        (method switch
        {
            Stage or StageUpdate => receipt.State == "Staged",
            Inspect or InspectUpdate => receipt.State is "Staged" or "PublishUnconfirmed" or "Published" or "DiscardUnconfirmed" or "Discarded",
            Publish or PublishUpdate => receipt.State == "Published",
            Discard or DiscardUpdate => receipt.State == "Discarded",
            _ => false
        });

    private static bool IsDigest(string value) =>
        value is { Length: 64 } && value.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9');

    private static bool IsImportId(string value) =>
        Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty && id.ToString("N") == value;
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
