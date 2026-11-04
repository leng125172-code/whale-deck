using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecursiveAclRestoreAction(string action)
    {
        if (action is "container-recursive-acl-restore:plan" or "container-recursive-acl-restore:apply" or "container-recursive-acl-restore:inspect" or "container-recursive-acl-restore:discard") return true;
        if (action.StartsWith("container-recursive-acl-restore:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerRecursiveAclRestoreOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(320));
        using var driver = await StartAsync("container-recursive-acl-restore:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerRecursiveAclRestore || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_RESTORE_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
