using System.Text;
using System.Text.Json;
using Grpc.Core;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecycleArchiveRetirementAction(string action)
    {
        if (action is "container-recycle-archive-retirement:plan" or "container-recycle-archive-retirement:reserve" or "container-recycle-archive-retirement:apply" or "container-recycle-archive-retirement:inspect" or "container-recycle-archive-retirement:discard") return true;
        if (action.StartsWith("container-recycle-archive-retirement:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID");
        return false;
    }

    public async Task<JsonDocument> ContainerRecycleArchiveRetirementOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (!ContainerRecycleArchiveRetirementAction("container-recycle-archive-retirement:" + action)) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID");
        if (Encoding.UTF8.GetByteCount(input) > 65536 || input.Contains('\n') || input.Contains('\r')) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INPUT_INVALID");
        cancellationToken.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using var driver = await StartAsync("container-recycle-archive-retirement:" + action, input, lifetime.Token);
            var result = await ReadJsonAsync(driver, lifetime.Token);
            try
            {
                if (!driver.IsContainerRecycleArchiveRetirement || driver.BufferCount != 0 || await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                    throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TRAILING_OUTPUT");
                await FinishAsync(driver, lifetime.Token);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new RpcException(new(StatusCode.DeadlineExceeded, "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TIMEOUT")); }
    }
}
