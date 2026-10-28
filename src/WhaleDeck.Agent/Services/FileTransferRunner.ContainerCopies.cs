using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerCopyAction(string action)
    {
        if (action is "container-copy:plan" or "container-copy:apply" or "container-copy:inspect" or "container-copy:discard") return true;
        if (action.StartsWith("container-copy:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_COPY_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerCopyOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_COPY_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(630));
        using var driver = await StartAsync("container-copy:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerCopy || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_COPY_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_COPY_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token); return result;
        }
        catch { result.Dispose(); throw; }
    }
}
