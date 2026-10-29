using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

/// <summary>Dedicated, method-capability checked file transport; not arbitrary host paths or commands.</summary>
public sealed partial class FileTransferGrpcService(FileTransferRunner runner) : FileTransferService.FileTransferServiceBase
{
    public override async Task<FileRoots> ListRoots(Empty request, ServerCallContext context)
    {
        using var result = await runner.OnceAsync("roots", "{}", context.CancellationToken);
        var response = new FileRoots();
        foreach (var item in result.RootElement.GetProperty("roots").EnumerateArray())
        {
            var root = new FileRoot { Id = item.GetProperty("id").GetString(), Name = item.GetProperty("name").GetString() };
            root.Actions.AddRange(item.GetProperty("actions").EnumerateArray().Select(value => value.GetString()!)); response.Roots.Add(root);
        }
        return response;
    }
    public override async Task<FileListResponse> List(FileListRequest request, ServerCallContext context)
    {
        FileTransferPolicy.Location(request.Location, true);
        if (request.Take is < 1 or > 100 || request.After.Length > 255 || request.After.Contains('/', StringComparison.Ordinal)) throw new ArgumentException("FILES_LIST_INVALID");
        using var result = await runner.OnceAsync("list", JsonSerializer.Serialize(new { rootId = request.Location.RootId, path = request.Location.Path, after = request.After, take = request.Take }), context.CancellationToken);
        var response = new FileListResponse { NextCursor = result.RootElement.GetProperty("nextCursor").GetString(), Version = result.RootElement.GetProperty("version").GetString() };
        response.Entries.AddRange(result.RootElement.GetProperty("entries").EnumerateArray().Select(FileTransferPolicy.Entry)); return response;
    }
    public override async Task<FileEntry> Inspect(FileLocation request, ServerCallContext context)
    {
        FileTransferPolicy.Location(request);
        using var result = await runner.OnceAsync("stat", JsonSerializer.Serialize(new { rootId = request.RootId, path = request.Path }), context.CancellationToken);
        return FileTransferPolicy.Entry(result.RootElement);
    }
    public override async Task<FileListResponse> Directories(FileListRequest request, ServerCallContext context)
    {
        FileTransferPolicy.Location(request.Location, true);
        if (request.Take is < 1 or > 100 || Encoding.UTF8.GetByteCount(request.After) > 255 || request.After.Any(char.IsControl) || request.After.Contains('/', StringComparison.Ordinal) || request.After.Contains('\\', StringComparison.Ordinal) || request.After is "." or "..") throw new ArgumentException("FILES_LIST_INVALID");
        using var result = await runner.OnceAsync("directories", JsonSerializer.Serialize(new { rootId = request.Location.RootId, path = request.Location.Path, after = request.After, take = request.Take }), context.CancellationToken);
        var response = new FileListResponse { NextCursor = result.RootElement.GetProperty("nextCursor").GetString(), Version = result.RootElement.GetProperty("version").GetString() };
        response.Entries.AddRange(result.RootElement.GetProperty("entries").EnumerateArray().Select(FileTransferPolicy.Entry));
        if (response.Entries.Count > request.Take || response.Entries.Any(item => item.Kind != "Directory" || item.IsProtected)) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        return response;
    }
    public override async Task<FileSearchResponse> Search(FileSearchRequest request, ServerCallContext context)
    {
        FileTransferPolicy.Search(request);
        using var result = await runner.OnceAsync("search", JsonSerializer.Serialize(new { rootId = request.Location.RootId, path = request.Location.Path,
            query = request.Query, kind = request.Kind, matchCase = request.MatchCase, cursor = request.Cursor, take = request.Take }), context.CancellationToken);
        var value = result.RootElement;
        var response = new FileSearchResponse { NextCursor = value.GetProperty("nextCursor").GetString() ?? throw new InvalidOperationException("FILES_OUTPUT_INVALID"), ScannedEntries = value.GetProperty("scannedEntries").GetUInt32(),
            ExcludedEntries = value.GetProperty("excludedEntries").GetUInt32(), Complete = value.GetProperty("complete").GetBoolean(),
            StopReason = value.GetProperty("stopReason").GetString(), DepthLimited = value.GetProperty("depthLimited").GetBoolean() };
        foreach (var hit in value.GetProperty("entries").EnumerateArray())
        {
            var path = hit.GetProperty("path").GetString()!;
            FileTransferPolicy.Location(new() { RootId = request.Location.RootId, Path = path });
            var entry = FileTransferPolicy.Entry(hit.GetProperty("entry"));
            if (entry.IsProtected || entry.Kind is not ("File" or "Directory") || !path.EndsWith("/" + entry.Name, StringComparison.Ordinal) && path != entry.Name ||
                request.Location.Path.Length > 0 && !path.StartsWith(request.Location.Path + "/", StringComparison.Ordinal)) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
            response.Entries.Add(new FileSearchHit { Path = path, Entry = entry });
        }
        if (response.Entries.Count > request.Take || response.ScannedEntries > 10000 || response.NextCursor.Length > 32768 ||
            response.Complete && (response.NextCursor.Length != 0 || response.DepthLimited) || response.StopReason is not ("" or "ScanBudget" or "DirectoryScanLimit" or "ResultBytes" or "ResultPage" or "DepthLimit")) throw new InvalidOperationException("FILES_OUTPUT_INVALID");
        return response;
    }
    public override Task Download(FileDownloadRequest request, IServerStreamWriter<FileDownloadFrame> responseStream, ServerCallContext context)
    {
        FileTransferPolicy.Location(request.Location); FileTransferPolicy.Hash(request.ExpectedVersion);
        return runner.DownloadAsync(JsonSerializer.Serialize(new { rootId = request.Location.RootId, path = request.Location.Path, expectedVersion = request.ExpectedVersion }), responseStream, context.CancellationToken);
    }
    public override async Task<FileUploadReceipt> Upload(IAsyncStreamReader<FileUploadFrame> requestStream, ServerCallContext context)
        => await Receive(requestStream, context, staged: false);
    public override async Task<FileUploadReceipt> StageUpload(IAsyncStreamReader<FileUploadFrame> requestStream, ServerCallContext context)
        => await Receive(requestStream, context, staged: true);
    private async Task<FileUploadReceipt> Receive(IAsyncStreamReader<FileUploadFrame> requestStream, ServerCallContext context, bool staged)
    {
        using var first = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken); first.CancelAfter(TimeSpan.FromSeconds(5));
        if (!await requestStream.MoveNext(first.Token)) throw new ArgumentException("FILES_HEADER_REQUIRED");
        var header = requestStream.Current;
        var input = FileTransferPolicy.Open(header);
        return await runner.UploadAsync(input, header.Header.SizeBytes, requestStream, context.CancellationToken, staged);
    }
    public override Task<FileUploadReceipt> PublishUpload(FileUploadLookup request, ServerCallContext context) => StagedAction("publish", request, context);
    public override Task<FileUploadReceipt> DiscardUpload(FileUploadLookup request, ServerCallContext context) => StagedAction("discard", request, context);
    private async Task<FileUploadReceipt> StagedAction(string action, FileUploadLookup request, ServerCallContext context)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        using var result = await runner.OnceAsync(action, JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }), context.CancellationToken);
        return FileTransferPolicy.Receipt(result.RootElement);
    }
    public override async Task<FileUploadReceipt> GetUploadStatus(FileUploadLookup request, ServerCallContext context)
    {
        FileTransferPolicy.Identity(request.OperationId); FileTransferPolicy.Hash(request.Owner);
        using var result = await runner.OnceAsync("status", JsonSerializer.Serialize(new { operationId = request.OperationId, owner = request.Owner }), context.CancellationToken);
        return FileTransferPolicy.Receipt(result.RootElement);
    }
}

