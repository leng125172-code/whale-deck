using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerMoveAction(string action)
    {
        if (action is "container-move:plan" or "container-move:apply" or "container-move:inspect" or "container-move:discard") return true;
        if (action.StartsWith("container-move:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_MOVE_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerMoveOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_MOVE_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(630));
        using var driver = await StartAsync("container-move:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerMove || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_MOVE_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_MOVE_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token); return result;
        }
        catch { result.Dispose(); throw; }
    }
}
