using System.Text;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferGrpcService
{
    public override async Task<FileContentSearchResponse> SearchContent(FileContentSearchRequest request, ServerCallContext context)
    {
        FileTransferPolicy.ContentSearch(request);
        using var result = await runner.OnceAsync("content-search", JsonSerializer.Serialize(new
        {
            rootId = request.Location.RootId, path = request.Location.Path, query = request.Query,
            matchCase = request.MatchCase, cursor = request.Cursor, take = request.Take, maxFileBytes = request.MaxFileBytes
        }), context.CancellationToken);
        return FileTransferPolicy.ContentResponse(result.RootElement, request);
    }
}

internal static partial class FileTransferPolicy
{
    public static void ContentSearch(FileContentSearchRequest request)
    {
        Location(request.Location, true);
        if (string.IsNullOrWhiteSpace(request.Query) || new UTF8Encoding(false, true).GetByteCount(request.Query) > 256 || request.Query.Any(char.IsControl) ||
            request.Take is < 1 or > 100 || request.MaxFileBytes is < 1 or > 16 * 1024 * 1024 || request.Cursor.Length > 32768 ||
            request.Cursor.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("FILES_SEARCH_INVALID");
    }

    public static FileContentSearchResponse ContentResponse(JsonElement value, FileContentSearchRequest request)
    {
        var response = new FileContentSearchResponse
        {
            NextCursor = value.GetProperty("nextCursor").GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID"),
            ScannedEntries = value.GetProperty("scannedEntries").GetUInt32(), ExcludedEntries = value.GetProperty("excludedEntries").GetUInt32(),
            Complete = value.GetProperty("complete").GetBoolean(), StopReason = value.GetProperty("stopReason").GetString(),
            DepthLimited = value.GetProperty("depthLimited").GetBoolean(), ScannedFiles = value.GetProperty("scannedFiles").GetUInt32(),
            ReadBytes = value.GetProperty("readBytes").GetInt64(), SkippedNonTextFiles = value.GetProperty("skippedNonTextFiles").GetUInt32(),
            SkippedOversizedFiles = value.GetProperty("skippedOversizedFiles").GetUInt32()
        };
        foreach (var hit in value.GetProperty("entries").EnumerateArray())
        {
            var path = hit.GetProperty("path").GetString()!;
            Location(new() { RootId = request.Location.RootId, Path = path });
            var entry = Entry(hit.GetProperty("entry"));
            var excerpt = hit.GetProperty("excerpt").GetString()!;
            var line = hit.GetProperty("lineNumber").GetUInt32();
            if (entry.IsProtected || entry.Kind != "File" || entry.SizeBytes > request.MaxFileBytes ||
                !path.EndsWith("/" + entry.Name, StringComparison.Ordinal) && path != entry.Name ||
                request.Location.Path.Length > 0 && !path.StartsWith(request.Location.Path + "/", StringComparison.Ordinal) ||
                line is 0 or > 16 * 1024 * 1024 + 1 || string.IsNullOrEmpty(excerpt) || Encoding.UTF8.GetByteCount(excerpt) > 1280 || excerpt.Any(char.IsControl))
                throw new InvalidOperationException("FILES_OUTPUT_INVALID");
            response.Entries.Add(new FileContentSearchHit { Path = path, Entry = entry, LineNumber = line, Excerpt = excerpt,
                ExcerptTruncated = hit.GetProperty("excerptTruncated").GetBoolean() });
        }
        if (response.Entries.Count > request.Take || response.ScannedEntries > 10000 || response.ExcludedEntries > 10000 ||
            response.ScannedFiles > 10000 || response.ReadBytes is < 0 or > 64 * 1024 * 1024 ||
            response.SkippedNonTextFiles > 10000 || response.SkippedOversizedFiles > 10000 || response.NextCursor.Length > 32768 ||
            response.NextCursor.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')) ||
            response.Complete && (response.NextCursor.Length != 0 || response.DepthLimited) ||
            response.StopReason is not ("" or "ScanBudget" or "DirectoryScanLimit" or "ResultBytes" or "ResultPage" or "DepthLimit" or "ContentByteBudget" or "FileReadTimeLimit") ||
            response.StopReason == "FileReadTimeLimit" && (response.Complete || response.NextCursor.Length != 0))
            throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        return response;
    }
}
