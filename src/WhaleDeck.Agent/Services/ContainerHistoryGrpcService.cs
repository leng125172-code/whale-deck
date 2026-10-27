using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Read-only historical content; business authorization and permanent recovery intents remain in Application.</summary>
public sealed class ContainerHistoryGrpcService(FileTransferRunner runner) : ContainerHistoryService.ContainerHistoryServiceBase
{
    public override async Task<ContainerHistoryPage> List(ContainerHistoryListRequest request, ServerCallContext context)
    {
        ContainerHistoryPolicy.List(request); ContainerHistoryPolicy.Binding(request, context);
        using var result = await runner.ContainerHistoryOnceAsync(JsonSerializer.Serialize(new
        {
            containerId = request.Location.ContainerId, instanceId = request.Location.InstanceId, path = request.Location.Path,
            after = request.After, take = request.Take, expectedVersion = request.ExpectedVersion
        }), context.CancellationToken);
        return ContainerHistoryPolicy.Page(result.RootElement, request);
    }

    public override Task ReadSource(ContainerHistorySourceRequest request, IServerStreamWriter<ContainerHistorySourceFrame> responseStream, ServerCallContext context)
    {
        ContainerHistoryPolicy.Source(request); ContainerHistoryPolicy.Binding(request, context);
        return runner.ContainerHistorySourceAsync(JsonSerializer.Serialize(new
        {
            containerId = request.Location.ContainerId, instanceId = request.Location.InstanceId, path = request.Location.Path,
            captureId = request.CaptureId, expectedCaptureHash = request.ExpectedCaptureHash
        }), request, responseStream, context.CancellationToken);
    }
}

internal static class ContainerHistoryPolicy
{
    public static void List(ContainerHistoryListRequest request)
    {
        ContainerFilePolicy.Location(request.Location, allowEmpty: true);
        if (request.Take is < 1 or > 50) throw new ArgumentException("CONTAINER_HISTORY_REQUEST_INVALID");
        if (request.After.Length != 0) FileTransferPolicy.Identity(request.After);
        if (request.ExpectedVersion.Length != 0) FileTransferPolicy.Hash(request.ExpectedVersion);
        if (request.After.Length != 0 && request.ExpectedVersion.Length == 0) throw new ArgumentException("CONTAINER_HISTORY_VERSION_REQUIRED");
    }

    public static void Source(ContainerHistorySourceRequest request)
    {
        ContainerFilePolicy.Location(request.Location); FileTransferPolicy.Identity(request.CaptureId); FileTransferPolicy.Hash(request.ExpectedCaptureHash);
    }

