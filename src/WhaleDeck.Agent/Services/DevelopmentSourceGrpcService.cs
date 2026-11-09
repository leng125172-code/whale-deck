using System.ComponentModel;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentSourceGrpcService : DevelopmentSourceService.DevelopmentSourceServiceBase
{
    private static readonly SemaphoreSlim Helpers = new(4, 4);
    private static readonly SemaphoreSlim Uploads = new(2, 2);

    public override async Task<WorkspaceSourceReceipt> Stage(IAsyncStreamReader<WorkspaceSourceFrame> requestStream, ServerCallContext context)
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

    public override Task<WorkspaceSourceReceipt> Publish(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "publish", context.CancellationToken);

    public override Task<WorkspaceSourceReceipt> Discard(WorkspaceSourceRequest request, ServerCallContext context) =>
        ExecuteAsync(request, context.Method, "discard", context.CancellationToken);

    private static async Task<WorkspaceSourceReceipt> ExecuteAsync(WorkspaceSourceRequest request, string method,
        string action, CancellationToken token, Func<Stream, CancellationToken, Task>? pump = null)
    {
        if (!DevelopmentSourceBinding.IsValid(request, method))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_SOURCE_REQUEST_INVALID");
        if (!await Helpers.WaitAsync(0, token))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_SOURCE_BUSY");
        try
        {
            var result = await BoundedProcessRunner.RunWithInputAsync("/usr/bin/sudo",
                ["-n", "/usr/local/libexec/whaledeck-privileged", "development-source-" + action, Guid.NewGuid().ToString("N")],
                async (input, lifetime) =>
                {
                    await input.WriteAsync(DevelopmentSourceBinding.HostInput(request), lifetime);
                    await input.WriteAsync("\n"u8.ToArray(), lifetime);
                    if (pump is not null) await pump(input, lifetime);
                }, TimeSpan.FromSeconds(pump is null ? 20 : 110), token);
            token.ThrowIfCancellationRequested();
            return result.ExitCode == 0 ? ParseReceipt(request, result.StandardOutput, method) : throw Rejected(result.StandardError);
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
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            var stage = method == DevelopmentSourceBinding.Stage;
            if (root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != (stage ? 10 : 11) ||
                root.EnumerateObject().Count() != (stage ? 10 : 11))
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
                    "WORKSPACE_SOURCE_DISCARDED" => StatusCode.Aborted,
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
                "WORKSPACE_SOURCE_TAR_INVALID" or "WORKSPACE_SOURCE_REJECTED" => StatusCode.InvalidArgument,
                _ => StatusCode.OK
            };
            if (status != StatusCode.OK) return Error(status, line);
        }
        return Error(StatusCode.Unavailable, "WORKSPACE_SOURCE_HELPER_UNAVAILABLE");
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
