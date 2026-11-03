using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerAclHistoryAction(string action)
    {
        if (action is "container-acl-history:list" or "container-acl-history:read") return true;
        if (action.StartsWith("container-acl-history:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_ACL_HISTORY_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerAclHistoryOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("list" or "read")) throw new ArgumentException("CONTAINER_ACL_HISTORY_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var driver = await StartAsync("container-acl-history:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerAclHistory || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_ACL_HISTORY_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_ACL_HISTORY_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
