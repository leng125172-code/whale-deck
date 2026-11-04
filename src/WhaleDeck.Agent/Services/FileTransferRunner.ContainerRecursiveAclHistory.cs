using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecursiveAclHistoryAction(string action)
    {
        if (action is "container-recursive-acl-history:list" or "container-recursive-acl-history:read") return true;
        if (action.StartsWith("container-recursive-acl-history:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerRecursiveAclHistoryOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("list" or "read")) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_HISTORY_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var driver = await StartAsync("container-recursive-acl-history:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerRecursiveAclHistory || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_HISTORY_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_HISTORY_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
