using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Dedicated archive capability over registered roots, never arbitrary host paths or scripts.</summary>
public sealed class FileArchiveGrpcService(FileTransferRunner runner) : FileArchiveService.FileArchiveServiceBase
{
    public override async Task<FileHistoryPage> List(FileHistoryRequest request, ServerCallContext context)
    {
        var input = FileArchivePolicy.HistoryInput(request);
        using var result = await runner.OnceAsync("archive-list", input, context.CancellationToken);
        return FileArchivePolicy.History(result.RootElement, request);
    }
    public override async Task<FileArchivePlan> Plan(FileArchivePlanRequest request, ServerCallContext context)
    {
        FileOperationPolicy.Actor(request.Context);
        using var result = await runner.OnceAsync("archive-plan", FileArchivePolicy.CommandJson(request.Command), context.CancellationToken);
        return FileArchivePolicy.Plan(result.RootElement, request.Command);
    }
    public override Task<FileArchiveReceipt> Apply(FileArchiveApplyRequest request, ServerCallContext context) => Write(request, context, false);
    public override Task<FileArchiveReceipt> Discard(FileArchiveApplyRequest request, ServerCallContext context) => Write(request, context, true);
    private async Task<FileArchiveReceipt> Write(FileArchiveApplyRequest request, ServerCallContext context, bool discard)
    {
        FileOperationPolicy.Actor(request.Context); FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        var plan = FileArchivePolicy.PlanInput(request.Plan);
        if (request.OperationId == request.Plan.Command.VersionId) throw new ArgumentException("FILES_ARCHIVE_INVALID");
        using var result = await runner.OnceAsync(discard ? "archive-discard" : "archive-apply",
            JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner, plan }), context.CancellationToken);
        return FileArchivePolicy.Receipt(result.RootElement, request.OperationId, request.Plan);
    }
    public override async Task<FileArchiveReceipt> Inspect(FileArchiveLookup request, ServerCallContext context)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        using var result = await runner.OnceAsync("archive-status", JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }), context.CancellationToken);
        return FileArchivePolicy.Receipt(result.RootElement, request.OperationId);
    }
}

