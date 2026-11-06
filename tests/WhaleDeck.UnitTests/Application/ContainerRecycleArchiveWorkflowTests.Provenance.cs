using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Theory][InlineData("request")][InlineData("plan")][InlineData("receipt")][InlineData("barrier")][InlineData("history")][InlineData("other-source")]
    public async Task OriginalRecycleProofMustStillMatchTheCompletedPurge(string field)
    {
        using var f=new Fixture();var r=f.Original.Row;
        switch(field)
        {
            case "request":typeof(WhaleDeck.Domain.Entities.ContainerRecycleIntent).GetProperty(nameof(r.RequestHash))!.SetValue(r,new string('0',64));break;
            case "plan":r.PlanHash=new('0',64);break;
            case "receipt":r.ReceiptHash=new('0',64);break;
            case "barrier":r.PublishBarrier=true;break;
            case "history":r.HistoryAvailable=false;break;
            default:f.Original.Row=MakeSource(f.Protector,f.Clock.Now);break;
        }
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Queue());
        Assert.Equal("CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED",error.Message);Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Requires);
    }

    [Fact]
    public async Task OriginalChangedDuringPlanningFailsWithoutStartingRootWork()
    {
        using var f=new Fixture();await f.Queue();f.Gateway.AfterPlan=()=>f.Original.Row.HistoryAvailable=false;
        await f.Dispatcher.TickAsync(default);Assert.Equal("Failed",f.Repository.Row!.State);Assert.Null(f.Repository.Row.PlanJson);Assert.Equal(0,f.Gateway.Reserves);
    }

    [Theory][InlineData("request")][InlineData("plan")][InlineData("execution")][InlineData("receipt")][InlineData("original-proof")][InlineData("bytes")]
    public void SourceMacBindsThePurgeAndOriginalProvenance(string field)
    {
        using var f=new Fixture();var r=f.Source.Row;var command=ContainerRecycleArchiveWorkflowValidation.SnapshotCommand(f.Command);
        var binding=new ContainerRecycleArchiveSourceBinding(r.Id,r.RequestHash,r.PlanHash!,r.ExecutionHash!,r.ReceiptHash!,r.SourceProofHash,command);
        var changed=field switch
        {
            "request"=>binding with{RequestHash=new('0',64)},"plan"=>binding with{PlanHash=new('0',64)},
            "execution"=>binding with{ExecutionHash=new('0',64)},"receipt"=>binding with{ReceiptHash=new('0',64)},
            "original-proof"=>binding with{OriginalProofHash=new('0',64)},
            _=>binding with{Command=ContainerRecycleArchiveWorkflowValidation.SnapshotCommand(command)}
        };
        if(field=="bytes")changed.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0]^=42;
        Assert.NotEqual(f.Protector.FingerprintRecycleArchiveSource(binding),f.Protector.FingerprintRecycleArchiveSource(changed));
    }

    [Fact]
    public async Task CompletedPurgeWithLateCancelStillProvidesHistoricalAuthority()
    {
        using var f=new Fixture();f.Source.Row.CancelRequestedAtUtc=f.Clock.Now;
        Assert.True(ContainerRecyclePurgeWorkflowValidation.HasValidBinding(f.Source.Row,f.Protector));
        await f.Queue();Assert.NotNull(f.Repository.Row);
    }

    [Fact]
    public async Task LostCompletedReplyWithCurrentBindingGoneKeepsHistoricalCopy()
    {
        using var f=new Fixture();await f.Create();var original=JsonSerializer.Serialize(f.Original.Row);var purge=JsonSerializer.Serialize(f.Source.Row);
        f.Gateway.ApplyHandler=op=>{f.Gateway.Receipt=Gateway.ReceiptFor(op,"Completed") with{BindingCurrent=false,OwnedFence=false};throw new IOException("lost copy response");};
        await f.Dispatcher.TickAsync(default);f.Logins.Active=false;await f.Dispatcher.TickAsync(default);
        Assert.Equal("Succeeded",f.Repository.Row!.State);Assert.True(f.Repository.Row.ArchiveCopied);Assert.False(f.Repository.Row.BindingCurrent);
        Assert.Equal(1,f.Gateway.Applies);Assert.Equal(original,JsonSerializer.Serialize(f.Original.Row));Assert.Equal(purge,JsonSerializer.Serialize(f.Source.Row));
        Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(f.Repository.Row,f.Protector));
    }

    [Theory][InlineData("old-inode")][InlineData("archive-inode")][InlineData("ctime")][InlineData("uid")]
    public async Task InvalidPublishedCopyNeverBecomesSuccessfulBusinessProof(string field)
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>
        {
            var r=Gateway.ReceiptFor(op,"Completed");var i=r.PublishedIdentity!;
            return field switch
            {
                "old-inode"=>r with{PublishedIdentity=i with{Inode=op.Plan.DeletedIdentity.Inode}},
                "archive-inode"=>r with{PublishedIdentity=i with{Device=op.Plan.ArchiveIdentity.Device,Inode=op.Plan.ArchiveIdentity.Inode}},
                "ctime"=>r with{PublishedChangedAtUnixNs=-1},_=>r with{PublishedIdentity=i with{OwnerUid=99}}
            };
        };
        await f.Dispatcher.TickAsync(default);Assert.Equal("Unknown",f.Repository.Row!.State);Assert.True(f.Repository.Row.PublishBarrier);Assert.Null(f.Repository.Row.ReceiptJson);
    }

    [Fact]
    public async Task CompletedCopyCannotSubstituteItsPublishedInode()
    {
        using var f=new Fixture();await f.Create();await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;var receipt=ContainerRecycleArchiveWorkflowValidation.ReadReceipt(r)!;
        Assert.Throws<InvalidOperationException>(()=>ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(r,receipt with{PublishedIdentity=receipt.PublishedIdentity! with{Inode=999}}));
        ContainerRecycleArchiveWorkflowValidation.RequireCompatibleReceipt(r,receipt with{BindingCurrent=false,OwnedFence=false});
    }
}
