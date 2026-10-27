using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerDirectoryAction(string action)
    {
        if (action is "container-directory:plan" or "container-directory:apply" or "container-directory:inspect" or "container-directory:discard") return true;
        if (action.StartsWith("container-directory:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_DIRECTORY_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerDirectoryOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_DIRECTORY_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var driver = await StartAsync("container-directory:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerDirectory || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_DIRECTORY_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_DIRECTORY_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
