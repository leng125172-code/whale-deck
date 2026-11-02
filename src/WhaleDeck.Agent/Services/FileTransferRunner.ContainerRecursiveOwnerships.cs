using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecursiveOwnershipAction(string action)
    {
        if (action is "container-recursive-ownership:plan" or "container-recursive-ownership:apply" or "container-recursive-ownership:inspect" or "container-recursive-ownership:discard") return true;
        if (action.StartsWith("container-recursive-ownership:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerRecursiveOwnershipOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(320));
        using var driver = await StartAsync("container-recursive-ownership:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerRecursiveOwnership || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_RECURSIVE_OWNERSHIP_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
