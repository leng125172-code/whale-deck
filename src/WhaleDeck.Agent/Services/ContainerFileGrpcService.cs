using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Separate read-only container capability; no host root alias or container Exec.</summary>
public sealed class ContainerFileGrpcService(DockerEngine docker, ResourceRegistry registry, FileTransferRunner runner)
    : ContainerFileService.ContainerFileServiceBase
{
    public override async Task<ContainerTerminalTarget> InspectTarget(ResourceReference request, ServerCallContext context)
    {
        ContainerTerminalPolicy.RequireId(request.ResourceId);
        if (request.ResourceType != "Container") throw new ArgumentException("CONTAINER_TYPE_REQUIRED");
        return ContainerTerminalPolicy.Inspect(await docker.InspectContainerAsync(request.ResourceId, context.CancellationToken), registry);
    }

    public override async Task<FileListResponse> List(ContainerFileListRequest request, ServerCallContext context)
    {
        ContainerFilePolicy.Location(request.Location, allowEmpty: true);
        ContainerFilePolicy.Cursor(request.After);
        if (request.Take is < 1 or > 100) throw new ArgumentException("CONTAINER_FILES_LIST_INVALID");
        using var result = await runner.ContainerOnceAsync("list", JsonSerializer.Serialize(new
        {
            containerId = request.Location.ContainerId, instanceId = request.Location.InstanceId,
            path = request.Location.Path, after = request.After, take = request.Take
        }), context.CancellationToken);
        var root = result.RootElement;
        var response = new FileListResponse { NextCursor = root.GetProperty("nextCursor").GetString(), Version = root.GetProperty("version").GetString() };
        response.Entries.AddRange(root.GetProperty("entries").EnumerateArray().Select(FileTransferPolicy.Entry));
        ContainerFilePolicy.Page(response, request.After, request.Take);
        return response;
    }

    public override async Task<FileEntry> Inspect(ContainerFileLocation request, ServerCallContext context)
    {
        ContainerFilePolicy.Location(request, allowEmpty: true);
        using var result = await runner.ContainerOnceAsync("stat", JsonSerializer.Serialize(new
        { containerId = request.ContainerId, instanceId = request.InstanceId, path = request.Path }), context.CancellationToken);
        var entry = FileTransferPolicy.Entry(result.RootElement);
        ContainerFilePolicy.Entry(entry, allowEmptyName: request.Path.Length == 0);
        if (entry.Name != request.Path.Split('/')[^1]) throw new InvalidOperationException("CONTAINER_FILES_OUTPUT_INVALID");
        return entry;
    }

    public override Task Download(ContainerFileDownloadRequest request, IServerStreamWriter<FileDownloadFrame> responseStream, ServerCallContext context)
    {
        ContainerFilePolicy.Location(request.Location);
        FileTransferPolicy.Hash(request.ExpectedVersion);
        return runner.ContainerDownloadAsync(JsonSerializer.Serialize(new
        { containerId = request.Location.ContainerId, instanceId = request.Location.InstanceId, path = request.Location.Path, expectedVersion = request.ExpectedVersion }),
            request.Location.Path.Split('/')[^1], request.ExpectedVersion, responseStream, context.CancellationToken);
    }
}

