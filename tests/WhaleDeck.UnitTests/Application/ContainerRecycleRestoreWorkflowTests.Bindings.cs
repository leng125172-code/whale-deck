using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleRestoreWorkflowTests
{
    [Fact]
    public async Task MissingReceiptUnknownCanRecordLateCompletedProofDuringAcknowledgement()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=_=>throw new IOException("lost apply reply");
        await f.Dispatcher.TickAsync(default);f.Clock.Now=f.Clock.Now.AddMinutes(16);f.Gateway.DiscardFailure=new IOException("lost inspection/fence reply");
        await f.Dispatcher.TickAsync(default);var row=f.Repository.Row!;Assert.Equal("Unknown",row.State);Assert.Null(row.ReceiptJson);Assert.True(row.PublishBarrier);
        f.Gateway.DiscardFailure=null;f.Gateway.Receipt=Gateway.ReceiptFor(f.Gateway.LastOperation!,"Completed");
        await f.Service.AcknowledgeAsync(f.Actor,row.Id,new(row.Version,true),default);
        Assert.Equal("Acknowledged",row.State);Assert.Equal("Completed",row.RootState);Assert.True(row.RestoredByThisOperation);Assert.False(row.PublishBarrier);Assert.Equal(1,f.Gateway.Applies);
    }
    [Theory][InlineData(false,0)][InlineData(true,-1)]
    public async Task ConfirmationMustBeExplicitAndVersionValid(bool confirmed,long version)
    {
        using var f=new Fixture();await f.Ready();var ready=await f.Get();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.ConfirmAsync(f.Actor,ready.Location.ContainerId,ready.Id,new(ready.Confirmation!.PlanHash,confirmed,version),default));
        Assert.Null(f.Repository.Row!.ExecutionJobId);
    }
    [Theory][InlineData("old-domain")][InlineData("other-actor")][InlineData("other-login")][InlineData("other-id")]
    public async Task ConfirmationCannotBorrowSourceRecyclingDomainOrAnotherSession(string field)
    {
        using var f=new Fixture();await f.Ready();var ready=await f.Get();var row=f.Repository.Row!;
        var token=field=="old-domain"?f.Protector.SignRecycleConfirmation(new(row.Id,row.ActorSubject,row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value)):
            f.Protector.SignRecycleRestoreConfirmation(new(field=="other-id"?Guid.NewGuid():row.Id,field=="other-actor"?"different-admin":row.ActorSubject,field=="other-login"?new('F',64):row.LoginBinding,row.PlanHash!,row.ReadyExpiresAtUtc!.Value));
        if(field=="old-domain")await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.ConfirmAsync(f.Actor,ready.Location.ContainerId,ready.Id,new(token,true,ready.Version),default));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service.ConfirmAsync(f.Actor,ready.Location.ContainerId,ready.Id,new(token,true,ready.Version),default));
        Assert.Equal(0,f.Repository.Confirms);Assert.Equal(0,f.Gateway.Applies);
    }
    [Theory][InlineData("source-id")][InlineData("request")][InlineData("plan")][InlineData("execution")][InlineData("receipt")][InlineData("history")][InlineData("bytes")]
    public void SourceProofCoversCompleteProvenanceAndNestedCommand(string field)
    {
        using var f=new Fixture();var r=f.Source.Row;var q=ContainerRecycleWorkflowValidation.Reference(r);
        var command=ContainerRecycleRestoreWorkflowValidation.SnapshotCommand(new(new(q.Id,q.Owner,q.Plan),r.HistoryDigest!));
        var original=new ContainerRecycleRestoreSourceBinding(r.Id,r.RequestHash,r.PlanHash!,r.ExecutionHash!,r.ReceiptHash!,command);
        var changed=field switch {"source-id"=>original with{IntentId=Guid.NewGuid()},"request"=>original with{RequestHash=new('F',64)},"plan"=>original with{PlanHash=new('F',64)},
            "execution"=>original with{ExecutionHash=new('F',64)},"receipt"=>original with{ReceiptHash=new('F',64)},"history"=>original with{Command=command with{HistoryDigest=new('F',64)}},
            _=>original with{Command=ContainerRecycleRestoreWorkflowValidation.SnapshotCommand(command)}};
        if(field=="bytes")changed.Command.Source.Plan.Attributes[0].Value[0]=99;
        Assert.NotEqual(f.Protector.FingerprintRecycleRestoreSource(original),f.Protector.FingerprintRecycleRestoreSource(changed));
    }
    [Fact]
    public async Task ExpiredReadyAndWrongContainerOrLoginCannotConfirmOrRead()
    {
        using var f=new Fixture();await f.Ready();var ready=await f.Get();
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Service.GetAsync(f.Actor with{LoginBinding=new('F',64)},ready.Location.ContainerId,ready.Id,default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Service.GetAsync(f.Actor,new('b',64),ready.Id,default));
        f.Clock.Now=f.Clock.Now.AddMinutes(6);var expired=await f.Get();Assert.Equal("Expired",expired.State);Assert.Null(expired.Confirmation);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.ConfirmAsync(f.Actor,ready.Location.ContainerId,ready.Id,new(ready.Confirmation!.PlanHash,true,ready.Version),default));Assert.Equal(0,f.Gateway.Applies);
    }
    [Theory][InlineData("unknown-false")][InlineData("other-source-state")][InlineData("history")]
    public async Task AcknowledgementCannotRewriteUnknownAttributionOrHistory(string change)
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Unknown");await f.Dispatcher.TickAsync(default);var row=f.Repository.Row!;
        var original=JsonSerializer.Serialize(new {row.ReceiptJson,row.ReceiptHash,row.State,row.PublishBarrier,row.Version});var prior=f.Gateway.Receipt!;
        f.Gateway.Receipt=change switch {"unknown-false"=>prior with{RestoredByThisOperation=false},"other-source-state"=>prior with{SourceState="Restored"},_=>prior with{HistoryDigest=new('A',64)}};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.AcknowledgeAsync(f.Actor,row.Id,new(row.Version,true),default));
        Assert.Equal(original,JsonSerializer.Serialize(new {row.ReceiptJson,row.ReceiptHash,row.State,row.PublishBarrier,row.Version}));Assert.True(row.PublishBarrier);
    }
    [Fact]
    public async Task ForeignPlanSourceCannotBecomeReadyEvenIfShapeIsValid()
    {
        using var f=new Fixture();await f.Queue();var p=f.Gateway.PlanValue;f.Gateway.PlanValue=p with{Command=p.Command with{HistoryDigest=new('A',64)}};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Failed",f.Repository.Row!.State);Assert.Null(f.Repository.Row.PlanJson);Assert.Equal(0,f.Gateway.Applies);
    }
    [Fact]
    public async Task InflightSourceRevocationCancelsTheCallAndPreservesBarrierUntilOriginalExecutionIsFenced()
    {
        using var f=new Fixture();await f.Create();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Gateway.AsyncApply=async(_,token)=>{started.SetResult();await Task.Delay(Timeout.InfiniteTimeSpan,token);throw new InvalidOperationException("unreachable");};
        var tick=f.Dispatcher.TickAsync(default);await started.Task.WaitAsync(TimeSpan.FromSeconds(2));f.Source.Row.State="Unknown";
        await tick.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(1,f.Gateway.Applies);Assert.True(f.Repository.Row!.PublishBarrier);Assert.NotNull(f.Repository.Row.RootStartedAtUtc);
        await f.Dispatcher.TickAsync(default);Assert.Equal(1,f.Gateway.Applies);Assert.Equal(1,f.Gateway.Discards);Assert.False(f.Repository.Row.PublishBarrier);
    }
}
