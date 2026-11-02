using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerAclAction(string action)
    {
        if (action is "container-acl:plan" or "container-acl:apply" or "container-acl:inspect" or "container-acl:discard") return true;
        if (action.StartsWith("container-acl:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_ACL_ACTION_INVALID");
        return false;
    }
    public async Task<JsonDocument> ContainerAclOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("plan" or "apply" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_ACL_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        using var driver = await StartAsync("container-acl:" + action, input, lifetime.Token);
        var result = await ReadJsonAsync(driver, lifetime.Token);
        try
        {
            if (!driver.IsContainerAcl || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_ACL_TRAILING_OUTPUT");
            if (await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidOperationException("CONTAINER_ACL_TRAILING_OUTPUT");
            await FinishAsync(driver, lifetime.Token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
