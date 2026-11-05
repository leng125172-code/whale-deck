using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecycleAction(string action)
    {
        if (action is "container-recycle:plan" or "container-recycle:apply" or "container-recycle:inspect" or "container-recycle:discard" or "container-recycle:restore") return true;
        if (action.StartsWith("container-recycle:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECYCLE_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerRecycleOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard" or "restore")) throw new ArgumentException("CONTAINER_RECYCLE_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        using var driver = await StartAsync("container-recycle:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerRecycle || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_RECYCLE_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_RECYCLE_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
