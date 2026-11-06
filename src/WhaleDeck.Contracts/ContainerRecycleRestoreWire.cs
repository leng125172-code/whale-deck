using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Contracts;

/// <summary>Exact native projection and Python-compatible canonical plan fingerprint.</summary>
public static class ContainerRecycleRestoreWire
{
    public static void RequireKnown(IMessage value)
    {
        var raw = value.ToByteArray();
        var known = value.Descriptor.Parser.WithDiscardUnknownFields(true).ParseFrom(raw).ToByteArray();
        if (!raw.AsSpan().SequenceEqual(known)) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_SCHEMA_INVALID");
    }
    private static object[] Identity(FileObjectIdentity i, long changed) =>
        [i.Device, i.Inode, i.Mode, i.OwnerUid, i.OwnerGid, i.LinkCount, i.SizeBytes, i.ModifiedAtUnixNs, changed];
    public static object SourcePlan(ContainerRecyclePlan p) => new
    {
        schemaVersion = p.SchemaVersion,
        command = new { containerId = p.Command.Location.ContainerId, instanceId = p.Command.Location.InstanceId, path = p.Command.Location.Path, expectedVersion = p.Command.ExpectedVersion },
        targetProof = p.TargetProof, parentIdentity = new[] { p.ParentIdentity.Device, p.ParentIdentity.Inode }, parentVersion = p.ParentVersion,
        uid = p.OwnerUid, gid = p.OwnerGid, fingerprint = p.Fingerprint, sourceIdentity = Identity(p.SourceIdentity, p.SourceChangedAtUnixNs),
        sha256 = p.Sha256, attributes = p.Attributes.Select(a => new[] { a.Name, Convert.ToBase64String(a.Value.Span) }).ToArray()
    };
    public static object Command(ContainerRecycleRestoreCommand c) => new
    { source = new { operationId = c.Source.OperationId, owner = c.Source.Owner, plan = SourcePlan(c.Source.Plan) }, historyDigest = c.HistoryDigest };
    public static object Plan(ContainerRecycleRestorePlan p, bool omitFingerprint = false)
    {
        var value = new Dictionary<string, object> {
            ["schemaVersion"] = p.SchemaVersion, ["command"] = Command(p.Command), ["targetProof"] = p.TargetProof,
            ["parentIdentity"] = new[] { p.ParentIdentity.Device, p.ParentIdentity.Inode }, ["parentVersion"] = p.ParentVersion,
            ["uid"] = p.OwnerUid, ["gid"] = p.OwnerGid, ["privateIdentity"] = new[] { p.PrivateIdentity.Device, p.PrivateIdentity.Inode },
            ["retainedIdentity"] = Identity(p.RetainedIdentity, p.RetainedChangedAtUnixNs)
        };
        if (!omitFingerprint) value["fingerprint"] = p.Fingerprint;
        return value;
    }
    public static string Fingerprint(ContainerRecycleRestorePlan plan)
    {
        var value = JsonSerializer.SerializeToElement(Plan(plan, true)); var output = new StringBuilder();
        Canonical(value, output);
        return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes("whaledeck-container-recycle-restore-plan-v1\n" + output)));
    }
    public static void Receipt(ContainerRecycleRestoreRequest request, ContainerRecycleRestoreReceipt value)
    {
        try { RequireKnown(value); } catch (ArgumentException) { throw Invalid(); }
        var source = value.HasSourceState ? value.SourceState : null;
        if (value.OperationId != request.OperationId || value.SourceOperationId != request.Plan.Command.Source.OperationId ||
            value.Fingerprint != request.Plan.Fingerprint || value.TargetProof != request.Plan.TargetProof || value.State is not ("Completed" or "Aborted" or "Unknown") ||
            value.HistoryDigest != (value.HistoryVerified ? request.Plan.Command.HistoryDigest : "") || value.HistoryAvailable && !value.HistoryVerified ||
            value.BindingCurrent && !(value.State == "Completed" && value.HistoryVerified && value.HistoryAvailable) ||
            source is not (null or "Trashed" or "Restored" or "Aborted" or "Unknown")) throw Invalid();
        if (value.State == "Completed" && (!value.HistoryVerified || source != "Restored" || !value.HasSourceRetained || value.SourceRetained || !value.HasSourceRestored || !value.SourceRestored || !value.HasRestoredByThisOperation || !value.RestoredByThisOperation) ||
            value.State == "Unknown" && (!value.HistoryVerified || value.HasSourceRetained || value.HasSourceRestored || value.HasRestoredByThisOperation) ||
            value.State == "Aborted" && (!value.HasRestoredByThisOperation || value.RestoredByThisOperation ||
                (value.HistoryVerified ? source != "Trashed" || !value.HasSourceRetained || !value.SourceRetained || !value.HasSourceRestored || value.SourceRestored :
                    source is not null || value.HasSourceRetained || value.HasSourceRestored))) throw Invalid();
    }
    private static InvalidOperationException Invalid() => new("CONTAINER_RECYCLE_RESTORE_OUTPUT_INVALID");
    internal static void Canonical(JsonElement value, StringBuilder output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{'); var first = true;
                foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { if (!first) output.Append(','); first = false; Quote(p.Name, output); output.Append(':'); Canonical(p.Value, output); }
                output.Append('}'); break;
            case JsonValueKind.Array:
                output.Append('['); var head = true;
                foreach (var item in value.EnumerateArray()) { if (!head) output.Append(','); head = false; Canonical(item, output); }
                output.Append(']'); break;
            case JsonValueKind.String: Quote(value.GetString()!, output); break;
            case JsonValueKind.Number: output.Append(value.GetRawText()); break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_JSON_INVALID");
        }
    }
    private static void Quote(string value, StringBuilder output)
    {
        _ = new UTF8Encoding(false, true).GetBytes(value); output.Append('"');
        foreach (var rune in value.EnumerateRunes())
            switch (rune.Value)
            {
                case 34: output.Append("\\\""); break;
                case 92: output.Append("\\\\"); break;
                case 8: output.Append("\\b"); break;
                case 12: output.Append("\\f"); break;
                case 10: output.Append("\\n"); break;
                case 13: output.Append("\\r"); break;
                case 9: output.Append("\\t"); break;
                default: if (rune.Value < 32) output.Append("\\u" + rune.Value.ToString("x4", System.Globalization.CultureInfo.InvariantCulture)); else output.Append(rune.ToString()); break;
            }
        output.Append('"');
    }
}
