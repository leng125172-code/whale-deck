using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DevelopmentSourceRpcTests
{
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
}