internal static partial class FileTransferPolicy
{
    public const long MaximumBytes = 1024L * 1024 * 1024;
    public static void Search(FileSearchRequest request)
    {
        Location(request.Location, true);
        if (string.IsNullOrWhiteSpace(request.Query) || Encoding.UTF8.GetByteCount(request.Query) > 256 || request.Query.Any(char.IsControl) || request.Query.Contains('/', StringComparison.Ordinal) || request.Query.Contains('\\', StringComparison.Ordinal) ||
            request.Kind is not ("All" or "File" or "Directory") || request.Take is < 1 or > 100 || request.Cursor.Length > 32768 || request.Cursor.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("FILES_SEARCH_INVALID");
    }
    public static void Location(FileLocation? value, bool allowEmpty = false)
    {
        if (value is null || value.RootId.Length is < 1 or > 64 || value.RootId.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'z' or '.' or '-')) ||
            Encoding.UTF8.GetByteCount(value.Path) > 4096 || value.Path.Any(char.IsControl) || value.Path.Contains('\\', StringComparison.Ordinal) ||
            !(allowEmpty && value.Path.Length == 0) && value.Path.Split('/').Any(part => part is "" or "." or ".." || Encoding.UTF8.GetByteCount(part) > 255))
            throw new ArgumentException("FILES_LOCATION_INVALID");
    }
    public static void Identity(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var id) || id == Guid.Empty || id.ToString("N") != value) throw new ArgumentException("FILES_OPERATION_INVALID");
    }
    public static void Hash(string value)
    {
        if (value.Length != 64 || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F'))) throw new ArgumentException("FILES_HASH_INVALID");
    }
    public static string Open(FileUploadFrame frame)
    {
        var header = frame.Header;
        if (header is null || frame.Data.Length != 0 || frame.Offset != 0 || frame.Commit || header.SizeBytes is < 0 or > MaximumBytes || header.Context is null ||
            string.IsNullOrWhiteSpace(header.Context.ActorSubject) || header.Context.ActorSubject.Length > 256 || header.Context.ActorSubject.Any(char.IsControl)) throw new ArgumentException("FILES_HEADER_INVALID");
        Location(header.Location); Identity(header.OperationId); Hash(header.Owner); Hash(header.Sha256);
        if (header.ExpectedVersion != "ABSENT") Hash(header.ExpectedVersion);
        return JsonSerializer.Serialize(new { rootId = header.Location.RootId, path = header.Location.Path, operationId = header.OperationId, owner = header.Owner,
            sizeBytes = header.SizeBytes, sha256 = header.Sha256, expectedVersion = header.ExpectedVersion });
    }
    public static string Input(FileUploadFrame frame, ref long offset, long size)
    {
        if (frame.Header is not null) throw new ArgumentException("FILES_TARGET_CHANGED");
        if (frame.Commit && frame.Data.Length == 0 && frame.Offset == 0 && offset == size) return "{\"type\":\"commit\"}";
        if (frame.Commit || frame.Data.Length is < 1 or > 32768 || frame.Offset != offset || offset + frame.Data.Length > size) throw new ArgumentException("FILES_CHUNK_INVALID");
        var value = JsonSerializer.Serialize(new { type = "chunk", offset, data = frame.Data.ToBase64() }); offset += frame.Data.Length; return value;
    }
    public static FileEntry Entry(JsonElement item) => new() { Name = item.GetProperty("name").GetString(), Kind = item.GetProperty("kind").GetString(),
        SizeBytes = item.GetProperty("sizeBytes").GetInt64(), ModifiedAtUnixMs = item.GetProperty("modifiedAtUnixMs").GetInt64(), Version = item.GetProperty("version").GetString(),
        Mode = item.GetProperty("mode").GetString(), IsProtected = item.GetProperty("isProtected").GetBoolean() };
    public static FileEntry DownloadEntry(JsonElement item, long maximumBytes)
    {
        var entry = Entry(item);
        if (maximumBytes is not (MaximumBytes or FileTreePackingPolicy.MaximumArchiveBytes) || entry.SizeBytes < 0 || entry.SizeBytes > maximumBytes || entry.Kind != "File" || entry.IsProtected)
            throw new InvalidOperationException("FILES_METADATA_INVALID");
        Hash(entry.Version); return entry;
    }
    public static FileUploadReceipt Receipt(JsonElement item)
    {
        string Value(string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
        bool Flag(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        var result = new FileUploadReceipt { OperationId = Value("operationId"), State = Value("state"), SizeBytes = item.GetProperty("sizeBytes").GetInt64(), Sha256 = Value("sha256"),
            Version = Value("version"), PreviousRetained = Flag("previousRetained"), Replayed = Flag("replayed"), BindingCurrent = Flag("bindingCurrent"), IsInProgress = Flag("isInProgress"), ResultCode = Value("resultCode") };
        if (result.ResultCode.Length > 0 && (result.ResultCode.Length <= 13 || result.ResultCode.Length > 64 || !result.ResultCode.StartsWith("FILES_BACKUP_", StringComparison.Ordinal) || result.ResultCode.Any(c => c is not (>= 'A' and <= 'Z' or '_'))))
            throw new InvalidOperationException("FILES_RESULT_CODE_INVALID");
        if (item.TryGetProperty("hddBackup", out var proof))
        {
            var id = proof.GetProperty("operationId").GetString() ?? string.Empty;
            var sourceVersion = proof.GetProperty("sourceVersion").GetString() ?? string.Empty;
            var checksum = proof.GetProperty("sha256").GetString() ?? string.Empty;
            var storage = proof.GetProperty("storageId").GetString() ?? string.Empty; var size = proof.GetProperty("sizeBytes").GetInt64();
            var created = proof.GetProperty("createdAtUnixMs").GetInt64();
            Identity(id); Hash(sourceVersion); Hash(checksum);
            if (id != result.OperationId || storage != "database-platform.backup.hdd" || size is < 0 or > MaximumBytes || created is < 0 or > 253402300799999)
                throw new InvalidOperationException("FILES_BACKUP_PROOF_INVALID");
            result.HddBackup = new() { OperationId = id, StorageId = storage, SourceVersion = sourceVersion, SizeBytes = size,
                Sha256 = checksum, CreatedAtUnixMs = created, DifferentSourceDevice = proof.GetProperty("differentSourceDevice").GetBoolean() };
        }
        return result;
    }
}

/// <summary>Only fixed root helper pipes. Bounded memory, four shared root slots, exact-invocation cancellation.</summary>
public sealed partial class FileTransferRunner
{
    private const string Helper = "/usr/local/libexec/whaledeck-privileged";
    public async Task<JsonDocument> OnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        using var result = await StartAsync(action, input, cancellationToken);
        var response = await ReadJsonAsync(result, cancellationToken);
        await FinishAsync(result, cancellationToken);
        return response;
    }
    public Task DownloadAsync(string input, IServerStreamWriter<FileDownloadFrame> output, CancellationToken cancellationToken) =>
        DownloadCoreAsync("download", FileTransferPolicy.MaximumBytes, input, output, cancellationToken);
    public Task TreePackingDownloadAsync(string input, IServerStreamWriter<FileDownloadFrame> output, CancellationToken cancellationToken) =>
        DownloadCoreAsync("tree-pack-download", FileTreePackingPolicy.MaximumArchiveBytes, input, output, cancellationToken);
    private static async Task DownloadCoreAsync(string action, long maximumBytes, string input, IServerStreamWriter<FileDownloadFrame> output, CancellationToken cancellationToken)
    {
        using var result = await StartAsync(action, input, cancellationToken);
        long offset = 0; long size = -1; var complete = false;
        while (!complete)
        {
            using var row = await ReadJsonAsync(result, cancellationToken);
            var value = row.RootElement; var kind = value.GetProperty("type").GetString();
            FileDownloadFrame frame;
            if (kind == "metadata" && size == -1)
            {
                var entry = FileTransferPolicy.DownloadEntry(value, maximumBytes); size = entry.SizeBytes;
                frame = new() { Kind = "metadata", Metadata = entry };
            }
            else if (kind == "chunk" && size >= 0 && value.GetProperty("offset").GetInt64() == offset)
            {
                var bytes = value.GetProperty("data").GetBytesFromBase64();
                if (bytes.Length is < 1 or > 32768 || offset + bytes.Length > size) throw new InvalidOperationException("FILES_CHUNK_INVALID");
                frame = new() { Kind = "chunk", Offset = offset, Data = ByteString.CopyFrom(bytes) }; offset += bytes.Length;
            }
            else if (kind == "end" && offset == size && value.GetProperty("sizeBytes").GetInt64() == size)
            {
                frame = new() { Kind = "end", SizeBytes = size, Sha256 = value.GetProperty("sha256").GetString() ?? string.Empty, Version = value.GetProperty("version").GetString() ?? string.Empty };
                FileTransferPolicy.Hash(frame.Sha256); FileTransferPolicy.Hash(frame.Version); complete = true;
            }
            else throw new InvalidOperationException("FILES_OUTPUT_INVALID");
            await output.WriteAsync(frame, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        await FinishAsync(result, cancellationToken);
    }
    public async Task<FileUploadReceipt> UploadAsync(string input, long size, IAsyncStreamReader<FileUploadFrame> frames, CancellationToken cancellationToken, bool staged = false)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        using var result = await StartAsync(staged ? "stage" : "upload", input, lifetime.Token);
        var writing = Write(); var reading = ReadJsonAsync(result, lifetime.Token);
        try
        {
            var ended = await Task.WhenAny(writing, reading);
            try { await ended; }
            catch (IOException) when (ended == writing)
            {
                // A fixed helper can reject a stale version before the sender
                // finishes. Preserve its stable RPC rejection instead of a
                // nondeterministic broken-pipe/Internal result.
                using var rejected = await reading.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                throw new InvalidOperationException("FILES_INPUT_INTERRUPTED");
            }
            await writing.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            using var response = await reading;
            await FinishAsync(result, lifetime.Token);
            return FileTransferPolicy.Receipt(response.RootElement);
        }
        finally
        {
            lifetime.Cancel();
            try { await writing; } catch (Exception) { /* No body, path or exception text in logs. */ }
            FileTransferPipe.AbortInput(result.Process.StandardInput);
            try { if (reading.IsCompletedSuccessfully) reading.Result.Dispose(); else await reading; } catch (Exception) { }
        }
        async Task Write()
        {
            long offset = 0; var committed = false;
            while (await frames.MoveNext(lifetime.Token))
            {
                var frame = frames.Current;
                if (committed) throw new ArgumentException("FILES_COMMIT_ALREADY_SENT");
                var line = FileTransferPolicy.Input(frame, ref offset, size);
                if (frame.Commit)
                {
                    using var end = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); end.CancelAfter(TimeSpan.FromSeconds(5));
                    if (await frames.MoveNext(end.Token)) throw new ArgumentException("FILES_COMMIT_TRAILING_DATA");
                }
                await result.Process.StandardInput.WriteLineAsync(line.AsMemory(), lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                await result.Process.StandardInput.FlushAsync(lifetime.Token);
                if (frame.Commit) { committed = true; break; }
            }
            if (!committed) throw new ArgumentException("FILES_COMMIT_REQUIRED");
            result.Process.StandardInput.Close();
        }
    }
    private static async Task<Driver> StartAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("FILES_LINUX_REQUIRED");
        var driver = new Driver(action);
        try
        {
            driver.Process.Start(); driver.Errors = CaptureErrorsAsync(driver, cancellationToken);
            await driver.Process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken);
            await driver.Process.StandardInput.FlushAsync(cancellationToken);
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); opening.CancelAfter(TimeSpan.FromSeconds(10));
            using var ready = await ReadJsonAsync(driver, opening.Token);
            if (ready.RootElement.GetProperty("type").GetString() != "driver") throw new InvalidOperationException("FILES_DRIVER_INVALID");
            var id = ready.RootElement.GetProperty("invocationId").GetString() ?? string.Empty;
            FileTransferPolicy.Identity(id); driver.InvocationId = id;
            FileTransferPipe.CompleteOpeningInput(driver.Process.StandardInput, action);
            return driver;
        }
        catch { driver.Dispose(); throw; }
    }
    private static async Task<JsonDocument> ReadJsonAsync(Driver driver, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream(65536);
        while (true)
        {
            if (driver.BufferCount == 0)
            {
                driver.BufferCount = await driver.Process.StandardOutput.BaseStream.ReadAsync(driver.Buffer, cancellationToken); driver.BufferOffset = 0;
                if (driver.BufferCount == 0) { await FinishAsync(driver, cancellationToken); throw new InvalidOperationException("FILES_OUTPUT_TRUNCATED"); }
            }
            var newline = driver.Buffer.AsSpan(driver.BufferOffset, driver.BufferCount).IndexOf((byte)'\n');
            var count = newline < 0 ? driver.BufferCount : newline;
            if (bytes.Length + count > 65536) throw new InvalidOperationException("FILES_OUTPUT_LIMIT");
            bytes.Write(driver.Buffer, driver.BufferOffset, count);
            var consumed = count + (newline < 0 ? 0 : 1); driver.BufferCount -= consumed; driver.BufferOffset += consumed;
            if (newline >= 0)
            {
                var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
                if ((driver.IsContainerRead || driver.IsContainerWrite || driver.IsContainerHistory || driver.IsContainerDirectory || driver.IsContainerCopy || driver.IsContainerMove) && document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "error")
                {
                    var code = document.RootElement.TryGetProperty("code", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                    document.Dispose();
                    throw driver.IsContainerMove ? ContainerMovePolicy.Failure(code) : driver.IsContainerCopy ? ContainerCopyPolicy.Failure(code) : driver.IsContainerDirectory ? ContainerDirectoryPolicy.Failure(code) : driver.IsContainerHistory ? ContainerHistoryPolicy.Failure(code) : driver.IsContainerWrite ? ContainerUploadPolicy.Failure(code) : ContainerFilePolicy.Failure(code);
                }
                return document;
            }
        }
    }
    private static async Task CaptureErrorsAsync(Driver driver, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096]; int count;
        while ((count = await driver.Process.StandardError.BaseStream.ReadAsync(buffer, cancellationToken)) > 0)
            if (driver.ErrorBuffer.Length < 4096) driver.ErrorBuffer.Write(buffer, 0, Math.Min(count, 4096 - (int)driver.ErrorBuffer.Length));
    }
    private static async Task FinishAsync(Driver driver, CancellationToken cancellationToken)
    {
        await driver.Process.WaitForExitAsync(cancellationToken); await driver.Errors;
        if (driver.Process.ExitCode == 0) return;
        // Fixed own error codes only. Never pass process stderr or exception objects.
        var code = "FILES_HELPER_REJECTED";
        foreach (var line in Encoding.UTF8.GetString(driver.ErrorBuffer.ToArray()).Split('\n'))
        {
            if (!line.StartsWith("{\"errorCode\":", StringComparison.Ordinal)) continue;
            try
            {
                using var error = JsonDocument.Parse(line); var candidate = error.RootElement.GetProperty("errorCode").GetString();
                if (candidate is { Length: <= 80 } && candidate.StartsWith("FILES_", StringComparison.Ordinal) && candidate.All(c => char.IsAsciiLetterUpper(c) || c == '_')) code = candidate;
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        throw new RpcException(new(code switch
        {
            "FILES_PROTECTED" or "FILES_ROOT_FORBIDDEN" or "FILES_ACCESS_DENIED" or "FILES_PATH_REJECTED" or "FILES_REGULAR_SINGLE_LINK_REQUIRED" or "FILES_TREE_PROTECTED" => StatusCode.PermissionDenied,
            "FILES_VERSION_CONFLICT" or "FILES_IDEMPOTENCY_CONFLICT" or "FILES_TREE_CHANGED" => StatusCode.Aborted,
            "FILES_OPERATION_BUSY" or "FILES_TRANSFER_LIMIT" or "FILES_RECEIPT_LIMIT" or "FILES_CAPACITY_REQUIRED" or "FILES_TREE_ENTRY_LIMIT" or "FILES_TREE_SIZE_LIMIT" or "FILES_TREE_DEPTH_LIMIT" or "FILES_TREE_MANIFEST_LIMIT" => StatusCode.ResourceExhausted,
            "FILES_TREE_DEADLINE" => StatusCode.DeadlineExceeded,
            "FILES_TREE_CANCELED" => StatusCode.Cancelled,
            "FILES_OPERATION_NOT_FOUND" or "FILES_NOT_FOUND" => StatusCode.NotFound,
            _ => StatusCode.FailedPrecondition
        }, code));
    }
    private static ProcessStartInfo Start(string action, string id, string? invocation = null)
    {
        var container = ContainerAction(action);
        var containerWrite = ContainerWriteAction(action);
        var history = ContainerHistoryAction(action);
        var directory = ContainerDirectoryAction(action);
        var copy = ContainerCopyAction(action);
        var move = ContainerMoveAction(action);
        var start = new ProcessStartInfo("/usr/bin/sudo") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-n"); start.ArgumentList.Add(Helper);
        var transport = move ? "container-file-moves" : copy ? "container-file-copies" : directory ? "container-file-directories" : history ? "container-file-history" : containerWrite ? "container-file-write" : container ? "container-file-transport" : "file-transport";
        start.ArgumentList.Add(invocation is null ? transport : transport + "-stop");
        if (invocation is null) start.ArgumentList.Add(move || copy ? action[15..] : directory ? action[20..] : history ? action[8..] : containerWrite ? action[16..] : container ? action[10..] : action);
        start.ArgumentList.Add(id); if (invocation is not null) start.ArgumentList.Add(invocation);
        start.Environment.Clear(); start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin"; start.Environment["LANG"] = "C.UTF-8";
        return start;
    }
    private sealed class Driver : IDisposable
    {
        private readonly string action;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public Process Process { get; }
        public byte[] Buffer { get; } = new byte[65536];
        public int BufferOffset { get; set; }
        public int BufferCount { get; set; }
        public MemoryStream ErrorBuffer { get; } = new(4096);
        public Task Errors { get; set; } = Task.CompletedTask;
        public string? InvocationId { get; set; }
        public bool IsContainerRead => ContainerAction(action);
        public bool IsContainerWrite => ContainerWriteAction(action);
        public bool IsContainerHistory => ContainerHistoryAction(action);
        public bool IsContainerDirectory => ContainerDirectoryAction(action);
        public bool IsContainerCopy => ContainerCopyAction(action);
        public bool IsContainerMove => ContainerMoveAction(action);
        public Driver(string action)
        {
            this.action = action;
            Process = new() { StartInfo = Start(action, Id) };
        }
        public void Dispose()
        {
            // Closing pipe endpoints also releases a driver whose Ready was not
            // received. Once Ready is bound, stop only that exact invocation.
            try { FileTransferPipe.AbortInput(Process.StandardInput); Process.StandardOutput.Close(); } catch (InvalidOperationException) { }
            if (InvocationId is not null)
            {
                using var stop = Process.Start(Start(action, Id, InvocationId));
                if (stop is not null)
                {
                    stop.StandardInput.Close();
                    var output = stop.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                    var errors = stop.StandardError.BaseStream.CopyToAsync(Stream.Null);
                    if (!stop.WaitForExit(10000)) throw new InvalidOperationException("FILES_STOP_TIMEOUT");
                    Task.WhenAll(output, errors).GetAwaiter().GetResult();
                    if (stop.ExitCode != 0) throw new InvalidOperationException("FILES_STOP_REJECTED");
                }
            }
            Process.Dispose(); ErrorBuffer.Dispose();
        }
    }
}

internal static class FileTransferPipe
{
    // Both receive modes keep stdin open until the exact body and commit EOF.
    // Metadata/publication actions have no body after their opening request.
    public static void CompleteOpeningInput(StreamWriter input, string action)
    {
        if (action is not ("upload" or "stage" or "container-write:stage")) input.Close();
    }
    // A helper may reject a stale version before consuming buffered input.
    // Close the underlying pipe first: disposal must not flush that buffer and
    // replace the authoritative rejection with a broken-pipe exception.
    public static void AbortInput(StreamWriter writer)
    {
        writer.BaseStream.Dispose();
        try { writer.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
