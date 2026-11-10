using Google.Protobuf;
using Grpc.Core;
using System.Security.Cryptography;
using System.Text.Json;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DevelopmentSourceRpcTests
{
    [Fact]
    public void UpdateScopeAndChoiceCannotCrossMethodBoundaries()
    {
        var request = UpdateRequest();
        Assert.True(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.StageUpdate));
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.Stage));
        request.PlanSha256 = new string('a', 64);
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.PublishUpdate));
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.ExportPrevious));
        request.ConflictResolution = WorkspaceSourceConflictResolution.RetainAndReplace;
        Assert.True(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.PublishUpdate));
        Assert.True(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.ExportPrevious));
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.DiscardUpdate));
        using var payload = JsonDocument.Parse(DevelopmentSourceBinding.HostInput(request));
        Assert.Equal("retain-and-replace", payload.RootElement.GetProperty("conflictResolution").GetString());
        Assert.Equal(request.PreviousImportId, payload.RootElement.GetProperty("previousImportId").GetString());
        request.PreviousImportId = request.ImportId;
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.PublishUpdate));
        Assert.False(DevelopmentSourceBinding.IsValid(request, DevelopmentSourceBinding.ExportPrevious));
    }

    [Fact]
    public async Task PreviousExportWithholdsTailAndBindsOffsetsAndDigest()
    {
        var bytes = new byte[DevelopmentSourceBinding.ChunkBytes * 2 + 512];
        RandomNumberGenerator.Fill(bytes);
        using var input = new MemoryStream(bytes, writable: false);
        var responses = new ExportResponses();
        var complete = await DevelopmentSourceGrpcService.ReadPreviousAsync(input, responses, CancellationToken.None);

        Assert.NotNull(complete);
        Assert.Equal((ulong)bytes.Length, complete.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), complete.Sha256);
        Assert.Equal(bytes[^512..], complete.Tail.ToByteArray());
        Assert.Equal(2, responses.Frames.Count);
        for (var i = 0; i < responses.Frames.Count; i++)
        {
            var offset = i * DevelopmentSourceBinding.ChunkBytes;
            Assert.Equal((ulong)offset, responses.Frames[i].Offset);
            Assert.Equal(bytes.AsSpan(offset, DevelopmentSourceBinding.ChunkBytes).ToArray(), responses.Frames[i].Content.ToByteArray());
        }
    }

    [Fact]
    public void UpdateReceiptRequiresMatchingBaselineAndUniqueSummaryFields()
    {
        var request = UpdateRequest();
        using var input = JsonDocument.Parse(DevelopmentSourceBinding.HostInput(request));
        var storageHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(input.RootElement.GetProperty("storage"))));
        var receipt = JsonSerializer.Serialize(new
        {
            workspaceId = request.Storage.WorkspaceId, storageSha256 = storageHash, importId = request.ImportId,
            previousImportId = request.PreviousImportId, archiveFormat = "tar", archiveBytes = request.ArchiveBytes,
            archiveSha256 = request.ArchiveSha256, planSha256 = new string('a', 64), contentSha256 = new string('b', 64),
            fileCount = 1, totalBytes = 1,
            update = new { previousContentSha256 = new string('d', 64), workContentSha256 = new string('e', 64),
                dirty = true, addedEntries = 1, removedEntries = 1, changedEntries = 0 }
        });
        var parsed = DevelopmentSourceGrpcService.ParseReceipt(request, receipt, DevelopmentSourceBinding.StageUpdate);
        Assert.True(parsed.Update.Dirty);
        Assert.Equal(request.PreviousImportId, parsed.PreviousImportId);
        var duplicate = receipt.Replace("\"dirty\":true", "\"dirty\":false,\"dirty\":true", StringComparison.Ordinal);
        Assert.Equal(StatusCode.DataLoss, Assert.Throws<RpcException>(() =>
            DevelopmentSourceGrpcService.ParseReceipt(request, duplicate, DevelopmentSourceBinding.StageUpdate)).StatusCode);
        request.PreviousImportId = new string('3', 32);
        Assert.Equal(StatusCode.DataLoss, Assert.Throws<RpcException>(() =>
            DevelopmentSourceGrpcService.ParseReceipt(request, receipt, DevelopmentSourceBinding.StageUpdate)).StatusCode);
    }

    private static WorkspaceSourceRequest UpdateRequest() => new()
    {
        Storage = new()
        {
            WorkspaceId = "95037670-d4b5-437b-9da2-05d97e4184ca", StorageRootId = "guest-qa",
            EnvironmentSha256 = new string('f', 64), DiskMib = 256, ActorSubject = "qa", TraceId = "qa"
        },
        ImportId = new string('1', 32), PreviousImportId = new string('2', 32), ArchiveFormat = "tar",
        ArchiveBytes = 10240, ArchiveSha256 = new string('c', 64)
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullByteCountReachesHostOnlyAfterCleanStreamEof(bool extraFrame)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = timeout.Token;
        var content = ByteString.CopyFromUtf8("archive");
        using var input = new MemoryStream();
        var frames = new StreamEnding(content, extraFrame);
        var pump = DevelopmentSourceGrpcService.PumpAsync(frames, input, (uint)content.Length, token);
        await frames.ReadingEnd.Task.WaitAsync(token);
        Assert.Equal("archiv"u8.ToArray(), input.ToArray());
        frames.End.SetResult();
        if (extraFrame)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => pump);
            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
            Assert.Equal("archiv"u8.ToArray(), input.ToArray());
        }
        else
        {
            await pump;
            Assert.Equal(content.ToByteArray(), input.ToArray());
        }
    }

    [Fact]
    public async Task CancelledEndCannotCompleteHostInput()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var content = ByteString.CopyFromUtf8("archive");
        using var input = new MemoryStream();
        var frames = new StreamEnding(content, false);
        var pump = DevelopmentSourceGrpcService.PumpAsync(frames, input, (uint)content.Length, cancellation.Token);
        await frames.ReadingEnd.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);
        Assert.Equal("archiv"u8.ToArray(), input.ToArray());
    }

    private sealed class StreamEnding(ByteString body, bool extraFrame) : IAsyncStreamReader<WorkspaceSourceFrame>
    {
        public WorkspaceSourceFrame Current { get; } = new() { Content = body };
        public TaskCompletionSource ReadingEnd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;
                return true;
            }
            ReadingEnd.SetResult();
            await End.Task.WaitAsync(cancellationToken);
            return extraFrame;
        }
    }

    private sealed class ExportResponses : IServerStreamWriter<WorkspaceSourceExportFrame>
    {
        public List<WorkspaceSourceExportFrame> Frames { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(WorkspaceSourceExportFrame message)
        {
            Frames.Add(message);
            return Task.CompletedTask;
        }
    }
}
