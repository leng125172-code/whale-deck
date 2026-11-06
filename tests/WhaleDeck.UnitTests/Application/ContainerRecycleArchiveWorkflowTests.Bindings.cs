using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Theory][InlineData("source")][InlineData("source-proof")][InlineData("request")][InlineData("plan")][InlineData("execution")][InlineData("reservation")][InlineData("apply-marker")][InlineData("barrier")]
    public async Task PersistedBindingsAndExecutionMarkersCannotBeAltered(string field)
    {
        using var f=new Fixture();await f.Create();f.Gateway.AsyncApply=(_,_)=>throw new IOException("hold after marker");await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;
        switch(field){case "source":typeof(WhaleDeck.Domain.Entities.ContainerRecycleArchiveIntent).GetProperty(nameof(r.SourceIntentId))!.SetValue(r,Guid.NewGuid());break;case "source-proof":typeof(WhaleDeck.Domain.Entities.ContainerRecycleArchiveIntent).GetProperty(nameof(r.SourceProofHash))!.SetValue(r,new string('F',64));break;
            case "request":typeof(WhaleDeck.Domain.Entities.ContainerRecycleArchiveIntent).GetProperty(nameof(r.RequestHash))!.SetValue(r,new string('0',64));break;case "plan":r.PlanHash=new('0',64);break;case "execution":r.ExecutionHash=new('0',64);break;case "reservation":r.ReservationHash=new('0',64);break;case "apply-marker":r.ApplyStartedAtUtc=r.ExecutionExpiresAtUtc;break;default:r.PublishBarrier=false;break;}
        Assert.False(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(r,f.Protector));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Get());
    }
    [Theory][InlineData("old-domain")][InlineData("other-actor")][InlineData("other-login")][InlineData("other-id")]
    public async Task ConfirmationCannotBorrowAnotherDomainOrSession(string field)
    {
        using var f=new Fixture();await f.Ready();var r=f.Repository.Row!;var v=await f.Get();
        var token=field=="old-domain"?f.Protector.SignRecyclePurgeConfirmation(new(r.Id,r.ActorSubject,r.LoginBinding,r.PlanHash!,r.ReadyExpiresAtUtc!.Value)):
            f.Protector.SignRecycleArchiveConfirmation(new(field=="other-id"?Guid.NewGuid():r.Id,field=="other-actor"?"other-admin":r.ActorSubject,field=="other-login"?new('F',64):r.LoginBinding,r.PlanHash!,r.ReadyExpiresAtUtc!.Value));
        if(field=="old-domain")await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.ConfirmAsync(f.Actor,r.ContainerId,r.Id,new(token,true,v.Version),default));else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service.ConfirmAsync(f.Actor,r.ContainerId,r.Id,new(token,true,v.Version),default));Assert.Equal(0,f.Repository.Confirms);
    }
    [Theory][InlineData(false,0)][InlineData(true,-1)]
    public async Task ExplicitConfirmationAndValidVersionAreRequired(bool confirmed,long version)
    {
        using var f=new Fixture();await f.Ready();var v=await f.Get();await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.ConfirmAsync(f.Actor,v.Location.ContainerId,v.Id,new(v.Confirmation!.PlanHash,confirmed,version),default));Assert.Null(f.Repository.Row!.ExecutionJobId);
    }
    [Fact]
    public async Task ExpiredReadyWrongContainerAndDifferentLoginCannotExecute()
    {
        using var f=new Fixture();await f.Ready();var v=await f.Get();await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Service.GetAsync(f.Actor with{LoginBinding=new('F',64)},v.Location.ContainerId,v.Id,default));await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Service.GetAsync(f.Actor,new('b',64),v.Id,default));f.Clock.Now=f.Clock.Now.AddMinutes(6);Assert.Equal("Expired",(await f.Get()).State);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.ConfirmAsync(f.Actor,v.Location.ContainerId,v.Id,new(v.Confirmation!.PlanHash,true,v.Version),default));Assert.Equal(0,f.Gateway.Applies);
    }
    [Fact]
    public async Task IdentityOrRegistryOutageNeverExecutesOrUnlocks()
    {
        using var f=new Fixture();await f.Create();f.Identity.LiveUnavailable=true;await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Reserves);Assert.Equal(0,f.Gateway.Applies);Assert.True(f.Repository.Deferrals>0);
        using var g=new Fixture();await g.Create();g.Logins.Unavailable=true;await g.Dispatcher.TickAsync(default);Assert.Equal(0,g.Gateway.Applies);
    }
    [Fact]
    public async Task LostLeaseBeforeInspectCannotCallAnyRootMutation()
    {
        using var f=new Fixture();await f.Create();f.Repository.Renewal=false;await f.Dispatcher.TickAsync(default);Assert.Equal(0,f.Gateway.Inspects);Assert.Equal(0,f.Gateway.Reserves);Assert.Equal(0,f.Gateway.Applies);Assert.Equal(0,f.Gateway.Discards);
    }
    [Fact]
    public async Task UnexpectedCompletedReservationWithoutApplyMarkerHoldsUnknown()
    {
        using var f=new Fixture();await f.Create();f.Gateway.AsyncReserve=(op,_)=>Task.FromResult(Gateway.ReceiptFor(op,"Completed"));await f.Dispatcher.TickAsync(default);Assert.Equal("Unknown",f.Repository.Row!.State);Assert.True(f.Repository.Row.PublishBarrier);Assert.Null(f.Repository.Row.ArchiveCopied);Assert.Null(f.Repository.Row.ApplyStartedAtUtc);Assert.Equal(0,f.Gateway.Applies);
    }
    [Fact]
    public async Task StoredUnknownFactCannotBeRewrittenAsCompletedDuringRecovery()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Unknown");await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;f.Gateway.Receipt=Gateway.ReceiptFor(f.Gateway.LastOperation!,"Completed");await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.AcknowledgeAsync(f.Actor,r.Id,new(r.Version,true),default));Assert.Equal("Unknown",r.State);Assert.True(r.PublishBarrier);Assert.Null(r.ArchiveCopied);
    }
    [Fact]
    public async Task UnknownWithNoReplyMayRecordLateCompletedProofBySameIdOnly()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=_=>throw new IOException("lost copy result");await f.Dispatcher.TickAsync(default);f.Gateway.Receipt=null;f.Gateway.DiscardFailure=new IOException("lost observe");f.Clock.Now=f.Clock.Now.AddMinutes(16);await f.Dispatcher.TickAsync(default);var r=f.Repository.Row!;Assert.Equal("Unknown",r.State);Assert.Null(r.ReceiptJson);
        f.Gateway.DiscardFailure=null;f.Gateway.Receipt=Gateway.ReceiptFor(f.Gateway.LastOperation!,"Completed");await f.Service.AcknowledgeAsync(f.Actor,r.Id,new(r.Version,true),default);Assert.Equal("Acknowledged",r.State);Assert.True(r.ArchiveCopied);Assert.Equal(1,f.Gateway.Applies);Assert.True(ContainerRecycleArchiveWorkflowValidation.HasValidBinding(r,f.Protector));
    }
    [Fact]
    public async Task SourceAndPlanSnapshotsDoNotShareNestedMutableAttributes()
    {
        using var f=new Fixture();var first=ContainerRecycleArchiveWorkflowValidation.SnapshotCommand(f.Command);f.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0]^=42;Assert.NotEqual(first.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0],f.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0]);var resolved=await f.Resolver.ResolveAsync(f.Actor,f.Source.Row.Id,default);Assert.Equal(first.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0],resolved.Command.Purge.Plan.Command.Source.Plan.Attributes[0].Value[0]);
    }
}
