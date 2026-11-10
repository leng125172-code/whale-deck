using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

public static partial class DevelopmentArchiveBinding
{
    public const uint ProtocolMinor = 77;
    public const string Capability = "development.archive.tasks";
    public const string Service = "/whaledeck.agent.v1.DevelopmentArchiveService/";
    public const string Start = Service + "Start";
    public const string Observe = Service + "Observe";
    public const string Cancel = Service + "Cancel";
    public const int MaximumRequestBytes = 4096;
    public const int MaximumReceiptBytes = 16384;
    private static readonly string[] Facts = ["schemaVersion", "storage", "expectedEpoch", "storageProofSha256",
        "sourceVersion", "sourceManifestSha256", "entryCount", "totalBytes", "archiveFilesystemUuid", "hddProofSha256"];

    public static bool IsSupported(CapabilitiesResponse value) => value.ProtocolMajor == 1 &&
        value.ProtocolMinor >= ProtocolMinor && value.Capabilities.Contains(Capability);
    public static bool IsMethod(string method) => method is Start or Observe or Cancel;
    public static string Hash(WorkspaceArchiveRequest value) => Convert.ToHexString(SHA256.HashData(value.ToByteArray()));
    public static string Payload(string expires, string nonce, string method, string hash) =>
        $"whaledeck-development-archive-capability-v1\n{expires}.{nonce}.{method}.{hash}";

    public static bool IsValid(WorkspaceArchiveRequest value) => value.CalculateSize() <= MaximumRequestBytes &&
        value.Storage is not null && DevelopmentStorageBinding.IsValid(value.Storage) &&
        DevelopmentStorageBinding.IsUuid(value.TaskId) && DevelopmentStorageBinding.IsUuid(value.ArchiveId) &&
        value.Phase is "plan" or "capture" or "verify" &&
        value.ExpectedEpoch is >= 2 and < 0x7ffffffffffffffe && value.ExpectedEpoch % 2 == 0 &&
        (value.Phase == "plan" ? value.PlanSha256.Length == 0 : DevelopmentLifecycleBinding.Digest(value.PlanSha256)) &&
        (value.Phase == "verify" ? DevelopmentLifecycleBinding.Digest(value.ReceiptSha256) : value.ReceiptSha256.Length == 0);

    // Exact sorted ASCII canonical host request. Actor/trace are separately bound
    // by the complete protobuf signature; the stable host identity survives a
    // newly authorized administrator observing the original task.
    public static string HostInput(WorkspaceArchiveRequest value) => JsonSerializer.Serialize(new
    {
        archiveId = value.ArchiveId, expectedEpoch = value.ExpectedEpoch, phase = value.Phase,
        planSha256 = value.PlanSha256, receiptSha256 = value.ReceiptSha256, schemaVersion = 1,
        storage = new { diskMiB = value.Storage.DiskMib, environmentSha256 = value.Storage.EnvironmentSha256,
            rootId = value.Storage.StorageRootId, schemaVersion = 1, workspaceId = value.Storage.WorkspaceId },
        taskId = value.TaskId
    });
    public static string HostHash(WorkspaceArchiveRequest value) => Sha(Encoding.ASCII.GetBytes(HostInput(value)));

