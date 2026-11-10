using System.Security.Cryptography;
using System.Text.Json;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

// Internal prerequisite only. A matching plan is neither a signed capability
// nor permission to remove source or release a registration's budget.
public static class DevelopmentRetirementPlanBinding
{
    public const string Purpose = "whaledeck-development-source-retirement-plan-v1";
    public const int MaximumBytes = 8192;
    private static readonly string[] Shared = ["schemaVersion", "storage", "expectedEpoch", "storageProofSha256",
        "sourceVersion", "sourceManifestSha256", "entryCount", "totalBytes", "archiveFilesystemUuid", "hddProofSha256",
        "archiveSha256", "archiveIndexSha256", "archiveBytes", "archiveInode"];
    private static readonly string[] Extra = ["purpose", "archiveId", "archivePlanSha256", "captureReceiptSha256",
        "retainsSource", "reservesBudget", "deletionAuthorized", "retirementPlanSha256"];

    public static bool Matches(WorkspaceArchiveRequest request, WorkspaceArchiveReceipt verified, string planJson)
    {
        try
        {
            if (request.Phase != "verify" || verified.Status != "Succeeded" ||
                !DevelopmentArchiveBinding.Matches(request, verified) || planJson.Length > MaximumBytes) return false;
            using var planDoc = JsonDocument.Parse(planJson, new JsonDocumentOptions { MaxDepth = 4 });
            using var sourceDoc = JsonDocument.Parse(verified.ResultJson.Memory, new JsonDocumentOptions { MaxDepth = 4 });
            var plan = planDoc.RootElement; var source = sourceDoc.RootElement;
            var names = plan.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != Shared.Length + Extra.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                !names.ToHashSet(StringComparer.Ordinal).SetEquals(Shared.Concat(Extra))) return false;
            foreach (var name in Shared)
                if (!JsonElement.DeepEquals(plan.GetProperty(name), source.GetProperty(name))) return false;
            if (plan.GetProperty("purpose").GetString() != Purpose ||
                plan.GetProperty("archiveId").GetString() != request.ArchiveId ||
                plan.GetProperty("archivePlanSha256").GetString() != request.PlanSha256 ||
                plan.GetProperty("captureReceiptSha256").GetString() != request.ReceiptSha256 ||
                plan.GetProperty("retainsSource").ValueKind != JsonValueKind.True ||
                plan.GetProperty("reservesBudget").ValueKind != JsonValueKind.True ||
                plan.GetProperty("deletionAuthorized").ValueKind != JsonValueKind.False) return false;
            var digest = plan.GetProperty("retirementPlanSha256").GetString();
            return DevelopmentLifecycleBinding.Digest(digest ?? "") && digest ==
                Convert.ToHexStringLower(SHA256.HashData(Canonical(plan, omitDigest: true)));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException) { return false; }
    }

    public static byte[] Canonical(JsonElement value, bool omitDigest = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value, omitDigest);
        return stream.ToArray();
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value, bool omitDigest = false)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var rows = value.EnumerateObject().ToArray();
                if (rows.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new FormatException();
                writer.WriteStartObject();
                foreach (var row in rows.Where(p => !omitDigest || p.Name != "retirementPlanSha256").OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(row.Name); Write(writer, row.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Any(c => c > 127 || char.IsControl(c))) throw new FormatException();
                writer.WriteStringValue(text); break;
            case JsonValueKind.Number: writer.WriteNumberValue(value.GetUInt64()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            default: throw new FormatException();
        }
    }
}
