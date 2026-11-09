using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class DevelopmentGitGrpcService(BoundedProcessRunner processes) : DevelopmentGitService.DevelopmentGitServiceBase
{
    private const string Privileged = "/usr/local/libexec/whaledeck-privileged";
    private static readonly SemaphoreSlim Acquisition = new(1, 1);

    public override async Task Acquire(WorkspaceGitRequest request, IServerStreamWriter<WorkspaceGitFrame> responseStream,
        ServerCallContext context)
    {
        if (!DevelopmentGitBinding.IsValid(request))
            throw Error(StatusCode.InvalidArgument, "WORKSPACE_GIT_REQUEST_INVALID");
        if (!await Acquisition.WaitAsync(0, context.CancellationToken))
            throw Error(StatusCode.ResourceExhausted, "WORKSPACE_GIT_BUSY");

        try
        {
            var result = await BoundedProcessRunner.RunWithOutputAsync("/usr/bin/sudo",
                ["-n", Privileged, "development-git-acquire", request.CredentialRef.Length == 0 ? "-" : request.CredentialRef],
                DevelopmentGitBinding.HostInput(request), (stream, token) => ReadArchiveAsync(request, stream, responseStream, token),
                TimeSpan.FromSeconds(105), context.CancellationToken);
            if (result.ExitCode != 0) throw Rejected(result.Error);
            if (result.Output is null) throw Error(StatusCode.DataLoss, "WORKSPACE_GIT_STREAM_INVALID");
            // The caller can only receive all bytes after the helper exited cleanly.
            await responseStream.WriteAsync(new WorkspaceGitFrame { Content = result.Output }, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopAsync();
            throw Error(StatusCode.DeadlineExceeded, "WORKSPACE_GIT_DEADLINE");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or FormatException
                                     or KeyNotFoundException)
        {
            await StopAsync();
            throw Error(StatusCode.DataLoss, "WORKSPACE_GIT_STREAM_INVALID");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception)
        {
            await StopAsync();
            throw Error(StatusCode.Unavailable, "WORKSPACE_GIT_UNAVAILABLE");
        }
        finally { Acquisition.Release(); }
    }

    private async Task StopAsync()
    {
        try
        {
            await processes.RunAsync("/usr/bin/sudo", ["-n", Privileged, "development-git-stop"],
                null, TimeSpan.FromSeconds(6), "WORKSPACE_GIT_STOP_FAILED", CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            // The fixed transient unit still has an independent runtime limit.
        }
    }

    internal static async Task<ByteString?> ReadArchiveAsync(WorkspaceGitRequest request, Stream output,
        IServerStreamWriter<WorkspaceGitFrame> response, CancellationToken token)
    {
        using var buffered = new BufferedStream(output, DevelopmentSourceBinding.ChunkBytes);
        var header = new byte[DevelopmentGitBinding.MaximumHeaderBytes];
        var used = 0;
        for (; used < header.Length; used++)
        {
            if (await buffered.ReadAsync(header.AsMemory(used, 1), token) == 0)
                return used == 0 ? null : throw new InvalidDataException();
            if (header[used] == (byte)'\n') break;
        }
        if (used == header.Length) throw new InvalidDataException();
        var snapshot = ParseSnapshot(request, header.AsMemory(0, used));
        await response.WriteAsync(new WorkspaceGitFrame { Snapshot = snapshot }, token);

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[DevelopmentSourceBinding.ChunkBytes];
        var remaining = (int)snapshot.ArchiveBytes;
        while (true)
        {
            var count = Math.Min(remaining, chunk.Length);
            if (await buffered.ReadAtLeastAsync(chunk.AsMemory(0, count), count, false, token) != count)
                throw new InvalidDataException();
            digest.AppendData(chunk, 0, count);
            remaining -= count;
            if (remaining == 0)
            {
                if (await buffered.ReadAsync(header.AsMemory(0, 1), token) != 0 ||
                    Convert.ToHexStringLower(digest.GetHashAndReset()) != snapshot.ArchiveSha256)
                    throw new InvalidDataException();
                return ByteString.CopyFrom(chunk, 0, count);
            }
            await response.WriteAsync(new WorkspaceGitFrame { Content = ByteString.CopyFrom(chunk, 0, count) }, token);
        }
    }

    private static WorkspaceGitSnapshot ParseSnapshot(WorkspaceGitRequest request, ReadOnlyMemory<byte> header)
    {
        using var document = JsonDocument.Parse(header, new JsonDocumentOptions { MaxDepth = 2, AllowDuplicateProperties = false });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 9 ||
            root.GetProperty("archiveFormat").GetString() != "tar") throw new InvalidDataException();
        var snapshot = new WorkspaceGitSnapshot
        {
            WorkspaceId = request.Storage.WorkspaceId, ImportId = request.ImportId,
            RepositoryId = root.GetProperty("repositoryId").GetString() ?? "", RequestedRef = root.GetProperty("requestedRef").GetString() ?? "",
            ResolvedObject = root.GetProperty("resolvedObject").GetString() ?? "", Commit = root.GetProperty("commit").GetString() ?? "",
            ArchiveBytes = root.GetProperty("archiveBytes").GetUInt32(), ArchiveSha256 = root.GetProperty("archiveSha256").GetString() ?? "",
            FileCount = root.GetProperty("fileCount").GetUInt32(), TotalBytes = root.GetProperty("totalBytes").GetUInt64()
        };
        return DevelopmentGitBinding.Matches(request, snapshot) ? snapshot : throw new InvalidDataException();
    }

    private static RpcException Rejected(string stderr)
    {
        var code = stderr.Split('\n', 2)[0].Trim();
        return code switch
        {
            "WORKSPACE_GIT_REPOSITORY_NOT_APPROVED" or "WORKSPACE_GIT_CREDENTIAL_SCOPE" or
                "WORKSPACE_GIT_DESTINATION_NOT_APPROVED" => Error(StatusCode.PermissionDenied, code),
            "WORKSPACE_GIT_REF_CHANGED" => Error(StatusCode.Aborted, code),
            "WORKSPACE_GIT_REF_INVALID" or "WORKSPACE_GIT_SPECIAL_OBJECT" or "WORKSPACE_GIT_PATH_INVALID" or
                "WORKSPACE_GIT_TREE_INVALID" or "WORKSPACE_GIT_ENTRY_LIMIT" or "WORKSPACE_GIT_ARCHIVE_LIMIT" or
                "WORKSPACE_GIT_COMMIT_INVALID" or "WORKSPACE_GIT_HISTORY_NOT_SHALLOW" => Error(StatusCode.InvalidArgument, code),
            "WORKSPACE_GIT_REF_UNAVAILABLE" => Error(StatusCode.NotFound, code),
            "WORKSPACE_GIT_DEADLINE" => Error(StatusCode.DeadlineExceeded, code),
            "WORKSPACE_GIT_CREDENTIAL_UNAVAILABLE" or "WORKSPACE_GIT_CREDENTIAL_INVALID" or
                "WORKSPACE_GIT_REGISTRY_UNSAFE" or "WORKSPACE_GIT_REGISTRY_INVALID" or
                "WORKSPACE_GIT_DNS_UNAVAILABLE" or "WORKSPACE_GIT_FETCH_FAILED" => Error(StatusCode.Unavailable, code),
            _ => Error(StatusCode.Unavailable, "WORKSPACE_GIT_UNAVAILABLE")
        };
    }

    private static RpcException Error(StatusCode status, string code) => new(new Status(status, code));
}