    public static WorkspaceArchiveReceipt Parse(WorkspaceArchiveRequest request, string output)
    {
        if (!IsValid(request) || output.Length > MaximumReceiptBytes) throw new FormatException();
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 5 });
        var root = document.RootElement;
        RequireKeys(root, ["schemaVersion", "taskId", "requestSha256", "status", "code", "result"]);
        if (root.GetProperty("schemaVersion").GetUInt32() != 1) throw new FormatException();
        var status = Text(root, "status");
        var result = root.GetProperty("result");
        var code = root.GetProperty("code");
        if (code.ValueKind != JsonValueKind.Null && code.ValueKind != JsonValueKind.String) throw new FormatException();
        var receipt = new WorkspaceArchiveReceipt
        {
            TaskId = Text(root, "taskId"), RequestSha256 = Text(root, "requestSha256"), Status = status,
            Code = code.ValueKind == JsonValueKind.Null ? "" : code.GetString()!,
            ResultJson = result.ValueKind == JsonValueKind.Null ? ByteString.Empty : ByteString.CopyFrom(Canonical(result))
        };
        // JSON null and empty strings must not collapse into one wire state.
        if ((status == "Rejected") != (code.ValueKind == JsonValueKind.String) ||
            (status == "Succeeded") != (result.ValueKind == JsonValueKind.Object) || !Matches(request, receipt))
            throw new FormatException();
        return receipt;
    }

    public static bool Matches(WorkspaceArchiveRequest request, WorkspaceArchiveReceipt receipt)
    {
        try
        {
            if (!IsValid(request) || receipt.CalculateSize() > MaximumReceiptBytes ||
                receipt.TaskId != request.TaskId || receipt.RequestSha256 != HostHash(request)) return false;
            if (receipt.Status == "Rejected") return receipt.ResultJson.IsEmpty && Code().IsMatch(receipt.Code);
            if (receipt.Status is "Pending" or "Running" or "Finishing" or "Stopped" or "Interrupted")
                return receipt.Code.Length == 0 && receipt.ResultJson.IsEmpty;
            if (receipt.Status != "Succeeded" || receipt.Code.Length != 0 || receipt.ResultJson.IsEmpty) return false;
            using var document = JsonDocument.Parse(receipt.ResultJson.Memory, new JsonDocumentOptions { MaxDepth = 4 });
            var result = document.RootElement;
            ValidateResult(request, result);
            return receipt.ResultJson.Span.SequenceEqual(Canonical(result));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException) { return false; }
    }

    private static void ValidateResult(WorkspaceArchiveRequest request, JsonElement result)
    {
        var plan = request.Phase == "plan";
        RequireKeys(result, plan ? [.. Facts, "planSha256"] : [.. Facts, "planSha256", "operationId",
            "archiveFormat", "archiveBytes", "archiveSha256", "archiveIndexSha256", "archiveInode", "receiptSha256"]);
        var storage = result.GetProperty("storage");
        RequireKeys(storage, ["diskMiB", "environmentSha256", "rootId", "schemaVersion", "workspaceId"]);
        if (storage.GetProperty("schemaVersion").GetUInt32() != 1 || result.GetProperty("schemaVersion").GetUInt32() != 1 ||
            storage.GetProperty("diskMiB").GetUInt32() != request.Storage.DiskMib ||
            Text(storage, "environmentSha256") != request.Storage.EnvironmentSha256 ||
            Text(storage, "rootId") != request.Storage.StorageRootId || Text(storage, "workspaceId") != request.Storage.WorkspaceId ||
            result.GetProperty("expectedEpoch").GetUInt64() != request.ExpectedEpoch ||
            !DevelopmentStorageBinding.IsUuid(Text(result, "archiveFilesystemUuid")) ||
            !UpperDigest().IsMatch(Text(result, "sourceVersion")) ||
            result.GetProperty("entryCount").GetUInt32() is < 1 or > 10000 ||
            result.GetProperty("totalBytes").GetUInt64() > 16UL * 1024 * 1024 * 1024) throw new FormatException();
        foreach (var key in new[] { "storageProofSha256", "sourceManifestSha256", "hddProofSha256", "planSha256" })
            if (!DevelopmentLifecycleBinding.Digest(Text(result, key))) throw new FormatException();
        if (Text(result, "planSha256") != Sha(Canonical(result, Facts)) ||
            !plan && Text(result, "planSha256") != request.PlanSha256) throw new FormatException();
        if (plan) return;
        if (Text(result, "operationId") != request.ArchiveId || Text(result, "archiveFormat") != "tar.gz" ||
            result.GetProperty("archiveBytes").GetUInt64() is < 1 or > 17UL * 1024 * 1024 * 1024 ||
            result.GetProperty("archiveInode").GetUInt64() == 0) throw new FormatException();
        foreach (var key in new[] { "archiveSha256", "archiveIndexSha256", "receiptSha256" })
            if (!DevelopmentLifecycleBinding.Digest(Text(result, key))) throw new FormatException();
        var keys = result.EnumerateObject().Select(p => p.Name).Where(n => n != "receiptSha256");
        if (Text(result, "receiptSha256") != Sha(Canonical(result, keys)) ||
            request.Phase == "verify" && Text(result, "receiptSha256") != request.ReceiptSha256) throw new FormatException();
    }

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException();
    private static string Sha(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static void RequireKeys(JsonElement value, string[] keys)
    {
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != keys.Length || names.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            !names.ToHashSet(StringComparer.Ordinal).SetEquals(keys)) throw new FormatException();
    }

    private static byte[] Canonical(JsonElement value, IEnumerable<string>? keys = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value, keys);
        return stream.ToArray();
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value, IEnumerable<string>? keys = null)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var rows = value.EnumerateObject().ToArray();
            if (rows.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new FormatException();
            writer.WriteStartObject();
            foreach (var row in rows.Where(p => keys is null || keys.Contains(p.Name, StringComparer.Ordinal)).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(row.Name); Write(writer, row.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Number) writer.WriteNumberValue(value.GetUInt64());
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Any(c => c > 127 || char.IsControl(c))) throw new FormatException();
            writer.WriteStringValue(text);
        }
        else if (value.ValueKind == JsonValueKind.Null) writer.WriteNullValue();
        else throw new FormatException();
    }

    [GeneratedRegex(@"\AWORKSPACE_(?:STORAGE|ARCHIVE)_[A-Z_]{1,80}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
    [GeneratedRegex(@"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpperDigest();
}
