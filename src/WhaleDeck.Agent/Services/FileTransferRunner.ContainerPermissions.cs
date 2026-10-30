using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerPermissionAction(string action)
    {
        if (action is "container-permission:plan" or "container-permission:apply" or "container-permission:inspect" or "container-permission:discard") return true;
        if (action.StartsWith("container-permission:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_PERMISSION_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerPermissionOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_PERMISSION_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        using var driver = await StartAsync("container-permission:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerPermission || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_PERMISSION_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_PERMISSION_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