    public static void Binding(IMessage request, ServerCallContext context)
    {
        var expected = context.RequestHeaders.GetValue(ContainerHistoryBinding.Header);
        var actual = ContainerHistoryBinding.Hash(request);
        if (expected is not { Length: 64 } || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual)))
            throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_HISTORY_BINDING_INVALID"));
    }

    public static void Fields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        if (names.Length != fields.Length || !names.ToHashSet(StringComparer.Ordinal).SetEquals(fields)) throw Invalid();
    }

    public static ContainerHistoryEntry Entry(JsonElement value)
    {
        Fields(value, "captureId", "containerId", "originalInstanceId", "path", "sourceVersion", "sizeBytes", "sha256",
            "createdAtUnixMs", "differentSourceDevice", "availability", "captureHash");
        var entry = new ContainerHistoryEntry
        {
            CaptureId = Text(value, "captureId"), ContainerId = Text(value, "containerId"), OriginalInstanceId = Text(value, "originalInstanceId"),
            Path = Text(value, "path"), SourceVersion = Text(value, "sourceVersion"), SizeBytes = value.GetProperty("sizeBytes").GetInt64(),
            Sha256 = Text(value, "sha256"), CreatedAtUnixMs = value.GetProperty("createdAtUnixMs").GetInt64(),
            DifferentSourceDevice = value.GetProperty("differentSourceDevice").GetBoolean(), Availability = Text(value, "availability"),
            CaptureHash = Text(value, "captureHash")
        };
        Entry(entry); return entry;
    }

    public static void Entry(ContainerHistoryEntry entry)
    {
        FileTransferPolicy.Identity(entry.CaptureId);
        ContainerFilePolicy.Location(new() { ContainerId = entry.ContainerId, InstanceId = entry.OriginalInstanceId, Path = entry.Path });
        FileTransferPolicy.Hash(entry.SourceVersion); FileTransferPolicy.Hash(entry.Sha256); FileTransferPolicy.Hash(entry.CaptureHash);
        if (entry.SizeBytes is < 0 or > 1073741824L || entry.CreatedAtUnixMs is < 0 or > 253402300799999L ||
            entry.Availability is not ("Present" or "Incomplete" or "Missing" or "Invalid")) throw Invalid();
    }

    public static ContainerHistoryPage Page(JsonElement value, ContainerHistoryListRequest request)
    {
        Fields(value, "containerId", "instanceId", "path", "targetProof", "entries", "version", "nextCursor", "scanned", "invalidRecords", "complete");
        if (Text(value, "containerId") != request.Location.ContainerId || Text(value, "instanceId") != request.Location.InstanceId ||
            Text(value, "path") != request.Location.Path) throw Invalid();
        var page = new ContainerHistoryPage { Location = request.Location.Clone(), TargetProof = Text(value, "targetProof"),
            Version = Text(value, "version"), NextCursor = Text(value, "nextCursor"), Scanned = value.GetProperty("scanned").GetUInt32(),
            InvalidRecords = value.GetProperty("invalidRecords").GetUInt32(), Complete = value.GetProperty("complete").GetBoolean() };
        var entries = value.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 50) throw Invalid();
        foreach (var row in entries.EnumerateArray()) page.Entries.Add(Entry(row));
        Page(page, request); return page;
    }

    public static void Page(ContainerHistoryPage page, ContainerHistoryListRequest request)
    {
        if (!Equals(page.Location, request.Location)) throw Invalid();
        FileTransferPolicy.Hash(page.TargetProof); FileTransferPolicy.Hash(page.Version);
        if (page.NextCursor.Length != 0) FileTransferPolicy.Identity(page.NextCursor);
        if (request.ExpectedVersion.Length != 0 && page.Version != request.ExpectedVersion || page.Entries.Count > request.Take ||
            page.Scanned > 500 || page.InvalidRecords > page.Scanned || page.Entries.Count + page.InvalidRecords > page.Scanned ||
            page.Complete != (page.NextCursor.Length == 0) || !page.Complete && string.CompareOrdinal(page.NextCursor, request.After) <= 0) throw Invalid();
        var previous = request.After; var bytes = 0;
        foreach (var entry in page.Entries)
        {
            Entry(entry);
            if (entry.ContainerId != request.Location.ContainerId || request.Location.Path.Length != 0 && entry.Path != request.Location.Path ||
                string.CompareOrdinal(entry.CaptureId, previous) <= 0 || page.NextCursor.Length != 0 && string.CompareOrdinal(entry.CaptureId, page.NextCursor) > 0) throw Invalid();
            bytes += entry.CalculateSize();
            if (bytes > 48 * 1024) throw Invalid();
            previous = entry.CaptureId;
        }
    }

    public static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw Invalid();
    public static InvalidOperationException Invalid() => new("CONTAINER_HISTORY_OUTPUT_INVALID");

    public static RpcException Failure(string? code)
    {
        if (code?.StartsWith("CONTAINER_HISTORY_", StringComparison.Ordinal) != true)
            return code?.StartsWith("CONTAINER_FILES_", StringComparison.Ordinal) == true ? ContainerFilePolicy.Failure(code) : ContainerUploadPolicy.Failure(code);
        var status = code switch
        {
            "CONTAINER_HISTORY_REQUEST_INVALID" or "CONTAINER_HISTORY_VERSION_REQUIRED" => StatusCode.InvalidArgument,
            "CONTAINER_HISTORY_PROTECTED" or "CONTAINER_HISTORY_SCOPE_MISMATCH" => StatusCode.PermissionDenied,
            "CONTAINER_HISTORY_VERSION_CONFLICT" or "CONTAINER_HISTORY_CAPTURE_CHANGED" or "CONTAINER_HISTORY_CONTENT_CHANGED" => StatusCode.Aborted,
            "CONTAINER_HISTORY_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_HISTORY_TIMEOUT" => StatusCode.DeadlineExceeded,
            "CONTAINER_HISTORY_HISTORY_LIMIT" => StatusCode.ResourceExhausted,
            "CONTAINER_HISTORY_METADATA_INVALID" or "CONTAINER_HISTORY_METADATA_CHANGED" or "CONTAINER_HISTORY_CONTENT_INVALID" or
                "CONTAINER_HISTORY_INCOMPLETE" or "CONTAINER_HISTORY_TARGET_CHANGED" or "CONTAINER_HISTORY_ENGINE_MISMATCH" or
                "CONTAINER_HISTORY_MODULE_UNSAFE" or "CONTAINER_HISTORY_INVOCATION_INVALID" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        return new(new(status, status == StatusCode.Unavailable ? "CONTAINER_HISTORY_UNAVAILABLE" : code!));
    }
}
