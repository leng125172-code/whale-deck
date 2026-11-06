using System.Text.Json;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerRecycleArchiveAction(string action)
    {
        if(action is "container-recycle-archive:plan" or "container-recycle-archive:reserve" or "container-recycle-archive:apply" or "container-recycle-archive:inspect" or "container-recycle-archive:discard")return true;
        if(action.StartsWith("container-recycle-archive:",StringComparison.Ordinal))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_ACTION_INVALID");return false;
    }
    public async Task<JsonDocument> ContainerRecycleArchiveOnceAsync(string action,string input,CancellationToken cancellationToken)
    {
        if(action is not ("plan" or "reserve" or "apply" or "inspect" or "discard"))throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_ACTION_INVALID");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromSeconds(120));
        using var driver=await StartAsync("container-recycle-archive:"+action,input,lifetime.Token);
        var result=await ReadJsonAsync(driver,lifetime.Token);
        try
        {
            if(!driver.IsContainerRecycleArchive||driver.BufferCount!=0||await driver.Process.StandardOutput.BaseStream.ReadAsync(new byte[1],lifetime.Token)!=0)throw new InvalidOperationException("CONTAINER_RECYCLE_ARCHIVE_TRAILING_OUTPUT");
            await FinishAsync(driver,lifetime.Token);return result;
        }
        catch{result.Dispose();throw;}
    }
}