internal static class FileArchivePolicy
{
    internal sealed record RootCommand(string Kind, string RootId, string Path, string VersionId, string ExpectedVersion);
    private static readonly JsonSerializerOptions InputOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> CommandFields = ["kind", "rootId", "path", "versionId", "expectedVersion"];
    private static readonly HashSet<string> PlanFields = ["command", "parentIdentity", "sourceIdentity", "sourceSha256", "archiveIdentity", "restoreMetadata", "sizeBytes", "sha256", "fingerprint"];
    private static readonly HashSet<string> ArchiveFields = ["metadataHash", "dataIdentity"];
    private static readonly HashSet<string> ReceiptFields = ["operationId", "kind", "state", "fingerprint", "version", "sizeBytes", "sha256", "sourceRetained", "bindingCurrent", "hddBackup", "versionId"];
    private static readonly HashSet<string> ProofFields = ["operationId", "storageId", "sourceVersion", "sizeBytes", "sha256", "createdAtUnixMs", "differentSourceDevice"];
    private static readonly HashSet<string> HistoryFields = ["entries", "nextCursor", "version", "scanned", "invalidRecords", "complete"];
    private static readonly HashSet<string> VersionFields = ["versionId", "rootId", "path", "sourceVersion", "sizeBytes", "sha256", "createdAtUnixMs", "captureKind", "captureState", "availability", "differentSourceDevice"];
    public static string Kind(FileArchiveKind value) => value switch { FileArchiveKind.Trash => "trash", FileArchiveKind.Restore => "restore", FileArchiveKind.Purge => "purge", _ => throw new ArgumentException("FILES_ARCHIVE_INVALID") };
    private static FileArchiveKind Kind(string value) => value switch { "trash" => FileArchiveKind.Trash, "restore" => FileArchiveKind.Restore, "purge" => FileArchiveKind.Purge, _ => throw new InvalidOperationException("FILES_ARCHIVE_INVALID") };
    public static RootCommand Command(FileArchiveCommand? value)
    {
        if (value is null) throw new ArgumentException("FILES_ARCHIVE_REQUIRED");
        var kind = Kind(value.Kind); FileTransferPolicy.Location(value.Location);
        if (value.Kind == FileArchiveKind.Trash)
        { if (value.VersionId != "") throw new ArgumentException("FILES_ARCHIVE_INVALID"); FileTransferPolicy.Hash(value.ExpectedVersion); }
        else
        {
            FileTransferPolicy.Identity(value.VersionId);
            if (value.Kind == FileArchiveKind.Purge && value.ExpectedVersion != "ABSENT") throw new ArgumentException("FILES_ARCHIVE_INVALID");
            if (value.ExpectedVersion != "ABSENT") FileTransferPolicy.Hash(value.ExpectedVersion);
        }
        return new(kind, value.Location.RootId, value.Location.Path, value.VersionId, value.ExpectedVersion);
    }
    public static string CommandJson(FileArchiveCommand value) => JsonSerializer.Serialize(Command(value), InputOptions);
    private static void Identity(FileObjectIdentity? value)
    {
        if (value is null || value.Inode == 0 || value.Mode > ushort.MaxValue || (value.Mode & 0xF000) != 0x8000 || value.LinkCount != 1 ||
            value.OwnerUid == uint.MaxValue || value.OwnerGid == uint.MaxValue || value.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes)
            throw new ArgumentException("FILES_ARCHIVE_IDENTITY_INVALID");
    }
    private static object[]? Raw(FileObjectIdentity? value) => value is null ? null : [value.Device, value.Inode, value.Mode, value.OwnerUid, value.OwnerGid, value.LinkCount, value.SizeBytes, value.ModifiedAtUnixNs];
    public static JsonElement PlanInput(FileArchivePlan? value)
    {
        if (value is null) throw new ArgumentException("FILES_ARCHIVE_PLAN_REQUIRED");
        var command = Command(value.Command); FileTransferPolicy.Hash(value.Fingerprint); FileTransferPolicy.Hash(value.Sha256);
        if (value.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes) throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        if (value.Command.Kind == FileArchiveKind.Purge)
        { if (value.ParentIdentity is not null || value.SourceIdentity is not null || value.SourceSha256 != "") throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID"); }
        else
        {
            if (value.ParentIdentity is not { Inode: > 0 }) throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
            if (command.ExpectedVersion == "ABSENT")
            { if (value.SourceIdentity is not null || value.SourceSha256 != "") throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID"); }
            else { Identity(value.SourceIdentity); FileTransferPolicy.Hash(value.SourceSha256); }
        }
        if (value.Command.Kind == FileArchiveKind.Trash)
        {
            if (value.ArchiveIdentity is not null || value.RestoreMetadata is not null || value.SourceIdentity!.SizeBytes != value.SizeBytes || value.SourceSha256 != value.Sha256)
                throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        }
        else
        {
            var archive = value.ArchiveIdentity ?? throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
            Identity(archive.DataIdentity); Identity(value.RestoreMetadata); FileTransferPolicy.Hash(archive.MetadataHash);
            if (archive.DataIdentity.OwnerUid != 0 || (archive.DataIdentity.Mode & 0xFFF) != 0x180 || archive.DataIdentity.SizeBytes != value.SizeBytes || value.RestoreMetadata.SizeBytes != value.SizeBytes)
                throw new ArgumentException("FILES_ARCHIVE_PLAN_INVALID");
        }
        return JsonSerializer.SerializeToElement(new { command, parentIdentity = value.ParentIdentity is null ? null : new[] { value.ParentIdentity.Device, value.ParentIdentity.Inode },
            sourceIdentity = Raw(value.SourceIdentity), value.SourceSha256,
            archiveIdentity = value.ArchiveIdentity is null ? null : new { value.ArchiveIdentity.MetadataHash, dataIdentity = Raw(value.ArchiveIdentity.DataIdentity) },
            restoreMetadata = Raw(value.RestoreMetadata), value.SizeBytes, value.Sha256, value.Fingerprint }, InputOptions);
    }
    public static FileArchivePlan Plan(JsonElement value, FileArchiveCommand expected)
    {
        Fields(value, PlanFields); var command = value.GetProperty("command"); Fields(command, CommandFields);
        var result = new FileArchivePlan { Command = new() { Kind = Kind(Text(command, "kind")), Location = new() { RootId = Text(command, "rootId"), Path = Text(command, "path") },
            VersionId = Text(command, "versionId"), ExpectedVersion = Text(command, "expectedVersion") },
            SourceIdentity = Identity(value.GetProperty("sourceIdentity")), RestoreMetadata = Identity(value.GetProperty("restoreMetadata")),
            SourceSha256 = Text(value, "sourceSha256"), SizeBytes = value.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(value, "sha256"), Fingerprint = Text(value, "fingerprint") };
        var parent = value.GetProperty("parentIdentity");
        if (parent.ValueKind != JsonValueKind.Null)
        { if (parent.GetArrayLength() != 2) throw new InvalidOperationException("FILES_PARENT_INVALID"); result.ParentIdentity = new() { Device = parent[0].GetUInt64(), Inode = parent[1].GetUInt64() }; }
        var archive = value.GetProperty("archiveIdentity");
        if (archive.ValueKind != JsonValueKind.Null)
        { Fields(archive, ArchiveFields); result.ArchiveIdentity = new() { MetadataHash = Text(archive, "metadataHash"), DataIdentity = Identity(archive.GetProperty("dataIdentity")) }; }
        if (!result.Command.Equals(expected)) throw new InvalidOperationException("FILES_ARCHIVE_TARGET_MISMATCH");
        _ = PlanInput(result); return result;
    }
    private static FileObjectIdentity? Identity(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.GetArrayLength() != 8) throw new InvalidOperationException("FILES_ARCHIVE_IDENTITY_INVALID");
        return new() { Device = value[0].GetUInt64(), Inode = value[1].GetUInt64(), Mode = value[2].GetUInt32(), OwnerUid = value[3].GetUInt32(), OwnerGid = value[4].GetUInt32(),
            LinkCount = value[5].GetUInt64(), SizeBytes = value[6].GetInt64(), ModifiedAtUnixNs = value[7].GetInt64() };
    }
    public static string HistoryInput(FileHistoryRequest value)
    {
        FileTransferPolicy.Location(value.Location, true);
        if (value.Take is < 1 or > 50) throw new ArgumentException("FILES_ARCHIVE_LIST_INVALID");
        if (value.After != "") FileTransferPolicy.Identity(value.After);
        if (value.ExpectedVersion != "") FileTransferPolicy.Hash(value.ExpectedVersion);
        return JsonSerializer.Serialize(new { rootId = value.Location.RootId, path = value.Location.Path, value.After, value.Take, value.ExpectedVersion }, InputOptions);
    }
    public static FileHistoryPage History(JsonElement value, FileHistoryRequest request)
    {
        _ = HistoryInput(request); Fields(value, HistoryFields);
        var result = new FileHistoryPage { Version = Text(value, "version"), NextCursor = Text(value, "nextCursor"),
            Scanned = value.GetProperty("scanned").GetUInt32(), InvalidRecords = value.GetProperty("invalidRecords").GetUInt32(), Complete = value.GetProperty("complete").GetBoolean() };
        FileTransferPolicy.Hash(result.Version);
        if (result.Scanned > 500 || result.InvalidRecords > result.Scanned || result.Complete != (result.NextCursor == "") || request.ExpectedVersion != "" && result.Version != request.ExpectedVersion)
            throw new InvalidOperationException("FILES_ARCHIVE_LIST_INVALID");
        if (!result.Complete) { FileTransferPolicy.Identity(result.NextCursor); if (string.CompareOrdinal(result.NextCursor, request.After) <= 0) throw new InvalidOperationException("FILES_ARCHIVE_LIST_INVALID"); }
        var previous = request.After;
        foreach (var row in value.GetProperty("entries").EnumerateArray())
        {
            Fields(row, VersionFields);
            var entry = new FileVersionRecord { VersionId = Text(row, "versionId"), Location = new() { RootId = Text(row, "rootId"), Path = Text(row, "path") },
                SourceVersion = Text(row, "sourceVersion"), SizeBytes = row.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(row, "sha256"), CreatedAtUnixMs = row.GetProperty("createdAtUnixMs").GetInt64(),
                CaptureKind = Text(row, "captureKind"), CaptureState = Text(row, "captureState"), Availability = Text(row, "availability"), DifferentSourceDevice = row.GetProperty("differentSourceDevice").GetBoolean() };
            FileTransferPolicy.Location(entry.Location); FileTransferPolicy.Identity(entry.VersionId); FileTransferPolicy.Hash(entry.SourceVersion); FileTransferPolicy.Hash(entry.Sha256);
            if (entry.Location.RootId != request.Location.RootId || request.Location.Path != "" && entry.Location.Path != request.Location.Path || entry.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes ||
                entry.CreatedAtUnixMs is < 0 or > 253402300799999 || entry.CaptureKind is not ("Overwrite" or "Trash") || entry.CaptureState is not ("Available" or "Pending" or "Trashed" or "Restored" or "Canceled") ||
                entry.CaptureKind == "Overwrite" && entry.CaptureState != "Available" || entry.CaptureKind == "Trash" && entry.CaptureState == "Available" || entry.Availability is not ("Present" or "Missing" or "Incomplete" or "Invalid") ||
                string.CompareOrdinal(entry.VersionId, previous) <= 0 || !result.Complete && string.CompareOrdinal(entry.VersionId, result.NextCursor) > 0)
                throw new InvalidOperationException("FILES_ARCHIVE_LIST_INVALID");
            result.Entries.Add(entry); previous = entry.VersionId;
        }
        if (result.Entries.Count > request.Take || result.Entries.Count > result.Scanned) throw new InvalidOperationException("FILES_ARCHIVE_LIST_INVALID");
        return result;
    }
    public static FileArchiveReceipt Receipt(JsonElement value, string operation, FileArchivePlan? plan = null)
    {
        Fields(value, ReceiptFields); FileTransferPolicy.Identity(operation);
        var result = new FileArchiveReceipt { OperationId = Text(value, "operationId"), Kind = Kind(Text(value, "kind")), State = Text(value, "state"),
            Fingerprint = Text(value, "fingerprint"), Version = Text(value, "version"), SizeBytes = value.GetProperty("sizeBytes").GetInt64(), Sha256 = Text(value, "sha256"),
            SourceRetained = value.GetProperty("sourceRetained").GetBoolean(), BindingCurrent = value.GetProperty("bindingCurrent").GetBoolean(), VersionId = Text(value, "versionId") };
        FileTransferPolicy.Identity(result.VersionId); FileTransferPolicy.Hash(result.Fingerprint); FileTransferPolicy.Hash(result.Sha256);
        if (result.OperationId != operation || result.State is not ("Completed" or "Aborted" or "Unknown") || result.SizeBytes is < 0 or > FileTransferPolicy.MaximumBytes ||
            result.Kind == FileArchiveKind.Trash && result.VersionId != operation || result.Kind != FileArchiveKind.Trash && result.VersionId == operation)
            throw new InvalidOperationException("FILES_ARCHIVE_RECEIPT_INVALID");
        var proof = value.GetProperty("hddBackup");
        if (proof.ValueKind != JsonValueKind.Null)
        {
            Fields(proof, ProofFields); var parsed = FileTransferPolicy.Receipt(value); result.HddBackup = parsed.HddBackup;
            if (result.Kind == FileArchiveKind.Purge) throw new InvalidOperationException("FILES_ARCHIVE_RECEIPT_INVALID");
        }
        if (result.State == "Completed")
        {
            FileTransferPolicy.Hash(result.Version);
            if (result.Kind == FileArchiveKind.Trash && !result.SourceRetained || result.Kind == FileArchiveKind.Purge && result.SourceRetained || result.SourceRetained && result.HddBackup is null)
                throw new InvalidOperationException("FILES_ARCHIVE_RECEIPT_INVALID");
        }
        else if (result.Version != "" || result.SourceRetained || result.BindingCurrent) throw new InvalidOperationException("FILES_ARCHIVE_RECEIPT_INVALID");
        if (plan is not null)
        {
            _ = PlanInput(plan); var retained = plan.Command.Kind == FileArchiveKind.Trash || plan.Command.Kind == FileArchiveKind.Restore && plan.SourceIdentity is not null;
            if (result.Kind != plan.Command.Kind || result.Fingerprint != plan.Fingerprint || result.SizeBytes != plan.SizeBytes || result.Sha256 != plan.Sha256 ||
                result.VersionId != (result.Kind == FileArchiveKind.Trash ? operation : plan.Command.VersionId) || result.State == "Completed" && result.SourceRetained != retained ||
                result.HddBackup is { } backup && (!retained || backup.SourceVersion != plan.Command.ExpectedVersion || backup.SizeBytes != plan.SourceIdentity!.SizeBytes || backup.Sha256 != plan.SourceSha256))
                throw new InvalidOperationException("FILES_ARCHIVE_RECEIPT_MISMATCH");
        }
        return result;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    private static void Fields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains)) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
    }
}
