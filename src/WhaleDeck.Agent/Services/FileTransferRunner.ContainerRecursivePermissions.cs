using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecursivePermissionAction(string action)
    {
        if (action is "container-recursive-permission:plan" or "container-recursive-permission:apply" or "container-recursive-permission:inspect" or "container-recursive-permission:discard") return true;
        if (action.StartsWith("container-recursive-permission:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerRecursivePermissionOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(320));
        using var driver = await StartAsync("container-recursive-permission:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerRecursivePermission || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_RECURSIVE_PERMISSION_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
