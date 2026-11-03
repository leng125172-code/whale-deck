using System.Reflection;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Source-bound business state machine; repository, identity and Agent are explicit fixtures.</summary>
public sealed partial class ContainerAclWorkflowTests
{
    private sealed class RestoreSource : IContainerAclRestoreSourceGateway
    {
        public ContainerAclHistorySnapshotDto Value=null!; public int Reads; public Exception? Failure; public Action? AfterRead;
        public Task<ContainerAclHistorySnapshotDto> ReadContainerAclRestoreSourceAsync(TerminalActor actor, ContainerFileLocationDto location,string capture,string digest,CancellationToken token)
        { Reads++; if(Failure is not null)throw Failure;AfterRead?.Invoke();return Task.FromResult(Value); }
    }
    private static ContainerAclRestoreCommand RestoreCommand(Fixture f, RestoreSource source)=>new(f.Command.Location,source.Value.Entry.CaptureId,source.Value.Entry.HistoryDigest,f.Command.ExpectedVersion);
    private static RestoreSource SourceFor(Fixture f,bool clear=false)
    {
        var command=f.Command;var entries=clear?[]:command.Entries.ToArray();var access=command.AclType=="access"?entries:f.Gateway.PlanValue.AccessAcl.ToArray();var defaults=command.AclType=="default"?entries:Array.Empty<ContainerAclEntryDto>();
        return new(){Value=new(command.Location,new(Guid.NewGuid().ToString("N"),new('B',64),0,"666:2026-10-26T01:02:03Z",command.AclType,f.Gateway.PlanValue.Kind,ContainerAclValidation.Mode(access),(uint)access.Length,(uint)defaults.Length),access,defaults)};
    }
    [Fact]
    public async Task RecoverySummaryIsStoredReadOnlyAndSeparateFromOwnerSourceAcrossAcknowledgement()
    {
        var source = new RestoreSource(); using var f = new Fixture(false, source); source.Value = SourceFor(f).Value;
        var queued = await f.Service.QueueRestoreAsync(f.Actor, RestoreCommand(f, source), f.Key, default);
        var admin = f.Actor with { Subject = "another-current-admin", LoginBinding = new('F', 64) };
        await f.Dispatcher.TickAsync(default); await f.Confirm();
        f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown"); await f.Dispatcher.TickAsync(default);
        source.Failure = new IOException("HDD unavailable"); var stored = f.Repository.Row!.RestoreSourceJson; var reads = source.Reads;
        var summary = await f.Service.RecoveryRestoreSourceAsync(admin, queued.Intent.Id, default);
        Assert.Equal(new ContainerAclRestoreSourceSummaryDto(queued.Source.Entry.CaptureId, "access", "File", 0,
            queued.Source.Entry.OriginalMode, queued.Source.Entry.AccessEntryCount, 0), summary);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetRestoreAsync(admin, f.Command.Location.ContainerId, queued.Intent.Id, default));
        await f.Service.AcknowledgeAsync(admin, queued.Intent.Id, new(f.Repository.Row.Version, true), default);
        Assert.Equal("Acknowledged", f.Repository.Row.State);
        Assert.Equal(summary, await f.Service.RecoveryRestoreSourceAsync(admin, queued.Intent.Id, default));
        Assert.Equal(stored, f.Repository.Row.RestoreSourceJson); Assert.Equal(reads, source.Reads);
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal(1, f.Gateway.Discards);
    }
    [Fact]
    public async Task RecoverySummaryReturnsNullOnlyForValidOrdinaryRequestAndRejectsTampering()
    {
        using var f = new Fixture(); await f.Queue(); var row = f.Repository.Row!;
        Assert.Null(await f.Service.RecoveryRestoreSourceAsync(f.Actor, row.Id, default));
        typeof(ContainerAclIntent).GetProperty(nameof(row.RestoreSourceJson))!.SetValue(row, "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RecoveryRestoreSourceAsync(f.Actor, row.Id, default));
        Assert.Equal(0, f.Gateway.Plans + f.Gateway.Applies + f.Gateway.Discards);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RecoverySourceRejectsRevokedAuthorityBeforeAndAfterRepositoryRead(bool afterRead)
    {
        using var f = new Fixture(); await f.Queue();
        if (afterRead) f.Repository.AfterRead = () => f.Identity.LiveAdmin = false;
        else f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RecoveryRestoreSourceAsync(f.Actor, f.Repository.Row!.Id, default));
    }
    [Fact]
    public async Task RecoverySourceRejectsEmptyAndMissingIntentWithoutGatewayCalls()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.RecoveryRestoreSourceAsync(f.Actor, Guid.Empty, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.RecoveryRestoreSourceAsync(f.Actor, Guid.NewGuid(), default));
        Assert.Equal(0, f.Gateway.Requires + f.Gateway.Plans + f.Gateway.Inspects + f.Gateway.Applies + f.Gateway.Discards);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ExplicitEmptyDefaultAndNoChangeRestoreStillRequireANewConfirmedExecution(bool noChange)
    {
        var source=new RestoreSource();using var f=new Fixture(!noChange,source);var plan=f.Gateway.PlanValue;
        f.Gateway.PlanValue=noChange
            ? plan with{Command=plan.Command with{Entries=plan.AccessAcl.ToArray()},TargetMode=plan.SourceIdentity.Mode&511,NoChange=true}
            : plan with{Command=plan.Command with{AclType="default",Entries=[]},DefaultAcl=ContainerAclWorkflowFixture.Named(),TargetMode=plan.SourceIdentity.Mode&511};
        source.Value=SourceFor(f,!noChange).Value;
        await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);await f.Dispatcher.TickAsync(default);
        var ready=await f.Get();Assert.Equal(noChange,ready.NoChange);Assert.Equal(0,f.Gateway.Applies);
        if(!noChange)Assert.Empty(ready.Command.Entries);
        await f.Confirm();await f.Dispatcher.TickAsync(default);Assert.True((await f.Get()).HistoryVerified);Assert.Equal(1,f.Gateway.Applies);
    }
    [Fact]
    public async Task ChangedSelectedMetadataCannotReplaceTheFrozenSourceEvenWithTheSameDigest()
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;
        await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);
        source.Value=source.Value with{Entry=source.Value.Entry with{CreatedAtUnixMs=1}};
        await f.Dispatcher.TickAsync(default);Assert.Equal("Failed",f.Repository.Row!.State);Assert.Equal(0,f.Gateway.Plans+f.Gateway.Applies);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RestoreUsesNewIntentPlanAndExecutionAndSealsBothOriginalAclLists(bool directory)
    {
        var source=new RestoreSource();using var f=new Fixture(directory,source);source.Value=SourceFor(f).Value;
        var queued=await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);
        Assert.Equal("Queued",queued.Intent.State);Assert.NotNull(f.Repository.Row!.RestoreSourceJson);Assert.Equal(0,f.Gateway.Plans);Assert.Equal(0,f.Gateway.Applies);
        Assert.NotEqual(queued.Source.Entry.CaptureId,queued.Intent.Id.ToString("N"));Assert.True(ContainerAclWorkflowValidation.HasValidBinding(f.Repository.Row,f.Protector));
        await f.Dispatcher.TickAsync(default);var ready=await f.Get();Assert.Contains("历史ACL恢复",ready.Confirmation!.Warning,StringComparison.Ordinal);
        await f.Confirm();await f.Dispatcher.TickAsync(default);Assert.Equal("Succeeded",(await f.Get()).State);Assert.True(f.Repository.Row.HistoryVerified);
        Assert.NotEqual(queued.Source.Entry.CaptureId,f.Gateway.LastOperation!.Id.ToString("N"));Assert.Equal(f.Command.Entries,f.Gateway.LastOperation.Plan.Command.Entries);
        Assert.Equal(f.Gateway.PlanValue.AccessAcl,f.Gateway.LastOperation.Plan.AccessAcl);Assert.Equal(f.Gateway.PlanValue.DefaultAcl,f.Gateway.LastOperation.Plan.DefaultAcl);
        Assert.Equal(4,source.Reads); // Queue, fresh plan, confirmation, first write. Never after Inspect yields a receipt.
    }
    [Fact]
    public async Task OldPlainRequestSerializationStaysByteIdenticalAndSourceCannotBeStrippedOrSubstituted()
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;
        await f.Queue();var row=f.Repository.Row!;var binding=ContainerAclWorkflowValidation.RequestBinding(row);
        var old=JsonSerializer.Serialize(new {binding.Id,binding.IdempotencyKey,binding.Actor,binding.LoginBinding,binding.TraceId,binding.Owner,binding.Command,binding.CreatedAtUtc,binding.PlanningExpiresAtUtc},ContainerAclWorkflowValidation.Json);
        Assert.Equal(old,ContainerAclWorkflowValidation.Serialize(binding));Assert.DoesNotContain("RestoreSource",old);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default));Assert.Equal(0,source.Reads);
        f.Repository.Row=null;await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);row=f.Repository.Row!;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Queue());
        var stored=row.RestoreSourceJson!;typeof(ContainerAclIntent).GetProperty(nameof(row.RestoreSourceJson))!.SetValue(row,null);
        Assert.False(ContainerAclWorkflowValidation.HasValidBinding(row,f.Protector));typeof(ContainerAclIntent).GetProperty(nameof(row.RestoreSourceJson))!.SetValue(row,stored);
        var changed=ContainerAclRestoreValidation.Read(row)!;typeof(ContainerAclIntent).GetProperty(nameof(row.RestoreSourceJson))!.SetValue(row,ContainerAclWorkflowValidation.Serialize(changed with{Entry=changed.Entry with{CaptureId=Guid.NewGuid().ToString("N")}}));
        Assert.False(ContainerAclWorkflowValidation.HasValidBinding(row,f.Protector));
    }
    [Fact]
    public async Task IdempotentRestoreDoesNotRereadUnavailableHistoryOrRenewOriginalExpiry()
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;var command=RestoreCommand(f,source);
        var first=await f.Service.QueueRestoreAsync(f.Actor,command,f.Key,default);var expiry=f.Repository.Row!.PlanningExpiresAtUtc;source.Failure=new IOException();f.Clock.Now=f.Clock.Now.AddHours(1);
        Assert.Equal(first.Intent.Id,(await f.Service.QueueRestoreAsync(f.Actor,command,f.Key,default)).Intent.Id);Assert.Equal(1,source.Reads);Assert.Equal(expiry,f.Repository.Row.PlanningExpiresAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueRestoreAsync(f.Actor,command with{CaptureId=Guid.NewGuid().ToString("N")},f.Key,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.QueueRestoreAsync(f.Actor,command with{ExpectedVersion=new('C',64)},f.Key,default));
    }
    [Theory][InlineData("plan")][InlineData("confirm")][InlineData("apply")]
    public async Task MissingSourceAtEachFreshBoundaryCannotApply(string boundary)
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;
        await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);
        if(boundary!="plan")await f.Dispatcher.TickAsync(default);
        if(boundary=="apply")await f.Confirm();source.Failure=new ContainerAclRestoreSourceChangedException();
        if(boundary=="confirm"){await Assert.ThrowsAsync<ContainerAclRestoreSourceChangedException>(()=>f.Confirm());Assert.Null(f.Repository.Row!.ExecutionJobId);}
        else{await f.Dispatcher.TickAsync(default);Assert.Equal("Failed",f.Repository.Row!.State);Assert.Equal("CONTAINER_ACL_RESTORE_SOURCE_CHANGED",f.Repository.Row.ResultCode);}
        Assert.Equal(0,f.Gateway.Applies);Assert.Equal(boundary=="apply"?1:0,f.Gateway.Discards);
    }
    [Theory][InlineData("plan")][InlineData("confirm")][InlineData("apply")]
    public async Task SameDigestButReplacedSourceAtEachFreshBoundaryCannotApply(string boundary)
    {
        var source = new RestoreSource(); using var f = new Fixture(false, source); source.Value = SourceFor(f).Value;
        await f.Service.QueueRestoreAsync(f.Actor, RestoreCommand(f, source), f.Key, default);
        if (boundary != "plan") await f.Dispatcher.TickAsync(default);
        if (boundary == "apply") await f.Confirm();
        var frozen = f.Repository.Row!.RestoreSourceJson;
        source.Value = source.Value with { Entry = source.Value.Entry with { CreatedAtUnixMs = 1 } };
        if (boundary == "confirm")
        {
            await Assert.ThrowsAsync<ContainerAclRestoreSourceChangedException>(() => f.Confirm());
            Assert.Equal("Ready", f.Repository.Row.State); Assert.Null(f.Repository.Row.ExecutionJobId);
        }
        else
        {
            await f.Dispatcher.TickAsync(default); Assert.Equal("Failed", f.Repository.Row.State);
            Assert.Equal("CONTAINER_ACL_RESTORE_SOURCE_CHANGED", f.Repository.Row.ResultCode);
        }
        Assert.Equal(frozen, f.Repository.Row.RestoreSourceJson); Assert.Null(f.Repository.Row.RootStartedAtUtc);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(boundary == "apply" ? 1 : 0, f.Gateway.Discards);
    }
    [Theory][InlineData("plan")][InlineData("confirm")][InlineData("apply")]
    public async Task TemporarySourceOutagePreservesOriginalIdentityAndExpiryAndCanResumeBeforeAnyRootWrite(string boundary)
    {
        var source = new RestoreSource(); using var f = new Fixture(false, source); source.Value = SourceFor(f).Value;
        await f.Service.QueueRestoreAsync(f.Actor, RestoreCommand(f, source), f.Key, default);
        if (boundary != "plan") await f.Dispatcher.TickAsync(default);
        if (boundary == "apply") await f.Confirm();
        var row = f.Repository.Row!; var frozen = row.RestoreSourceJson;
        var expiry = row.PlanningExpiresAtUtc; var readyExpiry = row.ReadyExpiresAtUtc; var execution = row.ExecutionJobId;
        source.Failure = new IOException("synthetic history transport outage");
        if (boundary == "confirm") await Assert.ThrowsAsync<IOException>(() => f.Confirm());
        else { await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Repository.Deferrals); }
        Assert.Equal(frozen, row.RestoreSourceJson); Assert.Equal(expiry, row.PlanningExpiresAtUtc);
        Assert.Equal(readyExpiry, row.ReadyExpiresAtUtc); Assert.Equal(execution, row.ExecutionJobId);
        Assert.Null(row.RootStartedAtUtc); Assert.Equal(0, f.Gateway.Applies + f.Gateway.Discards);
        source.Failure = null;
        if (boundary == "plan") await f.Dispatcher.TickAsync(default);
        if (boundary != "apply") await f.Confirm();
        var originalExecution = row.ExecutionJobId; await f.Dispatcher.TickAsync(default);
        Assert.Equal("Succeeded", (await f.Get()).State); Assert.Equal(originalExecution, row.ExecutionJobId);
        Assert.Equal(1, f.Gateway.Plans); Assert.Equal(1, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards);
        Assert.Equal(frozen, row.RestoreSourceJson);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SourceOutageCannotRenewOriginalDeadlineOrAuthorizeWriteAfterExpiry(bool execution)
    {
        var source = new RestoreSource(); using var f = new Fixture(false, source); source.Value = SourceFor(f).Value;
        await f.Service.QueueRestoreAsync(f.Actor, RestoreCommand(f, source), f.Key, default);
        if (execution) { await f.Dispatcher.TickAsync(default); await f.Confirm(); }
        var row = f.Repository.Row!; var id = row.Id; var originalExecution = row.ExecutionJobId;
        var frozen = row.RestoreSourceJson; var planningExpiry = row.PlanningExpiresAtUtc;
        var deadline = execution ? row.ExecutionExpiresAtUtc!.Value : row.PlanningExpiresAtUtc;
        source.Failure = new IOException("synthetic history transport outage");
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Repository.Deferrals);
        var reads = source.Reads; f.Clock.Now = deadline.AddTicks(1);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Failed", (await f.Get()).State);
        Assert.Equal(execution ? "CONTAINER_ACL_INTERRUPTED" : "CONTAINER_ACL_PLAN_EXPIRED", row.ResultCode);
        Assert.Equal(reads, source.Reads); Assert.Equal(id, row.Id); Assert.Equal(originalExecution, row.ExecutionJobId);
        Assert.Equal(frozen, row.RestoreSourceJson); Assert.Equal(planningExpiry, row.PlanningExpiresAtUtc);
        Assert.Null(row.RootStartedAtUtc); Assert.Equal(0, f.Gateway.Applies); Assert.Equal(execution ? 1 : 0, f.Gateway.Discards);
        if (execution) Assert.Equal("Aborted", ContainerAclWorkflowValidation.ReadReceipt(row)!.State);
    }
    [Theory][InlineData("Completed", "Succeeded")][InlineData("Unknown", "Unknown")][InlineData("Aborted", "Failed")]
    public async Task AnOriginalExecutionReceiptReconcilesEvenWhenHistoryIsGone(string receiptState, string expectedState)
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);
        await f.Dispatcher.TickAsync(default);await f.Confirm();var op=ContainerAclWorkflowValidation.Reference(f.Repository.Row!);
        f.Gateway.Receipt=Gateway.ReceiptFor(op,receiptState);source.Failure=new ContainerAclRestoreSourceChangedException();var reads=source.Reads;
        await f.Dispatcher.TickAsync(default);Assert.Equal(expectedState,(await f.Get()).State);Assert.Equal(reads,source.Reads);Assert.Equal(0,f.Gateway.Applies + f.Gateway.Discards);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SourceArraysAreFrozenBeforeAwaitAndReturnedSourceCannotRewriteStoredIntent(bool revoke)
    {
        var source=new RestoreSource();using var f=new Fixture(false,source);source.Value=SourceFor(f).Value;
        if(revoke){source.AfterRead=()=>f.Identity.LiveAdmin=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default));Assert.Null(f.Repository.Row);return;}
        f.Gateway.AfterRequire=()=>((ContainerAclEntryDto[])source.Value.AccessAcl)[0]=new("user_obj",null,0);
        var queued=await f.Service.QueueRestoreAsync(f.Actor,RestoreCommand(f,source),f.Key,default);Assert.Equal(6U,queued.Source.AccessAcl[0].Permissions);
        var stored=f.Repository.Row!.RestoreSourceJson;((IList<ContainerAclEntryDto>)queued.Source.AccessAcl)[0]=new("user_obj",null,0);
        Assert.Equal(stored,f.Repository.Row.RestoreSourceJson);Assert.True(ContainerAclWorkflowValidation.HasValidBinding(f.Repository.Row,f.Protector));
    }
}
