using System.Security.Cryptography;
using Grpc.Core;
using WhaleDeck.Application.Development;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway
{
    public async Task AcquireGitAsync(WorkspaceSourceClaim claim, Func<WorkspaceGitProof, CancellationToken, Task> bind,
        Func<int, byte[], CancellationToken, Task> append, CancellationToken token)
    {
        var git = claim.Import.Git ?? throw GitIntegrity();
        if (claim.Import.WorkspaceId != claim.Workspace.Id || claim.Actor.Subject != claim.Workspace.OwnerSubject ||
            claim.Workspace.State != WorkspaceStorageAllocation.Allocated) throw GitIntegrity();
        var request = new WorkspaceGitRequest
        {
            Storage = SourceStorage(claim), ImportId = claim.Import.ImportId.ToString("N"), RepositoryId = git.RepositoryId,
            Ref = git.Snapshot?.ResolvedObject ?? git.Ref, CredentialRef = claim.GitCredentialRef ?? ""
        };
        if (!DevelopmentGitBinding.IsValid(request)) throw GitIntegrity();
        try
        {
            var caps = await _agent.GetCapabilitiesAsync(new Google.Protobuf.WellKnownTypes.Empty(),
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
            if (!DevelopmentGitBinding.IsSupported(caps))
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Unsupported);
            var headers = SourceHeaders(DevelopmentGitBinding.Acquire, DevelopmentGitBinding.Hash(request), DevelopmentGitBinding.Payload);
            var client = new DevelopmentGitService.DevelopmentGitServiceClient(_channel);
            using var call = client.Acquire(request, headers, DateTime.UtcNow.AddSeconds(111), token);
            var stream = call.ResponseStream;
            if (!await stream.MoveNext(token) || stream.Current.Snapshot is not { } snapshot ||
                !DevelopmentGitBinding.Matches(request, snapshot)) throw GitIntegrity();
            await bind(new(snapshot.ResolvedObject, snapshot.Commit, new("tar", (int)snapshot.ArchiveBytes, snapshot.ArchiveSha256),
                (int)snapshot.FileCount, (long)snapshot.TotalBytes), token);
            await ReceiveGitArchiveAsync(stream, snapshot, append, token);
        }
        catch (RpcException error)
        {
            token.ThrowIfCancellationRequested();
            throw new WorkspaceSourceException(error.StatusCode switch
            {
                StatusCode.Aborted => WorkspaceSourceFailure.Conflict,
                StatusCode.NotFound or StatusCode.InvalidArgument => WorkspaceSourceFailure.Rejected,
                StatusCode.PermissionDenied when error.Status.Detail.StartsWith("WORKSPACE_GIT_", StringComparison.Ordinal) =>
                    WorkspaceSourceFailure.Rejected,
                StatusCode.PermissionDenied or StatusCode.Unimplemented => WorkspaceSourceFailure.Unsupported,
                StatusCode.DataLoss => WorkspaceSourceFailure.Integrity,
                _ => WorkspaceSourceFailure.Retryable
            });
        }
    }

    private static async Task ReceiveGitArchiveAsync(IAsyncStreamReader<WorkspaceGitFrame> stream, WorkspaceGitSnapshot snapshot,
        Func<int, byte[], CancellationToken, Task> append, CancellationToken token)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var size = (int)snapshot.ArchiveBytes;
        var offset = 0;
        var filled = 0;
        var buffer = new byte[Math.Min(size, WorkspaceSources.ChunkBytes)];
        try
        {
            while (await stream.MoveNext(token))
            {
                if (stream.Current.PayloadCase != WorkspaceGitFrame.PayloadOneofCase.Content ||
                    stream.Current.Content.Length is < 1 or > DevelopmentSourceBinding.ChunkBytes ||
                    stream.Current.Content.Length > size - offset - filled) throw GitIntegrity();
                var frame = stream.Current.Content;
                digest.AppendData(frame.Span);
                for (var consumed = 0; consumed < frame.Length;)
                {
                    var count = Math.Min(buffer.Length - filled, frame.Length - consumed);
                    frame.Span.Slice(consumed, count).CopyTo(buffer.AsSpan(filled));
                    filled += count;
                    consumed += count;
                    if (filled != buffer.Length || offset + filled == size) continue;
                    await append(offset, buffer, token);
                    CryptographicOperations.ZeroMemory(buffer);
                    offset += filled;
                    filled = 0;
                    if (size - offset < buffer.Length) buffer = new byte[size - offset];
                }
            }
            if (offset + filled != size || Convert.ToHexStringLower(digest.GetHashAndReset()) != snapshot.ArchiveSha256)
                throw GitIntegrity();
            await append(offset, buffer, token);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static WorkspaceSourceException GitIntegrity() => new(WorkspaceSourceFailure.Integrity);
}
