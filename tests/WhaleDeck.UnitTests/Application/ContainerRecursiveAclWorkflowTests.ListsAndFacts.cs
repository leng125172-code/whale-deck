using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using static WhaleDeck.UnitTests.Application.ContainerRecursiveAclWorkflowFixture;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecursiveAclWorkflowTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CallerOwnedListsAreSnapshottedBeforeRepositoryOrAgentAwait(bool directory)
    {
        using var f=new Fixture();var command=f.Command;
        var entries=(ContainerAclEntryDto[])(directory?command.DirectoryEntries:command.FileEntries!);
        f.Repository.AfterRead=()=>entries[1]=entries[1] with {Id=0,Permissions=0};
        var queued=await f.Service.QueueAsync(f.Actor,command,f.Key,default);
        var captured=directory?queued.Command.DirectoryEntries:queued.Command.FileEntries!;
        Assert.Equal(2201U,captured[1].Id);Assert.Equal(6U,captured[1].Permissions);
        Assert.Equal(0U,entries[1].Id);Assert.True(ContainerRecursiveAclWorkflowValidation.HasValidBinding(f.Repository.Row!,f.Protector));
        Assert.Equal(0,f.Gateway.Plans);Assert.Equal(0,f.Gateway.Applies);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task AgentPlanListsAreCapturedBeforePostRpcRenewal(bool directory)
    {
        using var f=new Fixture();await f.Queue();var entries=(ContainerAclEntryDto[])(directory?f.Gateway.PlanValue.Command.DirectoryEntries:f.Gateway.PlanValue.Command.FileEntries!);
        var renewals=0;f.Repository.AfterRenew=()=>{if(++renewals==2)entries[1]=entries[1] with {Id=0,Permissions=0};};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Ready",f.Repository.Row!.State);
        var captured=ContainerRecursiveAclWorkflowValidation.ReadPlan(f.Repository.Row);
        Assert.Equal(2201U,(directory?captured.Command.DirectoryEntries:captured.Command.FileEntries!)[1].Id);
        Assert.Equal(0U,entries[1].Id);Assert.True(ContainerRecursiveAclWorkflowValidation.HasValidBinding(f.Repository.Row,f.Protector));
        Assert.Equal(0,f.Gateway.Applies);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task EqualTargetsInIndependentListsAreIdempotentButChangesConflict(bool directory)
    {
        using var f=new Fixture();var first=await f.Queue();
        var clone=ContainerRecursiveAclWorkflowValidation.SnapshotCommand(f.Command);
        Assert.NotSame(f.Command.DirectoryEntries,clone.DirectoryEntries);
        var second=await f.Service.QueueAsync(f.Actor,clone,f.Key,default);Assert.Equal(first.Id,second.Id);Assert.Equal(1,f.Repository.Enqueues);
        clone=directory?clone with {DirectoryEntries=Entries(5)}:clone with {FileEntries=Entries(5)};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueAsync(f.Actor,clone,f.Key,default));
        Assert.Equal(1,f.Repository.Enqueues);Assert.Equal(0,f.Gateway.Applies);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DefaultAclPreservesNullFilesAndExplicitClearThroughConfirmationAndExecution(bool clear)
    {
        using var f=new Fixture();f.Gateway.PlanValue=f.Gateway.PlanValue with {Command=f.Command with {AclType="default",FileEntries=null,DirectoryEntries=clear?[]:Entries()},ChangeCount=2};
        await f.Ready();var ready=await f.Get();Assert.Null(ready.Command.FileEntries);
        Assert.Equal(clear?0:5,ready.Command.DirectoryEntries.Count);Assert.Equal(2U,ready.ChangeCount);
        Assert.Contains("default仅设置目录继承ACL",ready.Confirmation!.Warning,StringComparison.Ordinal);
        Assert.Contains("显式空列表表示清除",ready.Confirmation.Warning,StringComparison.Ordinal);
        await f.Confirm();await f.Dispatcher.TickAsync(default);var done=await f.Get();
        Assert.Equal("Succeeded",done.State);Assert.Equal("default",done.Receipt!.AclType);Assert.Equal(2U,done.Receipt.ChangedCount);
        Assert.Null(f.Gateway.LastOperation!.Plan.Command.FileEntries);Assert.Equal(clear?0:5,f.Gateway.LastOperation.Plan.Command.DirectoryEntries.Count);
    }
    [Fact]
    public async Task WrongAclTypeOnOtherwiseValidRootReceiptRemainsUnknownAndProtected()
    {
        using var f=new Fixture();await f.Create();f.Gateway.ApplyHandler=op=>Gateway.ReceiptFor(op,"Completed") with {AclType="default"};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Unknown",f.Repository.Row!.State);Assert.True(f.Repository.Row.PublishBarrier);
        Assert.Null(f.Repository.Row.ReceiptJson);Assert.Null(f.Repository.Row.ReceiptHash);Assert.Equal(1,f.Gateway.Applies);
    }
    [Theory][InlineData("OutcomeDigest")][InlineData("BindingCurrent")][InlineData("HistoryAvailable")][InlineData("Replayed")]
    public async Task ChangingBothCanonicalReceiptAndMatchingColumnsCannotBypassReceiptMac(string field)
    {
        using var f=new Fixture();await f.Create();await f.Dispatcher.TickAsync(default);var row=f.Repository.Row!;
        var receipt=ContainerRecursiveAclWorkflowValidation.ReadReceipt(row)!;
        receipt=field switch {"OutcomeDigest"=>receipt with {OutcomeDigest=new('8',64)},"BindingCurrent"=>receipt with {BindingCurrent=false},
            "HistoryAvailable"=>receipt with {HistoryAvailable=false,BindingCurrent=false},_=>receipt with {Replayed=true}};
        row.ReceiptJson=ContainerRecursiveAclWorkflowValidation.Serialize(receipt);row.OutcomeDigest=receipt.OutcomeDigest;row.BindingCurrent=receipt.BindingCurrent;row.HistoryAvailable=receipt.HistoryAvailable;
        if(!receipt.BindingCurrent)row.State="Superseded";
        // Valid transport fields, canonical JSON and matching columns alone do not establish authority.
        Assert.NotNull(ContainerRecursiveAclWorkflowValidation.ReadReceipt(row));
        Assert.False(ContainerRecursiveAclWorkflowValidation.HasValidBinding(row,f.Protector));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Get());
    }
    [Fact]
    public async Task MissingOrInventedReceiptMacCannotBeTreatedAsTrustedOutcome()
    {
        using var f=new Fixture();await f.Create();var row=f.Repository.Row!;
        row.ReceiptHash=new('A',64);Assert.False(ContainerRecursiveAclWorkflowValidation.HasValidBinding(row,f.Protector));
        row.ReceiptHash=null;await f.Dispatcher.TickAsync(default);Assert.NotNull(row.ReceiptHash);
        row.ReceiptHash=null;await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Get());
    }
    [Theory][InlineData("mask")][InlineData("entries")][InlineData("directory")][InlineData("kind")]
    public async Task UnexpectedPlanTargetCannotBeSignedReady(string change)
    {
        using var f=new Fixture();await f.Queue();var plan=f.Gateway.PlanValue;
        f.Gateway.PlanValue=plan with {Command=change switch {
            "mask"=>plan.Command with {FileEntries=Entries(5)},"entries"=>plan.Command with {FileEntries=Entries(4,0,0)},
            "directory"=>plan.Command with {DirectoryEntries=Entries(5)},_=>plan.Command with {AclType="default",FileEntries=null}},ChangeCount=change=="kind"?2U:4U};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Failed",f.Repository.Row!.State);Assert.Null(f.Repository.Row.PlanHash);
        Assert.Null(f.Repository.Row.ExecutionJobId);Assert.Equal(0,f.Gateway.Applies);
    }
    [Fact]
    public void DeepSnapshotHasNoCallerAliasAndRetainsNamedZeroValues()
    {
        var command=Plan().Command with {FileEntries=Entries(4,0,0)};var clone=ContainerRecursiveAclWorkflowValidation.SnapshotCommand(command);
        Assert.True(ContainerRecursiveAclValidation.SameCommand(command,clone));Assert.NotSame(command.FileEntries,clone.FileEntries);
        ((ContainerAclEntryDto[])command.FileEntries!)[1]=new("user",2201,6);
        Assert.Equal(0U,clone.FileEntries![1].Id);Assert.Equal(0U,clone.FileEntries[1].Permissions);
        Assert.False(ContainerRecursiveAclValidation.SameCommand(command,clone));
    }
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public void SnapshotRejectsOversizedTargetsBeforeSerializationCanEnumerateThem(bool plan,bool directory)
    {
        var original=Plan();var command=directory?original.Command with {DirectoryEntries=new OversizedEntries()}:original.Command with {FileEntries=new OversizedEntries()};
        Assert.Throws<ArgumentException>(()=>{
            if(plan)ContainerRecursiveAclWorkflowValidation.SnapshotPlan(original with {Command=command});
            else ContainerRecursiveAclWorkflowValidation.SnapshotCommand(command);
        });
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OversizedListsNeverReachIdentityRepositoryOrAgent(bool directory)
    {
        using var f=new Fixture();f.Identity.LiveUnavailable=true;
        var command=directory?f.Command with {DirectoryEntries=new OversizedEntries()}:f.Command with {FileEntries=new OversizedEntries()};
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Service.QueueAsync(f.Actor,command,f.Key,default));
        Assert.Equal(0,f.Repository.Enqueues);Assert.Equal(0,f.Gateway.Requires);Assert.Null(f.Repository.Row);
    }
    private sealed class OversizedEntries:IReadOnlyList<ContainerAclEntryDto>
    {
        public int Count=>65;
        public ContainerAclEntryDto this[int index]=>throw new InvalidOperationException("OVERSIZED_ACL_ENUMERATED");
        public IEnumerator<ContainerAclEntryDto> GetEnumerator()=>throw new InvalidOperationException("OVERSIZED_ACL_ENUMERATED");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
