using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class DevelopmentSourceGrpcService(BoundedProcessRunner processes) : DevelopmentSourceService.DevelopmentSourceServiceBase
{
    private const string Privileged = "/usr/local/libexec/whaledeck-privileged";
    private static readonly SemaphoreSlim Helpers = new(4, 4);
    private static readonly SemaphoreSlim Uploads = new(2, 2);
    private static readonly SemaphoreSlim Exports = new(1, 1);

    public override Task<WorkspaceSourceReceipt> Stage(IAsyncStreamReader<WorkspaceSourceFrame> requestStream, ServerCallContext context) =>
        ReceiveAsync(requestStream, context);

    public override Task<WorkspaceSourceReceipt> StageUpdate(IAsyncStreamReader<WorkspaceSourceFrame> requestStream, ServerCallContext context) =>
        ReceiveAsync(requestStream, context);

    private static async Task<WorkspaceSourceReceipt> ReceiveAsync(IAsyncStreamReader<WorkspaceSourceFrame> requestStream, ServerCallContext context)
    {
        using var first = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        first.CancelAfter(TimeSpan.FromSeconds(5));
        if (!await requestStream.MoveNext(first.Token) || requestStream.Current.Header is not { } request)
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_HEADER_REQUIRED");
        if (request.CalculateSize() > DevelopmentSourceBinding.MaximumHeaderBytes ||
            context.RequestHeaders.GetValue(DevelopmentSourceBinding.Header) != DevelopmentSourceBinding.Hash(request))
            throw Error(StatusCode.PermissionDenied, "WORKSPACE_SOURCE_BINDING_INVALID");
        if (!await Uploads.WaitAsync(0, context.CancellationToken))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_SOURCE_BUSY");
        try
        {
            return await ExecuteAsync(request, context.Method, "stage", context.CancellationToken,
                (input, token) => PumpAsync(requestStream, input, request.ArchiveBytes, token));
        }
        finally { Uploads.Release(); }
    }

    public override Task<WorkspaceSourceReceipt> Inspect(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "inspect", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> InspectUpdate(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "inspect", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> Publish(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "publish", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> PublishUpdate(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "publish", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> Discard(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "discard", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> DiscardUpdate(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "discard", context.CancellationToken);

    public override Task<Empty> Abort(WorkspaceSourceRequest request, ServerCallContext context) => AbortAsync(request, context);

    public override Task<Empty> AbortUpdate(WorkspaceSourceRequest request, ServerCallContext context) => AbortAsync(request, context);

    public override async Task ExportPrevious(WorkspaceSourceRequest request,
        IServerStreamWriter<WorkspaceSourceExportFrame> responseStream, ServerCallContext context)
    {
        if (!DevelopmentSourceBinding.IsValid(request, context.Method))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_REQUEST_INVALID");
        if (!await Exports.WaitAsync(0, context.CancellationToken))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_SOURCE_BUSY");
        var unit = Guid.NewGuid().ToString("N");
        try
        {
            byte[] input = [.. DevelopmentSourceBinding.HostInput(request), (byte)'\n'];
            var result = await BoundedProcessRunner.RunWithOutputAsync("/usr/bin/sudo",
                ["-n", Privileged, "development-source-export", unit], input,
                (output, token) => ReadPreviousAsync(output, responseStream, token),
                TimeSpan.FromSeconds(110), context.CancellationToken);
            if (result.ExitCode != 0) throw Rejected(result.Error);
            if (result.Output is not { } export || export.Length < 10240 || export.Length % 512 != 0)
                throw Error(StatusCode.DataLoss, "WORKSPACE_SOURCE_STREAM_INVALID");
            await responseStream.WriteAsync(new() { Offset = export.Length - (ulong)export.Tail.Length, Content = export.Tail },
                context.CancellationToken);
            await responseStream.WriteAsync(new() { Offset = export.Length, ArchiveSha256 = export.Sha256 },
                context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopReadAsync("export", unit);
            throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_SOURCE_DEADLINE");
        }
        catch (InvalidDataException)
        {
            await StopReadAsync("export", unit);
            throw Error(StatusCode.DataLoss, "WORKSPACE_SOURCE_STREAM_INVALID");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        {
            await StopReadAsync("export", unit);
            throw Error(StatusCode.Unavailable, "WORKSPACE_SOURCE_HELPER_UNAVAILABLE");
        }
        finally { Exports.Release(); }
    }

    private async Task StopReadAsync(string action, string unit)
    {
        try
        {
            await processes.RunAsync("/usr/bin/sudo", ["-n", Privileged, "development-source-" + action + "-stop", unit],
                null, TimeSpan.FromSeconds(6), "WORKSPACE_SOURCE_READ_STOP_FAILED", CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            // The uniquely named transient unit also has its own deadline.
        }
    }

    internal sealed record PreviousArchive(ByteString Tail, ulong Length, string Sha256);

    internal static async Task<PreviousArchive?> ReadPreviousAsync(Stream output,
        IServerStreamWriter<WorkspaceSourceExportFrame> response, CancellationToken token)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[DevelopmentSourceBinding.ChunkBytes];
        ulong length = 0;
        ByteString? tail = null;
        while (true)
        {
            var size = await output.ReadAtLeastAsync(buffer, buffer.Length, false, token);
            if (size == 0) break;
            if ((ulong)size > DevelopmentSourceBinding.MaximumPreviousArchiveBytes - length)
                throw new InvalidDataException();
            if (tail is not null)
                await response.WriteAsync(new() { Offset = length - (ulong)tail.Length, Content = tail }, token);
            length += (ulong)size;
            digest.AppendData(buffer, 0, size);
            tail = ByteString.CopyFrom(buffer, 0, size);
        }
        // The caller sends this final chunk and digest only after successful helper exit.
        return tail is null ? null : new(tail, length, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    private static async Task<Empty> AbortAsync(WorkspaceSourceRequest request, ServerCallContext context)
    {
        var output = await RunAsync(request, context.Method, "abort", null, context.CancellationToken);
        if (output.Trim() != """{"state":"Aborted"}""")
            throw Error(StatusCode.DataLoss, "WORKSPACE_SOURCE_RECEIPT_INVALID");
        return new();
    }

    private static async Task<WorkspaceSourceReceipt> ExecuteAsync(WorkspaceSourceRequest request, string method,
        string action, CancellationToken token, Func<Stream, CancellationToken, Task>? pump = null) =>
        ParseReceipt(request, await RunAsync(request, method, action, pump, token), method);

    private static Task<string> RunAsync(WorkspaceSourceRequest request, string method,
        string action, Func<Stream, CancellationToken, Task>? pump, CancellationToken token)
    {
        if (!DevelopmentSourceBinding.IsValid(request, method))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_REQUEST_INVALID");
        return RunHostAsync(DevelopmentSourceBinding.HostInput(request), action, Guid.NewGuid().ToString("N"), pump,
            pump is not null || method == DevelopmentSourceBinding.PublishUpdate ? 110 : 20, token);
    }

    private static async Task<string> RunHostAsync(byte[] header, string action, string unit,
        Func<Stream, CancellationToken, Task>? pump, int timeoutSeconds, CancellationToken token)
    {
        if (!await Helpers.WaitAsync(0, token))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_SOURCE_BUSY");
        try
        {
            var result = await BoundedProcessRunner.RunWithInputAsync("/usr/bin/sudo",
                ["-n", Privileged, "development-source-" + action, unit],
                async (input, lifetime) =>
                {
                    await input.WriteAsync(header, lifetime);
                    await input.WriteAsync("\n"u8.ToArray(), lifetime);
                    if (pump is not null) await pump(input, lifetime);
                }, TimeSpan.FromSeconds(timeoutSeconds), token);
            token.ThrowIfCancellationRequested();
            return result.ExitCode == 0 ? result.StandardOutput : throw Rejected(result.StandardError);
        }
        catch (OperationCanceledException) { throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_SOURCE_DEADLINE"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        { throw Error(StatusCode.Unavailable, "WORKSPACE_SOURCE_HELPER_UNAVAILABLE"); }
        finally { Helpers.Release(); }
    }

    internal static async Task PumpAsync(IAsyncStreamReader<WorkspaceSourceFrame> stream, Stream input, uint size,
        CancellationToken token)
    {
        var remaining = (int)size;
        ByteString? final = null;
        while (await stream.MoveNext(token))
        {
            var frame = stream.Current;
            var bytes = frame.Content;
            if (frame.PayloadCase != WorkspaceSourceFrame.PayloadOneofCase.Content ||
                bytes.Length is 0 or > DevelopmentSourceBinding.ChunkBytes || bytes.Length > remaining)
                throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_STREAM_INVALID");
            remaining -= bytes.Length;
            if (remaining == 0)
            {
                // The helper validates length, hash and EOF. Withhold the final
                // byte so cancellation cannot turn an incomplete RPC into a valid upload.
                final = bytes;
                await input.WriteAsync(bytes.Memory[..^1], token);
            }
            else
            {
                await input.WriteAsync(bytes.Memory, token);
            }
        }
        token.ThrowIfCancellationRequested();
        if (final is null) throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_STREAM_INCOMPLETE");
        await input.WriteAsync(final.Memory[^1..], token);
    }

    internal static WorkspaceSourceReceipt ParseReceipt(WorkspaceSourceRequest request, string output, string method)
    {
        try
        {
            if (output.Length > 2048) throw new JsonException();
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2, AllowDuplicateProperties = false });
            var root = document.RootElement;
            var stage = DevelopmentSourceBinding.IsStage(method);
            var update = DevelopmentSourceBinding.IsUpdate(method);
            if (root.EnumerateObject().Count() != (stage ? 10 : 11) + (update ? 2 : 0))
                throw new JsonException();
            var receipt = new WorkspaceSourceReceipt
            {
                WorkspaceId = root.GetProperty("workspaceId").GetString()!,
                StorageSha256 = root.GetProperty("storageSha256").GetString()!,
                ImportId = root.GetProperty("importId").GetString()!,
                ArchiveFormat = root.GetProperty("archiveFormat").GetString()!,
                ArchiveBytes = root.GetProperty("archiveBytes").GetUInt32(),
                ArchiveSha256 = root.GetProperty("archiveSha256").GetString()!,
                PlanSha256 = root.GetProperty("planSha256").GetString()!,
                ContentSha256 = root.GetProperty("contentSha256").GetString()!,
                FileCount = root.GetProperty("fileCount").GetUInt32(),
                TotalBytes = root.GetProperty("totalBytes").GetUInt64(),
                State = stage ? "Staged" : root.GetProperty("state").GetString()!
            };
            if (update)
            {
                var details = root.GetProperty("update");
                if (details.EnumerateObject().Count() != 6) throw new JsonException();
                receipt.PreviousImportId = root.GetProperty("previousImportId").GetString()!;
                receipt.Update = new()
                {
                    PreviousContentSha256 = details.GetProperty("previousContentSha256").GetString()!,
                    WorkContentSha256 = details.GetProperty("workContentSha256").GetString()!,
                    Dirty = details.GetProperty("dirty").GetBoolean(),
                    AddedEntries = details.GetProperty("addedEntries").GetUInt32(),
                    RemovedEntries = details.GetProperty("removedEntries").GetUInt32(),
                    ChangedEntries = details.GetProperty("changedEntries").GetUInt32()
                };
            }
            return DevelopmentSourceBinding.Matches(request, receipt, method) ? receipt : throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Error(StatusCode.DataLoss, "WORKSPACE_SOURCE_RECEIPT_INVALID"); }
    }

    private static RpcException Rejected(string stderr)
    {
        foreach (var line in stderr.Split('\n', StringSplitOptions.TrimEntries))
        {
            var status = line switch
            {
                "WORKSPACE_SOURCE_NOT_FOUND" => StatusCode.NotFound,
                "WORKSPACE_SOURCE_DEADLINE" => StatusCode.DeadlineExceeded,
                "WORKSPACE_SOURCE_BUSY" or "WORKSPACE_STORAGE_BUSY" => StatusCode.Unavailable,
                "WORKSPACE_SOURCE_CAPACITY" => StatusCode.ResourceExhausted,
                "WORKSPACE_SOURCE_IMPORT_CONFLICT" or "WORKSPACE_SOURCE_PLAN_CONFLICT" or
                    "WORKSPACE_SOURCE_WORK_ALREADY_PRESENT" or "WORKSPACE_SOURCE_ALREADY_PUBLISHED" or
                    "WORKSPACE_SOURCE_WORK_BASE_CONFLICT" or "WORKSPACE_SOURCE_WORK_CHANGED" or
                    "WORKSPACE_SOURCE_WORK_DIRTY" or "WORKSPACE_SOURCE_UPDATE_CHOICE_CONFLICT" or
                    "WORKSPACE_SOURCE_DISCARDED" or "WORKSPACE_SOURCE_DIFF_UNAVAILABLE" => StatusCode.Aborted,
                "WORKSPACE_STORAGE_NOT_PUBLISHED" => StatusCode.FailedPrecondition,
                "WORKSPACE_SOURCE_PLAN_CHANGED" or "WORKSPACE_SOURCE_STAGE_CHANGED" or
                    "WORKSPACE_SOURCE_RECEIPT_CHANGED" or "WORKSPACE_SOURCE_PUBLICATION_UNCONFIRMED" or
                    "WORKSPACE_RUN_CLEANUP_UNCONFIRMED" => StatusCode.DataLoss,
                "WORKSPACE_SOURCE_REQUEST_INVALID" or "WORKSPACE_SOURCE_ARCHIVE_CHANGED" or
                    "WORKSPACE_SOURCE_ARCHIVE_EMPTY" or "WORKSPACE_SOURCE_PATH_INVALID" or
                    "WORKSPACE_SOURCE_PATH_CONFLICT" or "WORKSPACE_SOURCE_PROTECTED_PATH" or
                    "WORKSPACE_SOURCE_CREDENTIAL_CONTENT" or "WORKSPACE_SOURCE_ZIP_INVALID" or
                    "WORKSPACE_SOURCE_ZIP_ENTRY_INVALID" or "WORKSPACE_SOURCE_DEPTH_LIMIT" or
                    "WORKSPACE_SOURCE_EXPANSION_LIMIT" or "WORKSPACE_SOURCE_ENTRY_LIMIT" or
                    "WORKSPACE_SOURCE_WORK_SCAN_LIMIT" or "WORKSPACE_SOURCE_WORK_UNSUPPORTED" or
                    "WORKSPACE_SOURCE_TAR_INVALID" or "WORKSPACE_SOURCE_DIFF_CURSOR_INVALID" or "WORKSPACE_SOURCE_REJECTED" => StatusCode.InvalidArgument,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK) return Error(status, line);
        }
        return Error(StatusCode.Unavailable, "WORKSPACE_SOURCE_HELPER_UNAVAILABLE");
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
