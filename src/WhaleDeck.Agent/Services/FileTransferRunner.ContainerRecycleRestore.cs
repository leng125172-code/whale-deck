using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecycleRestoreAction(string action)
    {
        if(action is "container-recycle-restore:plan" or "container-recycle-restore:apply" or "container-recycle-restore:inspect" or "container-recycle-restore:discard")return true;
        if(action.StartsWith("container-recycle-restore:",StringComparison.Ordinal))throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_ACTION_INVALID");return false;
    }
    public async Task<JsonDocument> ContainerRecycleRestoreOnceAsync(string action,string input,CancellationToken cancellationToken)
    {
        if(action is not ("plan" or "apply" or "inspect" or "discard"))throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_ACTION_INVALID");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        using var driver=await StartAsync("container-recycle-restore:"+action,input,lifetime.Token);
        var result=await ReadJsonAsync(driver,lifetime.Token);
        try
        {
            if(!driver.IsContainerRecycleRestore||driver.BufferCount!=0||await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1],lifetime.Token)!=0)throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_TRAILING_OUTPUT");
            await FinishAsync(driver,lifetime.Token);return result;
        }
        catch{result.Dispose();throw;}
    }
}
