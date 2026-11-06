using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecyclePurgeAction(string action)
    {
        if(action is "container-recycle-purge:plan" or "container-recycle-purge:reserve" or "container-recycle-purge:apply" or "container-recycle-purge:inspect" or "container-recycle-purge:discard")return true;
        if(action.StartsWith("container-recycle-purge:",StringComparison.Ordinal))throw new ArgumentException("CONTAINER_RECYCLE_PURGE_ACTION_INVALID");return false;
    }
    public async Task<JsonDocument> ContainerRecyclePurgeOnceAsync(string action,string input,CancellationToken cancellationToken)
    {
        if(action is not ("plan" or "reserve" or "apply" or "inspect" or "discard"))throw new ArgumentException("CONTAINER_RECYCLE_PURGE_ACTION_INVALID");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        using var driver=await StartAsync("container-recycle-purge:"+action,input,lifetime.Token);
        var result=await ReadJsonAsync(driver,lifetime.Token);
        try
        {
            if(!driver.IsContainerRecyclePurge||driver.BufferCount!=0||await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1],lifetime.Token)!=0)throw new InvalidOperationException("CONTAINER_RECYCLE_PURGE_TRAILING_OUTPUT");
            await FinishAsync(driver,lifetime.Token);return result;
        }
        catch{result.Dispose();throw;}
    }
}
