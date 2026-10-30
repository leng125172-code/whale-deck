using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerOwnershipAction(string action)
    {
        if (action is "container-ownership:plan" or "container-ownership:apply" or "container-ownership:inspect" or "container-ownership:discard") return true;
        if (action.StartsWith("container-ownership:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_OWNERSHIP_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerOwnershipOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_OWNERSHIP_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        using var driver = await StartAsync("container-ownership:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerOwnership || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_OWNERSHIP_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_OWNERSHIP_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
