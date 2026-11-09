using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DevelopmentGitRpcTests
{
    private static readonly WorkspaceGitRequest Request = new()
    {
        Storage = new() { WorkspaceId = "95037670-d4b5-437b-9da2-05d97e4184ca" },
        ImportId = new string('1', 32), RepositoryId = "fixture", Ref = "refs/heads/main"
    };

    [Theory]
    [InlineData("exact")]
    [InlineData("short")]
    [InlineData("extra")]
    [InlineData("hash")]
    [InlineData("ref")]
    public async Task HeaderAndFinalChunkAreBoundToCompleteSnapshot(string mode)
    {
        var body = new byte[DevelopmentSourceBinding.ChunkBytes + 2];
        body[^1] = 9;
        var header = JsonSerializer.SerializeToUtf8Bytes(new
        {
            repositoryId = Request.RepositoryId,
            requestedRef = mode == "ref" ? "HEAD" : Request.Ref,
            resolvedObject = new string('a', 40), commit = new string('a', 40),
            archiveFormat = "tar", archiveBytes = body.Length,
            archiveSha256 = mode == "hash" ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(body)),
            fileCount = 1, totalBytes = 1
        });
        using var input = new MemoryStream();
        input.Write(header);
        input.WriteByte((byte)'\n');
        input.Write(mode == "short" ? body.AsSpan(0, body.Length - 1) : body);
        if (mode == "extra") input.WriteByte(1);
        input.Position = 0;
        var responses = new Responses();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = DevelopmentGitGrpcService.ReadArchiveAsync(Request, input, responses, timeout.Token);

        if (mode == "exact")
        {
            var final = await pending;
            Assert.Equal(ByteString.CopyFrom(body, body.Length - 2, 2), final);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => pending);
        }

        if (mode == "ref") Assert.Empty(responses.Frames);
        else
        {
            Assert.Equal(2, responses.Frames.Count);
            Assert.Equal(Request.ImportId, responses.Frames[0].Snapshot.ImportId);
            Assert.Equal(DevelopmentSourceBinding.ChunkBytes, responses.Frames[1].Content.Length);
        }
    }

    private sealed class Responses : IServerStreamWriter<WorkspaceGitFrame>
    {
        public List<WorkspaceGitFrame> Frames { get; } = [];
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(WorkspaceGitFrame message)
        {
            Frames.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(WorkspaceGitFrame message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WriteAsync(message);
        }
    }
}
