using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleRestoreWorkflowTests
{
    [Fact]
    public async Task RestoreUsesDurableSourceAndNewConfirmedJobsWithoutMutatingOriginalIntent()
    {
        using var f=new Fixture();var original=JsonSerializer.Serialize(f.Source.Row);var queued=await f.Queue();
        Assert.Equal("Queued",queued.State);Assert.Null(queued.ExecutionJobId);Assert.Equal(0,f.Gateway.Plans);Assert.Equal(0,f.Gateway.Applies);
        Assert.Equal(f.Source.Row.Id,queued.SourceIntentId);Assert.Equal(f.Source.Row.ExecutionJobId,queued.SourceOperationId);
        await f.Dispatcher.TickAsync(default);var ready=await f.Get();Assert.Equal("Ready",ready.State);Assert.NotNull(ready.Confirmation);
        Assert.Equal(1000U,ready.OriginalUid);Assert.Equal(1001U,ready.OriginalGid);Assert.Equal(7,ready.SizeBytes);Assert.Equal(1,ready.AttributeCount);
        var confirmed=await f.Confirm();Assert.NotEqual(confirmed.Id,confirmed.ExecutionJobId);Assert.NotEqual(queued.SourceOperationId,confirmed.ExecutionJobId);
        await f.Dispatcher.TickAsync(default);var completed=await f.Get();Assert.Equal("Succeeded",completed.State);Assert.True(completed.Receipt!.RestoredByThisOperation);
        Assert.Equal(confirmed.ExecutionJobId,f.Gateway.LastOperation!.Id);Assert.Equal(f.Source.Row.OwnerHash,f.Gateway.LastOperation.Plan.Command.Source.Owner);
        Assert.NotEqual(f.Source.Row.OwnerHash,f.Gateway.LastOperation.Owner);Assert.Equal(original,JsonSerializer.Serialize(f.Source.Row));
    }
    [Theory][InlineData("ordinary")][InlineData("logout")][InlineData("live-demotion")]
    public async Task UnauthorizedActorCannotResolveOrQueueDurableSource(string reason)
    {
        using var f=new Fixture();var reads=0;f.Source.OnFind=()=>reads++;
        if(reason=="ordinary")f.Identity.Admin=false;if(reason=="logout")f.Logins.Active=false;if(reason=="live-demotion")f.Identity.LiveAdmin=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Queue());Assert.Equal(0,reads);Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Requires);
    }
    [Theory][InlineData("state")][InlineData("receipt")][InlineData("mac")][InlineData("history")][InlineData("barrier")][InlineData("canceled")][InlineData("not-started")][InlineData("not-completed")][InlineData("plan")]
    public async Task InvalidOrUnresolvedSourceCannotCreateNewIntent(string field)
    {
        using var f=new Fixture();var row=f.Source.Row;
        switch(field){case "state":row.State="Unknown";break;case "receipt":row.ReceiptJson=null;break;case "mac":row.ReceiptHash=new('F',64);break;
            case "history":row.HistoryAvailable=false;break;case "barrier":row.PublishBarrier=true;break;case "canceled":row.CancelRequestedAtUtc=f.Clock.Now;break;
            case "not-started":row.RootStartedAtUtc=null;break;case "not-completed":row.CompletedAtUtc=null;break;default:row.PlanJson="{}";break;}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Queue());Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Requires);
    }
    [Fact]
    public async Task SameKeyReturnsOriginalSnapshotEvenIfSourceLaterChangesAndDifferentSourceConflicts()
    {
        using var f=new Fixture();var first=await f.Queue();f.Source.Row.State="Unknown";f.Source.OnFind=()=>throw new IOException("must not re-resolve idempotent request");
        Assert.Equal(first,await f.Queue());Assert.Equal(1,f.Repository.Enqueues);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueAsync(f.Actor,f.Command.Source.Plan.Command.Location.ContainerId,Guid.NewGuid(),f.Key,default));
    }
    [Fact]
    public async Task ResolverFreezesNestedAttributesAndHashesBeforeLaterAuthorityAwait()
    {
        using var f=new Fixture();var command=ContainerRecycleRestoreWorkflowValidation.SnapshotCommand(f.Command);
        f.Command.Source.Plan.Attributes[0].Value[0]=99;
        Assert.Equal(0,command.Source.Plan.Attributes[0].Value[0]);
        var resolved=await f.Resolver.ResolveAsync(f.Actor,f.Source.Row.Id,default);Assert.Equal(0,resolved.Command.Source.Plan.Attributes[0].Value[0]);
        Assert.NotEqual(f.Command.Source.Plan.Attributes[0].Value[0],resolved.Command.Source.Plan.Attributes[0].Value[0]);
    }
    [Fact]
    public async Task LiveRevocationAfterSourceReadPreventsPersistence()
    {
        using var f=new Fixture();f.Source.OnFind=()=>f.Identity.LiveAdmin=false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Queue());Assert.Null(f.Repository.Row);Assert.Equal(0,f.Gateway.Plans);
    }
    [Fact]
    public async Task SourceChangesAfterScanCannotPublishReadyPlanOrConfirm()
    {
        using var f=new Fixture();await f.Queue();f.Gateway.AfterPlan=()=>f.Source.Row.State="Unknown";
        await f.Dispatcher.TickAsync(default);Assert.Null(f.Repository.Row!.PlanJson);Assert.Equal("Failed",f.Repository.Row.State);Assert.Equal(0,f.Gateway.Applies);
        using var g=new Fixture();await g.Ready();g.Source.Row.State="Unknown";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>g.Confirm());Assert.Null(g.Repository.Row!.ExecutionJobId);
    }
    [Fact]
    public async Task SourceChangeAfterStartedNeverAppliesAndNextTickFencesTheSameId()
    {
        using var f=new Fixture();await f.Create();f.Repository.AfterStart=()=>f.Source.Row.State="Unknown";
        await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);Assert.True(f.Repository.Row!.PublishBarrier);
        var execution=f.Repository.Row.ExecutionJobId;await f.Dispatcher.TickAsync(default);
        Assert.Equal(0,f.Gateway.Applies);Assert.Equal(execution,f.Gateway.LastOperation!.Id);Assert.Equal(1,f.Gateway.Discards);
    }
    [Theory][InlineData("cancel")][InlineData("logout")][InlineData("demotion")][InlineData("expiry")]
    public async Task InvalidExecutionAuthorityDiscardsOriginalRestoreRatherThanWriting(string reason)
    {
        using var f=new Fixture();await f.Create();var id=f.Repository.Row!.ExecutionJobId;
        if(reason=="cancel")f.Repository.Canceled=true;if(reason=="logout")f.Logins.Active=false;if(reason=="demotion")f.Identity.LiveAdmin=false;if(reason=="expiry")f.Clock.Now=f.Clock.Now.AddMinutes(16);
        await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Discards);Assert.Equal(id,f.Gateway.LastOperation!.Id);
        Assert.Equal(reason=="expiry"?"Failed":"Canceled",f.Repository.Row.State);Assert.False(f.Repository.Row.RestoredByThisOperation);
    }
    [Fact]
    public async Task LostApplyResponseIsObservedByOriginalIdWithoutReapplyingOrRequiringOldSourceEligibility()
    {
        using var f=new Fixture();await f.Create();var id=f.Repository.Row!.ExecutionJobId;
        f.Gateway.ApplyHandler=op=>{f.Gateway.Receipt=Gateway.ReceiptFor(op,"Completed");throw new IOException("lost reply");};
        await f.Dispatcher.TickAsync(default);Assert.True(f.Repository.Row.PublishBarrier);Assert.Equal(1,f.Gateway.Applies);
        f.Source.Row.State="Unknown";f.Logins.Active=false;await f.Dispatcher.TickAsync(default);
        Assert.Equal(1,f.Gateway.Applies);Assert.Equal(id,f.Gateway.LastOperation!.Id);Assert.Equal("Succeeded",f.Repository.Row.State);Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task UnknownKeepsNullFactsAndBarrierAndCrossAdminAcknowledgementOnlyFencesOriginalExecution()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Unknown");await f.Dispatcher.TickAsync(default);
        var row=f.Repository.Row!;Assert.Equal("Unknown",row.State);Assert.True(row.PublishBarrier);Assert.Null(row.RestoredByThisOperation);Assert.Null(row.SourceRetained);Assert.Null(row.SourceRestored);
        var actor=f.Actor with {Subject="another-current-admin",LoginBinding=new('9',64)};
        await f.Service.AcknowledgeAsync(actor,row.Id,new(row.Version,true),default);
        Assert.Equal("Acknowledged",row.State);Assert.Equal("another-current-admin",row.AcknowledgedBy);Assert.Null(row.RestoredByThisOperation);Assert.False(row.PublishBarrier);
        Assert.Equal(row.ExecutionJobId,f.Gateway.LastOperation!.Id);Assert.Equal(f.Actor.Subject,f.Gateway.LastOperation.Context.ActorSubject);Assert.Equal(1,f.Gateway.Applies);
    }
    [Theory][InlineData("source")][InlineData("digest")][InlineData("attribution")][InlineData("retained")]
    public async Task InvalidExecutionReceiptNeverBecomesSuccess(string field)
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>{var r=Gateway.ReceiptFor(op,"Completed");return field switch{
            "source"=>r with{SourceOperationId=op.Id},"digest"=>r with{HistoryDigest=new('A',64)},"attribution"=>r with{RestoredByThisOperation=false},_=>r with{SourceRetained=true}};};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Unknown",f.Repository.Row!.State);Assert.True(f.Repository.Row.PublishBarrier);Assert.Null(f.Repository.Row.ReceiptJson);
    }
    [Fact]
    public async Task ClaimedExecutionWithoutReceiptIsFencedWithoutASecondApply()
    {
        using var f=new Fixture();await f.Create();f.Repository.Row!.RootStartedAtUtc=f.Clock.Now;f.Repository.Row.PublishBarrier=true;
        await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Discards);
    }
    [Fact]
    public async Task LosingLeaseNeverStartsOrDiscardsAndRetainsStateForRecovery()
    {
        using var f=new Fixture();await f.Create();f.Repository.Renewal=false;await f.Dispatcher.TickAsync(default);
        Assert.Equal(0,f.Gateway.Applies);Assert.Equal(0,f.Gateway.Discards);Assert.Equal(1,f.Repository.Deferrals);
    }
    [Theory][InlineData("source-id")][InlineData("source-proof")][InlineData("command")][InlineData("plan")][InlineData("execution")]
    public async Task StoredBindingCorruptionIsRejected(string field)
    {
        using var f=new Fixture();await f.Create();var r=f.Repository.Row!;
        switch(field){case "source-id":typeof(WhaleDeck.Domain.Entities.ContainerRecycleRestoreIntent).GetProperty(nameof(r.SourceIntentId))!.SetValue(r,Guid.NewGuid());break;
            case "source-proof":typeof(WhaleDeck.Domain.Entities.ContainerRecycleRestoreIntent).GetProperty(nameof(r.SourceProofHash))!.SetValue(r,new string('F',64));break;
            case "command":typeof(WhaleDeck.Domain.Entities.ContainerRecycleRestoreIntent).GetProperty(nameof(r.CommandJson))!.SetValue(r,"{}");break;
            case "plan":r.PlanHash=new('F',64);break;default:r.ExecutionHash=new('F',64);break;}
        Assert.False(ContainerRecycleRestoreWorkflowValidation.HasValidBinding(r,f.Protector));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Get());Assert.Equal(0,f.Gateway.Applies);
    }
}
