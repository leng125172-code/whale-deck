using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static partial class DevelopmentRetirementBinding
{
    public const uint ProtocolMinor = 78;
    public const string Capability = "development.retirement.tasks";
    public const string Service = "/whaledeck.agent.v1.DevelopmentRetirementService/";
    public const string Start = Service + "Start";
    public const string Observe = Service + "Observe";
    public const string Cancel = Service + "Cancel";
    public const int MaximumRequestBytes = 4096;
    public const int MaximumReceiptBytes = 16384;

    public static bool IsSupported(CapabilitiesResponse value) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= ProtocolMinor && value.Capabilities.Contains(Capability);
    public static bool IsMethod(string method) => method is Start or Observe or Cancel;
    public static string Hash(WorkspaceRetirementRequest value) => Convert.ToHexString(SHA256.HashData(value.ToByteArray()));
    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-retirement-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsValid(WorkspaceRetirementRequest value) => value.CalculateSize() <= MaximumRequestBytes &&
        value.Storage is not null && DevelopmentStorageBinding.IsValid(value.Storage) &&
        DevelopmentStorageBinding.IsUuid(value.TaskId) && DevelopmentStorageBinding.IsUuid(value.RetirementId) &&
        DevelopmentStorageBinding.IsUuid(value.ArchiveId) &&
        new[] { value.TaskId, value.RetirementId, value.ArchiveId, value.Storage.WorkspaceId }.Distinct(StringComparer.Ordinal).Count() == 4 &&
        value.Phase is "plan" or "execute" && value.ExpectedEpoch is >= 2 and < 0x7ffffffffffffffe && value.ExpectedEpoch % 2 == 0 &&
        DevelopmentLifecycleBinding.Digest(value.ArchivePlanSha256) && DevelopmentLifecycleBinding.Digest(value.CaptureReceiptSha256) &&
        (value.Phase == "plan" ? value.RetirementPlanSha256.Length == 0 : DevelopmentLifecycleBinding.Digest(value.RetirementPlanSha256));

    private static object Prerequisite(WorkspaceRetirementRequest value) => new
    {
        archiveId = value.ArchiveId, archivePlanSha256 = value.ArchivePlanSha256, captureReceiptSha256 = value.CaptureReceiptSha256,
        expectedEpoch = value.ExpectedEpoch, schemaVersion = 1,
        storage = new { diskMiB = value.Storage.DiskMib, environmentSha256 = value.Storage.EnvironmentSha256,
            rootId = value.Storage.StorageRootId, schemaVersion = 1, workspaceId = value.Storage.WorkspaceId }
    };
    // Sorted ASCII host JSON is distinct from the complete protobuf signature:
    // actor and trace remain signed, while a new authorized observer can use
    // the immutable host task. They are never part of a source/retirement ID.
    public static string HostInput(WorkspaceRetirementRequest value) => JsonSerializer.Serialize(new
    {
        phase = value.Phase, prerequisite = Prerequisite(value), retirementId = value.RetirementId,
        retirementPlanSha256 = value.RetirementPlanSha256, schemaVersion = 1, taskId = value.TaskId
    });
    public static string HostHash(WorkspaceRetirementRequest value) => Sha(Encoding.ASCII.GetBytes(HostInput(value)));
    public static string EngineHash(WorkspaceRetirementRequest value) => Sha(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(new
    {
        prerequisite = Prerequisite(value), retirementId = value.RetirementId,
        retirementPlanSha256 = value.RetirementPlanSha256, schemaVersion = 1
    })));

    public static WorkspaceRetirementReceipt Parse(WorkspaceRetirementRequest request, string output)
    {
        if (!IsValid(request) || output.Length > MaximumReceiptBytes) throw new FormatException();
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 5 });
        var root = document.RootElement;
        Keys(root, ["schemaVersion", "taskId", "requestSha256", "status", "code", "result"]);
        if (root.GetProperty("schemaVersion").GetUInt32() != 1) throw new FormatException();
        var status = Text(root, "status"); var code = root.GetProperty("code"); var result = root.GetProperty("result");
        if (code.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) ||
            (status == "Rejected") != (code.ValueKind == JsonValueKind.String) ||
            (status == "Succeeded") != (result.ValueKind == JsonValueKind.Object) ||
            (status != "Succeeded" && result.ValueKind != JsonValueKind.Null)) throw new FormatException();
        var receipt = new WorkspaceRetirementReceipt
        {
            TaskId = Text(root, "taskId"), RequestSha256 = Text(root, "requestSha256"), Status = status,
            Code = code.ValueKind == JsonValueKind.Null ? "" : code.GetString()!,
            ResultJson = result.ValueKind == JsonValueKind.Null ? ByteString.Empty : ByteString.CopyFrom(Canonical(result))
        };
        if (!Matches(request, receipt)) throw new FormatException();
        return receipt;
    }

    public static bool Matches(WorkspaceRetirementRequest request, WorkspaceRetirementReceipt receipt)
    {
        try
        {
            if (!IsValid(request) || receipt.CalculateSize() > MaximumReceiptBytes || receipt.TaskId != request.TaskId ||
                receipt.RequestSha256 != HostHash(request)) return false;
            if (receipt.Status == "Rejected") return receipt.ResultJson.IsEmpty && Code().IsMatch(receipt.Code);
            if (receipt.Status is "Pending" or "Running" or "Finishing" or "Stopped" or "Interrupted")
                return receipt.ResultJson.IsEmpty && receipt.Code.Length == 0;
            if (receipt.Status != "Succeeded" || receipt.Code.Length != 0 || receipt.ResultJson.IsEmpty) return false;
            using var document = JsonDocument.Parse(receipt.ResultJson.Memory, new JsonDocumentOptions { MaxDepth = 4 });
            if (request.Phase == "plan") ValidatePlan(request, document.RootElement);
            else ValidateExecution(request, document.RootElement);
            return receipt.ResultJson.Span.SequenceEqual(Canonical(document.RootElement));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException) { return false; }
    }

    private static void ValidatePlan(WorkspaceRetirementRequest request, JsonElement value)
    {
        Keys(value, ["schemaVersion", "purpose", "storage", "expectedEpoch", "storageProofSha256", "sourceVersion",
            "sourceManifestSha256", "entryCount", "totalBytes", "archiveId", "archivePlanSha256", "captureReceiptSha256",
            "archiveFilesystemUuid", "hddProofSha256", "archiveSha256", "archiveIndexSha256", "archiveBytes", "archiveInode",
            "retainsSource", "reservesBudget", "deletionAuthorized", "retirementPlanSha256"]);
        var storage = value.GetProperty("storage");
        Keys(storage, ["diskMiB", "environmentSha256", "rootId", "schemaVersion", "workspaceId"]);
        if (value.GetProperty("schemaVersion").GetUInt32() != 1 || storage.GetProperty("schemaVersion").GetUInt32() != 1 ||
            Text(value, "purpose") != DevelopmentRetirementPlanBinding.Purpose ||
            Text(storage, "rootId") != request.Storage.StorageRootId || Text(storage, "workspaceId") != request.Storage.WorkspaceId ||
            Text(storage, "environmentSha256") != request.Storage.EnvironmentSha256 || storage.GetProperty("diskMiB").GetUInt32() != request.Storage.DiskMib ||
            value.GetProperty("expectedEpoch").GetUInt64() != request.ExpectedEpoch || Text(value, "archiveId") != request.ArchiveId ||
            Text(value, "archivePlanSha256") != request.ArchivePlanSha256 || Text(value, "captureReceiptSha256") != request.CaptureReceiptSha256 ||
            !DevelopmentStorageBinding.IsUuid(Text(value, "archiveFilesystemUuid")) || !UpperDigest().IsMatch(Text(value, "sourceVersion")) ||
            value.GetProperty("entryCount").GetUInt32() is < 1 or > 10000 || value.GetProperty("totalBytes").GetUInt64() > 16UL * 1024 * 1024 * 1024 ||
            value.GetProperty("archiveBytes").GetUInt64() is < 1 or > 17UL * 1024 * 1024 * 1024 || value.GetProperty("archiveInode").GetUInt64() == 0 ||
            value.GetProperty("retainsSource").ValueKind != JsonValueKind.True || value.GetProperty("reservesBudget").ValueKind != JsonValueKind.True ||
            value.GetProperty("deletionAuthorized").ValueKind != JsonValueKind.False) throw new FormatException();
        Digests(value, ["storageProofSha256", "sourceManifestSha256", "hddProofSha256", "archiveSha256", "archiveIndexSha256", "retirementPlanSha256"]);
        if (Text(value, "retirementPlanSha256") != Sha(DevelopmentRetirementPlanBinding.Canonical(value, omitDigest: true))) throw new FormatException();
    }

    private static void ValidateExecution(WorkspaceRetirementRequest request, JsonElement value)
    {
        Keys(value, ["schemaVersion", "purpose", "retirementId", "workspaceId", "requestSha256", "retirementPlanSha256",
            "storageProofSha256", "sourceManifestSha256", "captureReceiptSha256", "projectId", "sourceRemoved", "quotaReleased",
            "claimReleased", "retainsHdd", "releasesApplicationBudget"]);
        if (value.GetProperty("schemaVersion").GetUInt32() != 1 || Text(value, "purpose") != "whaledeck-development-source-retirement-receipt-v1" ||
            Text(value, "retirementId") != request.RetirementId || Text(value, "workspaceId") != request.Storage.WorkspaceId ||
            Text(value, "requestSha256") != EngineHash(request) || Text(value, "retirementPlanSha256") != request.RetirementPlanSha256 ||
            Text(value, "captureReceiptSha256") != request.CaptureReceiptSha256 || value.GetProperty("projectId").GetUInt32() is < 1 or > 0x7fffffff ||
            value.GetProperty("sourceRemoved").ValueKind != JsonValueKind.True || value.GetProperty("quotaReleased").ValueKind != JsonValueKind.True ||
            value.GetProperty("claimReleased").ValueKind != JsonValueKind.True || value.GetProperty("retainsHdd").ValueKind != JsonValueKind.True ||
            value.GetProperty("releasesApplicationBudget").ValueKind != JsonValueKind.False) throw new FormatException();
        Digests(value, ["storageProofSha256", "sourceManifestSha256"]);
    }

    private static void Digests(JsonElement value, string[] names)
    {
        foreach (var name in names) if (!DevelopmentLifecycleBinding.Digest(Text(value, name))) throw new FormatException();
    }
    private static void Keys(JsonElement value, string[] keys)
    {
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != keys.Length || names.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            !names.ToHashSet(StringComparer.Ordinal).SetEquals(keys)) throw new FormatException();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException();
    private static byte[] Canonical(JsonElement value) => DevelopmentRetirementPlanBinding.Canonical(value);
    private static string Sha(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    [GeneratedRegex(@"\AWORKSPACE_STORAGE_[A-Z_]{1,80}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
    [GeneratedRegex(@"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpperDigest();
}
