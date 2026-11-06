using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Fact]
    public async Task FreshConfirmedLeasePersistsReservationAndApplyMarkerBeforeCopying()
    {
        using var f=new Fixture();var original=JsonSerializer.Serialize(f.Source.Row);await f.Queue();Assert.Equal(0,f.Gateway.Plans);await f.Dispatcher.TickAsync(default);Assert.NotNull((await f.Get()).Confirmation);await f.Confirm();
        f.Gateway.ApplyHandler=op=>{Assert.NotNull(f.Repository.Row!.RootStartedAtUtc);Assert.NotNull(f.Repository.Row.ReservationJson);Assert.NotNull(f.Repository.Row.ApplyStartedAtUtc);Assert.True(f.Repository.Row.PublishBarrier);return Gateway.ReceiptFor(op,"Completed");};
        await f.Dispatcher.TickAsync(default);var value=await f.Get();Assert.Equal("Succeeded",value.State);Assert.True(value.ArchiveCopied);Assert.True(value.ReservationRecorded);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(1,f.Gateway.Applies);Assert.Equal(original,JsonSerializer.Serialize(f.Source.Row));Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(f.Repository.Row!,f.Protector));
    }
    [Theory][InlineData("ordinary")][InlineData("logout")][InlineData("demotion")]
    public async Task UnauthorizedActorNeverReadsSourceOrCreatesWork(string reason)
    {
        using var f=new Fixture();var reads=0;f.Source.OnFind=()=>reads++;if(reason=="ordinary")f.Identity.Admin=false;if(reason=="logout")f.Logins.Active=false;if(reason=="demotion")f.Identity.LiveAdmin=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Queue());Assert.Equal(0,reads);Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Requires);
    }
    [Theory][InlineData("state")][InlineData("receipt")][InlineData("mac")][InlineData("history")][InlineData("barrier")][InlineData("start")][InlineData("complete")]
    public async Task UnprovenPurgeSourceNeverBecomesArchiveAuthority(string field)
    {
        using var f=new Fixture();var r=f.Source.Row;switch(field){case "state":r.State="Unknown";break;case "receipt":r.ReceiptJson=null;break;case "mac":r.ReceiptHash=new('F',64);break;case "history":r.PurgeApplied=false;break;case "barrier":r.PublishBarrier=true;break;case "start":r.RootStartedAtUtc=null;break;default:r.CompletedAtUtc=null;break;}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Queue());Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Reserves);
    }
    [Fact]
    public async Task SameKeyReturnsFrozenSnapshotButAnotherSourceConflicts()
    {
        using var f=new Fixture();var first=await f.Queue();f.Source.OnFind=()=>throw new IOException("must not reread source on same idempotency key");Assert.Equal(first,await f.Queue());Assert.Equal(1,f.Repository.Enqueues);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueAsync(f.Actor,f.Source.Row.ContainerId,Guid.NewGuid(),f.Key,default));
    }
    [Fact]
    public async Task RevocationAfterSourceReadPreventsPersistence()
    {using var f=new Fixture();f.Source.OnFind=()=>f.Identity.LiveAdmin=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Queue());Assert.Null(f.Repository.Row);}
    [Fact]
    public async Task SourceChangeAfterPlanOrBeforeConfirmationNeverAllowsCopy()
    {
        using var f=new Fixture();await f.Queue();f.Gateway.AfterPlan=()=>f.Source.Row.State="Unknown";await f.Dispatcher.TickAsync(default);Assert.Null(f.Repository.Row!.PlanJson);Assert.Equal("Failed",f.Repository.Row.State);
        using var g=new Fixture();await g.Ready();g.Source.Row.State="Unknown";await Assert.ThrowsAsync<InvalidOperationException>(()=>g.Confirm());Assert.Null(g.Repository.Row!.ExecutionJobId);
    }
    [Theory][InlineData("cancel")][InlineData("logout")][InlineData("demotion")][InlineData("expiry")][InlineData("source")][InlineData("lease")]
    public async Task AuthorityLostAfterReservedNeverEntersApplyAndNextTickOnlyDiscards(string reason)
    {
        using var f=new Fixture();await f.Create();f.Gateway.AfterReserve=()=>{switch(reason){case "cancel":f.Repository.Canceled=true;break;case "logout":f.Logins.Active=false;break;case "demotion":f.Identity.LiveAdmin=false;break;case "expiry":f.Clock.Now=f.Clock.Now.AddMinutes(16);break;case "source":f.Source.Row.State="Unknown";break;default:f.Repository.Renewal=false;break;}};
        await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);Assert.Null(f.Repository.Row!.ApplyStartedAtUtc);f.Repository.Renewal=true;
        if(f.Repository.Row.State!="Unknown")await f.Dispatcher.TickAsync(default);else await f.Service.AcknowledgeAsync(f.Actor with{Subject="recovery-admin",LoginBinding=new('9',64)},f.Repository.Row.Id,new(f.Repository.Row.Version,true),default);
        Assert.Equal(0,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Reserves);Assert.True(f.Gateway.Discards>0);
    }
    [Fact]
    public async Task ReclaimedReservedRowCancelsOriginalIdWithoutRepeatingReserveOrApply()
    {
        using var f=new Fixture();await f.Create();f.Gateway.AsyncReserve=(_,_)=>throw new IOException("lost reserved response");await f.Dispatcher.TickAsync(default);Assert.True(f.Repository.Row!.PublishBarrier);var id=f.Repository.Row.ExecutionJobId;
        await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(0,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Discards);Assert.Equal(id,f.Gateway.LastOperation!.Id);Assert.Equal("Failed",f.Repository.Row.State);
    }
    [Fact]
    public async Task FailedReservationPersistenceCannotAdvanceToCopy()
    {using var f=new Fixture();await f.Create();f.Repository.ReservationSaved=false;await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);Assert.Null(f.Repository.Row!.ApplyStartedAtUtc);await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(1,f.Gateway.Discards);}
    [Fact]
    public async Task FailedAtomicApplyEntryDoesNotCallCopyAndRecoveryOnlyDiscards()
    {using var f=new Fixture();await f.Create();f.Repository.ApplyStart=ContainerFileUploadStartResult.Busy;await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(1,f.Gateway.Discards);}
    [Fact]
    public async Task SourceChangedAfterApplyMarkerDoesNotEnterRpc()
    {using var f=new Fixture();await f.Create();f.Repository.AfterApplying=()=>f.Source.Row.State="Unknown";await f.Dispatcher.TickAsync(default);Assert.NotNull(f.Repository.Row!.ApplyStartedAtUtc);Assert.Equal(0,f.Gateway.Applies);await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Discards);}
    [Fact]
    public async Task LostCopyReplyIsInspectedDespiteLogoutAndNeverCopiedAgain()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>{f.Gateway.Receipt=Gateway.ReceiptFor(op,"Completed");throw new IOException("lost copy response");};await f.Dispatcher.TickAsync(default);f.Logins.Active=false;f.Source.Row.State="Unknown";await f.Dispatcher.TickAsync(default);
        Assert.Equal("Succeeded",f.Repository.Row!.State);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(1,f.Gateway.Applies);Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task UnknownKeepsNullOutcomeAndRecoveryDoesNotReuseOrRewriteExecution()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Unknown");await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;Assert.Equal("Unknown",r.State);Assert.Null(r.ArchiveCopied);Assert.True(r.PublishBarrier);var id=r.ExecutionJobId;
        await f.Service.AcknowledgeAsync(f.Actor with{Subject="recovery-admin",LoginBinding=new('9',64)},r.Id,new(r.Version,true),default);Assert.Equal("Acknowledged",r.State);Assert.Null(r.ArchiveCopied);Assert.Equal(id,f.Gateway.LastOperation!.Id);Assert.Equal(f.Actor.Subject,f.Gateway.LastOperation.Context.ActorSubject);Assert.False(r.PublishBarrier);
    }
    [Fact]
    public async Task StillReservedDiscardCannotReleaseProtectionOrAcknowledge()
    {
        using var f=new Fixture();await f.Create();f.Gateway.AsyncReserve=(_,_)=>throw new IOException("lost reservation");await f.Dispatcher.TickAsync(default);f.Gateway.DiscardFailure=new IOException("unavailable observation");f.Clock.Now=f.Clock.Now.AddMinutes(16);await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;Assert.Equal("Unknown",r.State);
        f.Gateway.DiscardFailure=null;f.Gateway.Receipt=Gateway.ReceiptFor(f.Gateway.LastOperation!,"Reserved");
        // Explicit test seam to simulate a server that still cannot resolve it.
        f.Gateway.KeepReservedOnDiscard=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.AcknowledgeAsync(f.Actor,r.Id,new(r.Version,true),default));Assert.True(r.PublishBarrier);Assert.Null(r.ArchiveCopied);
    }
    [Theory][InlineData("id")][InlineData("fingerprint")][InlineData("presence")][InlineData("fence")]
    public async Task InvalidTerminalReplyNeverBecomesSuccess(string field)
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>{var v=Gateway.ReceiptFor(op,"Completed");return field switch{"id"=>v with{SourceOperationId=op.Id},"fingerprint"=>v with{Fingerprint=new('0',64)},"presence"=>v with{ArchiveCopied=null},_=>v with{OwnedFence=false}};};await f.Dispatcher.TickAsync(default);Assert.Equal("Unknown",f.Repository.Row!.State);Assert.Null(f.Repository.Row.ReceiptJson);Assert.True(f.Repository.Row.PublishBarrier);
    }
    [Theory][InlineData("reserve")][InlineData("apply")]
    public async Task RevocationDuringLongRpcCancelsItsTokenAndDoesNotAdvanceOrReplay(string stage)
    {
        using var f=new Fixture();await f.Create();var canceled=false;
        async Task<ContainerRecycleArchiveReceiptDto> Hold(ContainerRecycleArchiveOperationDto op,CancellationToken token)
        {
            f.Identity.LiveAdmin=false;
            try{await Task.Delay(Timeout.InfiniteTimeSpan,token);throw new InvalidOperationException("unreachable");}
            catch(OperationCanceledException){canceled=true;throw;}
        }
        if(stage=="reserve")f.Gateway.AsyncReserve=Hold;else f.Gateway.AsyncApply=Hold;
        var tick=f.Dispatcher.TickAsync(default);Assert.Same(tick,await Task.WhenAny(tick,Task.Delay(TimeSpan.FromSeconds(15))));await tick;
        Assert.True(canceled);Assert.True(f.Repository.Row!.PublishBarrier);Assert.Equal(stage=="reserve"?0:1,f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Reserves);Assert.Equal(stage=="reserve"?0:1,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Discards);
    }
}