internal static partial class ContainerFilePolicy
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    [GeneratedRegex(@"\A([1-9][0-9]{0,9}):([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.([0-9]{1,9}))?Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    public static void Location(ContainerFileLocation? location, bool allowEmpty = false)
    {
        if (location is null) throw new ArgumentException("CONTAINER_FILES_LOCATION_REQUIRED");
        ContainerTerminalPolicy.RequireId(location.ContainerId);
        var match = InstancePattern().Match(location.InstanceId);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 2)
            throw new ArgumentException("CONTAINER_FILES_INSTANCE_INVALID");
        // Validate calendar fields, but never truncate the raw nanosecond startup identity.
        var fraction = match.Groups[3].Value;
        var date = match.Groups[2].Value + (fraction.Length == 0 ? "" : "." + fraction[..Math.Min(7, fraction.Length)]) + "Z";
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
            throw new ArgumentException("CONTAINER_FILES_INSTANCE_INVALID");
        Path(location.Path, allowEmpty);
    }

    public static void Path(string value, bool allowEmpty)
    {
        try
        {
            if (StrictUtf8.GetByteCount(value) > 4096 || value.Any(char.IsControl) || value.Contains('\\', StringComparison.Ordinal) ||
                !(allowEmpty && value.Length == 0) && value.Split('/').Any(part => part is "" or "." or ".." || StrictUtf8.GetByteCount(part) > 255))
                throw new ArgumentException("CONTAINER_FILES_PATH_INVALID");
        }
        catch (EncoderFallbackException) { throw new ArgumentException("CONTAINER_FILES_PATH_INVALID"); }
    }

    public static void Cursor(string value)
    {
        Path(value, allowEmpty: true);
        if (value.Contains('/', StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_FILES_CURSOR_INVALID");
    }

    public static void Entry(FileEntry entry, bool allowEmptyName = false)
    {
        Cursor(entry.Name); FileTransferPolicy.Hash(entry.Version);
        if (!allowEmptyName && entry.Name.Length == 0 || entry.Kind is not ("File" or "Directory" or "Symlink" or "Special") ||
            entry.Mode.Length != 4 || entry.Mode.Any(value => value is < '0' or > '7') || entry.SizeBytes < 0 ||
            (entry.Kind is "Symlink" or "Special") && !entry.IsProtected)
            throw new InvalidOperationException("CONTAINER_FILES_OUTPUT_INVALID");
    }

    public static void Page(FileListResponse page, string after, uint take)
    {
        FileTransferPolicy.Hash(page.Version); Cursor(page.NextCursor);
        if (page.Entries.Count > take) throw new InvalidOperationException("CONTAINER_FILES_OUTPUT_INVALID");
        var previous = after;
        foreach (var entry in page.Entries)
        {
            Entry(entry);
            // Python compares Unicode scalar values; UTF-8 ordinal order has the same ordering.
            if (CompareNames(previous, entry.Name) >= 0) throw new InvalidOperationException("CONTAINER_FILES_OUTPUT_INVALID");
            previous = entry.Name;
        }
        if (page.NextCursor.Length > 0 && (page.Entries.Count == 0 || page.NextCursor != page.Entries[^1].Name))
            throw new InvalidOperationException("CONTAINER_FILES_OUTPUT_INVALID");
    }

    private static int CompareNames(string left, string right) => StrictUtf8.GetBytes(left).AsSpan().SequenceCompareTo(StrictUtf8.GetBytes(right));

    public static RpcException Failure(string? code)
    {
        var status = code switch
        {
            "CONTAINER_PROTECTED" or "CONTAINER_UNSAFE_ISOLATION" or "CONTAINER_UNSAFE_MOUNT" or "CONTAINER_FILES_PROTECTED" or
                "CONTAINER_FILES_PATH_REJECTED" or "CONTAINER_FILES_ACCESS_DENIED" or "CONTAINER_FILES_SHARED_CORE_MOUNT" or
                "CONTAINER_FILES_REGULAR_SINGLE_LINK_REQUIRED" => StatusCode.PermissionDenied,
            "CONTAINER_FILES_VERSION_CONFLICT" or "CONTAINER_FILES_READ_CHANGED" => StatusCode.Aborted,
            "CONTAINER_FILES_BUSY" or "CONTAINER_FILES_CAPACITY_LOW" or "CONTAINER_FILES_SIZE_LIMIT" or "CONTAINER_FILES_SCAN_LIMIT" => StatusCode.ResourceExhausted,
            "CONTAINER_FILES_TIMEOUT" or "CONTAINER_FILES_INPUT_TIMEOUT" => StatusCode.DeadlineExceeded,
            "CONTAINER_FILES_CLIENT_DISCONNECTED" => StatusCode.Cancelled,
            "CONTAINER_FILES_NOT_FOUND" => StatusCode.NotFound,
            "CONTAINER_FILES_INSTANCE_CHANGED" or "CONTAINER_FILES_TARGET_CHANGED" or "CONTAINER_FILES_ROOT_CHANGED" or "CONTAINER_NOT_RUNNING" => StatusCode.FailedPrecondition,
            _ => StatusCode.Unavailable
        };
        var known = status != StatusCode.Unavailable;
        return new(new(status, known ? code! : "CONTAINER_FILES_UNAVAILABLE"));
    }
}
