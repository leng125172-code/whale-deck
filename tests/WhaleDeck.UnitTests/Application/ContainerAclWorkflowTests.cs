using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.UnitTests.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Business/cryptographic boundaries with an explicit in-memory repository; not SQL or installed evidence.</summary>
public sealed partial class ContainerAclWorkflowTests
{
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task OrdinaryUserAndRevokedLoginCannotQueueOrReachAgent(bool admin, bool login)
    {
        using var f = new Fixture(); f.Identity.Admin = admin; f.Logins.Active = login;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Queue());
        Assert.Equal(0, f.Gateway.Requires); Assert.Null(f.Repository.Row);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task QueueNeverScansOrWritesAclAndPlanningNeedsSeparateConfirmation(bool directory)
    {
        using var f = new Fixture(directory); var queued = await f.Queue();
        Assert.Equal("Queued", queued.State); Assert.Null(queued.Confirmation); Assert.Null(queued.ExecutionJobId);
        Assert.Equal(0, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default);
        var ready = await f.Get(); Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation);
        Assert.Equal(directory ? "Directory" : "File", ready.Kind); Assert.Equal(1000U, ready.OriginalUid); Assert.Equal(1001U, ready.OriginalGid);
        Assert.Equal(5100U, ready.ExecutorUid); Assert.Equal(5101U, ready.ExecutorGid); Assert.False(ready.NoChange);
        Assert.Equal(f.Gateway.PlanValue.AccessAcl, ready.OriginalAccessAcl); Assert.Equal(432U, ready.TargetMode);
        Assert.Equal(1, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies); Assert.False(f.Repository.Row!.PublishBarrier);
        var confirmed = await f.Confirm(); Assert.NotEqual(confirmed.Id, confirmed.ExecutionJobId);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal("Succeeded", (await f.Get()).State);
        Assert.Equal(confirmed.ExecutionJobId, f.Gateway.LastOperation!.Id);
        Assert.Equal(f.Repository.Row.IdempotencyKey, f.Gateway.LastOperation.Context.IdempotencyKey);
        Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation.Context.ActorSubject);
    }
    [Fact]
    public async Task IdempotentQueueAfterExpiryDoesNotRenewOrCallAgentAndChangedCommandConflicts()
    {
        using var f = new Fixture(); var first = await f.Queue(); var expiry = f.Repository.Row!.PlanningExpiresAtUtc;
        f.Clock.Now = f.Clock.Now.AddHours(1); f.Gateway.Unavailable = true;
        Assert.Equal(ContainerAclWorkflowValidation.Serialize(first), ContainerAclWorkflowValidation.Serialize(await f.Queue())); Assert.Equal(expiry, f.Repository.Row.PlanningExpiresAtUtc); Assert.Equal(1, f.Repository.Enqueues);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, f.Command with { Entries = ContainerAclWorkflowFixture.Named(2300) }, f.Key, default));
    }
    [Fact]
    public async Task ReadAndConfirmationStayBoundToExactOriginalLoginContainerAndPlan()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        var other = f.Actor with { LoginBinding = new('F', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(other, f.Command.Location.ContainerId, ready.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(f.Actor, new('b', 64), ready.Id, default));
        var token = f.Protector.SignAclConfirmation(new(ready.Id, "different-admin", f.Actor.LoginBinding, f.Repository.Row!.PlanHash!, ready.ReadyExpiresAtUtc!.Value));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Location.ContainerId, ready.Id, new(token, true, ready.Version), default));
        Assert.Null(f.Repository.Row.ExecutionJobId); Assert.Equal(0, f.Gateway.Applies);
    }
    [Theory][InlineData(false, 0)][InlineData(true, -1)]
    public async Task CancelingConfirmationOrInvalidVersionCannotCreateExecution(bool confirm, long version)
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Location.ContainerId, ready.Id, new(ready.Confirmation!.PlanHash, confirm, version), default));
        Assert.Null(f.Repository.Row!.ExecutionJobId); Assert.Equal(0, f.Repository.Confirms);
    }
    [Fact]
    public async Task ExpiredReadyReadDoesNotReplanRenewOrOfferConfirmation()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get(); var original = f.Repository.Row!.ReadyExpiresAtUtc;
        f.Clock.Now = f.Clock.Now.AddMinutes(6); var expired = await f.Get();
        Assert.Equal("Expired", expired.State); Assert.Null(expired.Confirmation); Assert.Equal(original, expired.ReadyExpiresAtUtc);
        Assert.Equal(1, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Location.ContainerId, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default));
    }
    [Theory][InlineData("cancel")][InlineData("logout")][InlineData("demotion")][InlineData("expiry")]
    public async Task NoLongerAuthorizedOrExpiredExecutionFencesOriginalIntentWithoutApply(string cause)
    {
        using var f = new Fixture(); await f.Create();
        if (cause == "cancel") f.Repository.Canceled = true;
        if (cause == "logout") f.Logins.Active = false;
        if (cause == "demotion") f.Identity.Admin = false;
        if (cause == "expiry") f.Clock.Now = f.Clock.Now.AddMinutes(16);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(1, f.Gateway.Discards);
        Assert.Equal(cause == "expiry" ? "Failed" : "Canceled", f.Repository.Row!.State);
    }
    [Theory][InlineData("cancel")][InlineData("logout")][InlineData("expiry")]
    public async Task PlanningCanceledOrExpiredBeforeScanNeverBuildsPlan(string cause)
    {
        using var f = new Fixture(); await f.Queue();
        if (cause == "cancel") f.Repository.Canceled = true;
        if (cause == "logout") f.Logins.Active = false;
        if (cause == "expiry") f.Clock.Now = f.Clock.Now.AddMinutes(16);
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Gateway.Plans); Assert.Null(f.Repository.Row!.PlanJson);
        Assert.NotNull(f.Repository.Row.CompletedAtUtc); Assert.Equal(0, f.Gateway.Discards);
    }
    [Fact]
    public async Task AuthorityAfterFinalScanIsRecheckedBeforeSigningReady()
    {
        using var f = new Fixture(); await f.Queue(); f.Gateway.AfterPlan = () => f.Logins.Active = false;
        await f.Dispatcher.TickAsync(default); Assert.Null(f.Repository.Row!.PlanJson); Assert.Equal(1, f.Repository.Deferrals);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Canceled", f.Repository.Row.State);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task CacheOrDirectoryFailureDefersWithoutInventingCancellationOrPublication(bool cache)
    {
        using var f = new Fixture(); await f.Create(); f.Logins.Unavailable = cache; f.Identity.Unavailable = !cache;
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Repository.Deferrals);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards); Assert.False(f.Repository.Row!.PublishBarrier);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task SharedBarrierBusyAndCapabilityDowngradeNeverApply(bool downgrade)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.Unavailable = downgrade; f.Repository.Start = ContainerFileUploadStartResult.Busy;
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Gateway.Applies); Assert.Null(f.Repository.Row!.RootStartedAtUtc);
    }
    [Fact]
    public async Task AuthorityRecheckedAfterBarrierAndLateSuccessReconcilesWithoutAnotherApply()
    {
        using var f = new Fixture(); await f.Create(); f.Repository.AfterStart = () => f.Logins.Active = false;
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Gateway.Applies); Assert.True(f.Repository.Row!.PublishBarrier);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Canceled", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task LostApplyResponseOnlyObservesOrFencesOriginalIdNeverReapplies(bool committed)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { if (committed) f.Gateway.Receipt = Gateway.ReceiptFor(op, "Completed"); throw new IOException("synthetic lost reply"); };
        await f.Dispatcher.TickAsync(default); Assert.True(f.Repository.Row!.PublishBarrier); Assert.Equal(1, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Gateway.Applies);
        Assert.Equal(committed ? "Succeeded" : "Failed", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task PostApplyRevocationDoesNotEraseHistoricalCompletion()
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { f.Gateway.Receipt = Gateway.ReceiptFor(op, "Completed"); f.Logins.Active = false; return f.Gateway.Receipt; };
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Repository.Deferrals); Assert.True(f.Repository.Row!.PublishBarrier);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Succeeded", f.Repository.Row.State); Assert.Equal(1, f.Gateway.Applies);
    }
    [Fact]
    public async Task ChangedCompletedTargetIsSupersededNotFreshSuccess()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Completed") with { BindingCurrent = false };
        await f.Dispatcher.TickAsync(default); Assert.Equal("Superseded", f.Repository.Row!.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Theory][InlineData("job")][InlineData("proof")][InlineData("state")]
    public async Task InvalidRootResponseRetainsUnknownAndCannotBeReportedAsSuccess(string field)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { var r = Gateway.ReceiptFor(op, "Completed"); return field switch {
            "job" => r with { Id = Guid.NewGuid() }, "proof" => r with { TargetProof = new('F', 64) }, _ => r with { State = "Running" } }; };
        await f.Dispatcher.TickAsync(default); Assert.Equal("Unknown", f.Repository.Row!.State);
        Assert.True(f.Repository.Row.PublishBarrier); Assert.Null(f.Repository.Row.CompletedAtUtc); Assert.Equal(1, f.Gateway.Applies);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task MissingReceiptAfterStartedMarkerNeverAuthorizesAnotherApplyEvenAfterDeadline(bool expired)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = _ => throw new IOException("synthetic disconnected Apply"); await f.Dispatcher.TickAsync(default);
        if (expired) f.Clock.Now = f.Clock.Now.AddMinutes(16);
        f.Gateway.DiscardFailure = new IOException("synthetic cannot reach Root fence");
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Gateway.Applies); Assert.True(f.Repository.Row!.PublishBarrier);
        Assert.Equal(expired ? "Unknown" : "Applying", f.Repository.Row.State); Assert.Null(f.Repository.Row.CompletedAtUtc);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ActiveAclCancellationAndLeaseLossCancelRpcThenOnlyFenceOriginalExecution(bool lostLease)
    {
        using var f = new Fixture(); await f.Create(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = false;
        f.Gateway.AsyncApply = async (_, token) => {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
            catch (OperationCanceledException) { canceled = true; throw; }
        };
        var tick = f.Dispatcher.TickAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (lostLease) f.Repository.Renewal = false; else f.Repository.Canceled = true;
        await tick.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(canceled); Assert.True(f.Repository.Row!.PublishBarrier); Assert.Equal(1, f.Gateway.Applies);
        f.Repository.Renewal = true; await f.Dispatcher.TickAsync(default);
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal(1, f.Gateway.Discards); Assert.False(f.Repository.Row.PublishBarrier);
        Assert.Equal(lostLease ? "Failed" : "Canceled", f.Repository.Row.State);
    }
    [Fact]
    public async Task DifferentCurrentAdminAcknowledgesUnknownOnlyAfterOriginalRootFenceAndPreservesPrivateFact()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var original = row.ExecutionJobId;
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier); Assert.True(row.HistoryMayExist);
        var admin = f.Actor with { Subject = "current-other-admin", LoginBinding = new('F', 64) };
        var list = await f.Service.UnknownAsync(admin, 50, default); Assert.Equal(row.Id, Assert.Single(list).Id);
        f.Gateway.DiscardFailure = new IOException("synthetic missing fence");
        await Assert.ThrowsAsync<IOException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); f.Gateway.DiscardFailure = null;
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.False(row.PublishBarrier); Assert.True(row.HistoryMayExist);
        Assert.Equal(original, f.Gateway.LastOperation!.Id); Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation.Context.ActorSubject);
        Assert.Equal(admin.Subject, row.AcknowledgedBy); Assert.Null(row.PublishedVersion);
    }
    [Theory][InlineData("Id")][InlineData("IdempotencyKey")][InlineData("ActorSubject")][InlineData("LoginBinding")][InlineData("TraceId")]
    [InlineData("OwnerHash")][InlineData("ContainerId")][InlineData("InstanceId")][InlineData("RelativePath")]
    [InlineData("CommandJson")][InlineData("RequestHash")][InlineData("CreatedAtUtc")][InlineData("PlanningExpiresAtUtc")]
    [InlineData("PlanJson")][InlineData("PlanHash")][InlineData("ReadyExpiresAtUtc")][InlineData("ExecutionJobId")]
    [InlineData("ConfirmedAtUtc")][InlineData("ExecutionExpiresAtUtc")][InlineData("ExecutionHash")]
    public async Task EveryImmutableRequestPlanAndExecutionFieldIsBoundBeforeAnyRootCall(string name)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        var property = typeof(ContainerAclIntent).GetProperty(name)!; var value = property.GetValue(row)!;
        object changed = value switch { Guid => Guid.NewGuid(), DateTimeOffset t => t.AddMinutes(1), string s => s + "x", _ => throw new NotSupportedException() };
        property.SetValue(row, changed); Assert.False(ContainerAclWorkflowValidation.HasValidBinding(row, f.Protector));
        var inspects = f.Gateway.Inspects; await f.Dispatcher.TickAsync(default);
        Assert.Equal(inspects, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies); Assert.Equal("Unknown", row.State);
    }

    [Theory]
    [InlineData("CONTAINER_ACL_VERSION_CONFLICT")][InlineData("CONTAINER_ACL_EXTENDED_METADATA_REQUIRED")]
    [InlineData("CONTAINER_ACL_SOURCE_UNSUPPORTED")][InlineData("CONTAINER_ACL_TARGET_CHANGED")]
    public async Task ExplicitPreflightRefusalFailsPlanningPromptlyWithoutRootMutation(string code)
    {
        using var f = new Fixture(); await f.Queue(); f.Gateway.PlanFailure = new ContainerAclPreflightRejectedException(code);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Failed", f.Repository.Row!.State); Assert.Equal(code, f.Repository.Row.ResultCode);
        Assert.NotNull(f.Repository.Row.CompletedAtUtc); Assert.Null(f.Repository.Row.ExecutionJobId); Assert.False(f.Repository.Row.PublishBarrier);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards); Assert.Equal(0, f.Repository.Deferrals);
    }
    [Fact]
    public async Task LiveDemotionCannotUseThePositiveCachedAdministratorOrRegisterAPlan()
    {
        using var f = new Fixture(); f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Queue());
        Assert.Null(f.Repository.Row); Assert.Equal(0, f.Gateway.Requires);
    }

    [Fact]
    public async Task LostLiveIdentityCannotUseThePositiveCachedAdministrator()
    {
        using var f = new Fixture(); f.Identity.LiveUnavailable = true;
        await Assert.ThrowsAsync<IOException>(() => f.Queue());
        Assert.Null(f.Repository.Row); Assert.Equal(0, f.Gateway.Requires);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task LiveAuthorityLossDuringApplyCancelsRpcDespitePositiveCachedPermission(bool unavailable)
    {
        using var f = new Fixture(); await f.Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var canceled = false;
        f.Gateway.AsyncApply = async (_, token) => {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
            catch (OperationCanceledException) { canceled = true; throw; }
        };
        var tick = f.Dispatcher.TickAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (unavailable) f.Identity.LiveUnavailable = true; else f.Identity.LiveAdmin = false;
        await tick.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(f.Identity.Admin); Assert.True(canceled); Assert.True(f.Repository.Row!.PublishBarrier);
        Assert.Equal(1, f.Gateway.Applies);
        f.Identity.LiveUnavailable = false; f.Identity.LiveAdmin = false;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Canceled", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal(1, f.Gateway.Discards);
    }
    [Fact]
    public async Task MutableCallerEntriesCannotChangeQueuedAuthorityDuringCapabilityAwait()
    {
        using var f = new Fixture(); var entries = ContainerAclWorkflowFixture.Named().ToList();
        var command = f.Command with { Entries = entries };
        f.Gateway.AfterRequire = () => entries[1] = entries[1] with { Id = 0, Permissions = 0 };
        var queued = await f.Service.QueueAsync(f.Actor, command, f.Key, default);
        Assert.Equal(2200U, queued.Command.Entries[1].Id); Assert.Equal(4U, queued.Command.Entries[1].Permissions);
        Assert.True(ContainerAclWorkflowValidation.HasValidBinding(f.Repository.Row!, f.Protector));
        f.Gateway.AfterRequire = null;
        var same = command with { Entries = ContainerAclWorkflowFixture.Named() };
        Assert.Equal(queued.Id, (await f.Service.QueueAsync(f.Actor, same, f.Key, default)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, command, f.Key, default));
        Assert.Equal(1, f.Repository.Enqueues);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ReturnedListsCannotMutateOriginalAuthorityOrFrozenPlan(bool planned)
    {
        using var f = new Fixture(); if (planned) await f.Ready(); else await f.Queue();
        var dto = await f.Get(); var stored = f.Repository.Row!.CommandJson; var plan = f.Repository.Row.PlanJson;
        ((IList<ContainerAclEntryDto>)dto.Command.Entries)[1] = new("user", 0, 0);
        if (planned) ((IList<ContainerAclEntryDto>)dto.OriginalAccessAcl!)[0] = new("user_obj", null, 0);
        Assert.Equal(stored, f.Repository.Row.CommandJson); Assert.Equal(plan, f.Repository.Row.PlanJson);
        Assert.True(ContainerAclWorkflowValidation.HasValidBinding(f.Repository.Row, f.Protector));
        Assert.Equal(2200U, (await f.Get()).Command.Entries[1].Id);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DirectoryDefaultSetAndExplicitClearKeepModeAndAccessAcl(bool clear)
    {
        using var f = new Fixture(true); var p = f.Gateway.PlanValue;
        f.Gateway.PlanValue = p with { Command = p.Command with { AclType = "default", Entries = clear ? [] : ContainerAclWorkflowFixture.Named(0, 0, 0) },
            DefaultAcl = clear ? ContainerAclWorkflowFixture.Named() : [], TargetMode = p.SourceIdentity.Mode & 511 };
        await f.Ready(); var ready = await f.Get();
        Assert.Equal("default", ready.Command.AclType); Assert.Equal(488U, ready.TargetMode); Assert.Equal(clear ? 0 : 5, ready.Command.Entries.Count);
        Assert.Equal(p.AccessAcl, ready.OriginalAccessAcl); Assert.False(ready.NoChange);
        await f.Confirm(); await f.Dispatcher.TickAsync(default);
        var done = await f.Get(); Assert.Equal("Succeeded", done.State); Assert.True(done.AclConfirmed); Assert.True(done.HistoryVerified);
        Assert.Equal("default", f.Gateway.LastOperation!.Plan.Command.AclType); Assert.Equal(488U, f.Gateway.LastOperation.Plan.TargetMode);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task NoChangeStillRequiresConfirmationAndMetadataProof(bool directory)
    {
        using var f = new Fixture(directory); var p = f.Gateway.PlanValue;
        f.Gateway.PlanValue = p with { Command = p.Command with { AclType = directory ? "default" : "access", Entries = directory ? [] : p.AccessAcl.ToArray() },
            TargetMode = p.SourceIdentity.Mode & 511, NoChange = true };
        await f.Ready(); Assert.True((await f.Get()).NoChange); Assert.Equal(0, f.Gateway.Applies);
        await f.Confirm(); await f.Dispatcher.TickAsync(default); Assert.True((await f.Get()).HistoryVerified); Assert.Equal(1, f.Gateway.Applies);
    }
    [Theory][InlineData("mapping")][InlineData("unsupported")][InlineData("file-default")][InlineData("invalid-plan")][InlineData("changed-command")]
    public async Task AclSpecificPreflightRefusalAndUntrustedPlanCannotRegisterReady(string cause)
    {
        using var f = new Fixture(); await f.Queue(); var p = f.Gateway.PlanValue;
        if (cause is "mapping" or "unsupported") f.Gateway.PlanFailure = new ContainerAclPreflightRejectedException(cause == "mapping" ? "CONTAINER_ACL_MAPPING_UNPROVEN" : "CONTAINER_ACL_UNSUPPORTED");
        if (cause == "file-default") f.Gateway.PlanValue = p with { Command = p.Command with { AclType = "default" } };
        if (cause == "invalid-plan") f.Gateway.PlanValue = p with { TargetMode = 511 };
        if (cause == "changed-command") f.Gateway.PlanValue = p with { Command = p.Command with { Entries = ContainerAclWorkflowFixture.Named(0) } };
        await f.Dispatcher.TickAsync(default); Assert.Equal("Failed", f.Repository.Row!.State); Assert.Null(f.Repository.Row.PlanJson);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Repository.Deferrals);
    }
    [Theory][InlineData("Unknown")][InlineData("Aborted")]
    public async Task TrustedOriginalReceiptIsRecordedDuringLiveIdentityOutage(string state)
    {
        using var f = new Fixture(); await f.Create(); var op = ContainerAclWorkflowValidation.Reference(f.Repository.Row!);
        f.Gateway.Receipt = Gateway.ReceiptFor(op, state); f.Identity.LiveUnavailable = true;
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.Equal(state == "Unknown" ? "Unknown" : "Failed", row.State); Assert.Equal(0, f.Repository.Deferrals);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards);
        Assert.Equal(state == "Unknown" ? null : (bool?)false, row.AclConfirmed);
        Assert.True(ContainerAclWorkflowValidation.HasValidBinding(row, f.Protector));
    }
    [Theory][InlineData("receipt")][InlineData("signature")][InlineData("confirmation")][InlineData("history")]
    public async Task SavedRootFactsCannotBeTamperedOrInventedEvenWithValidReceiptShape(string field)
    {
        using var f = new Fixture(); await f.Create(); await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.True(ContainerAclWorkflowValidation.HasValidBinding(row, f.Protector));
        var receipt = ContainerAclWorkflowValidation.ReadReceipt(row)!;
        if (field == "receipt") { receipt = receipt with { Version = new('A', 64) }; row.ReceiptJson = ContainerAclWorkflowValidation.Serialize(receipt); row.PublishedVersion = receipt.Version; }
        if (field == "signature") row.ReceiptHash = new('A', 64);
        if (field == "confirmation") row.AclConfirmed = false;
        if (field == "history") row.HistoryDigest = new('A', 64);
        Assert.False(ContainerAclWorkflowValidation.HasValidBinding(row, f.Protector));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get());
    }
    [Fact]
    public async Task UnknownAcknowledgementRejectsContradictoryFenceWithoutErasingPriorFact()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var original = row.ReceiptJson;
        f.Gateway.Receipt = Gateway.ReceiptFor(ContainerAclWorkflowValidation.Reference(row), "Aborted");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default));
        Assert.Equal(original, row.ReceiptJson); Assert.True(row.PublishBarrier); Assert.Equal("Unknown", row.State); Assert.Null(row.AclConfirmed);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ReadsRecheckLiveAuthorityAfterRepositoryWait(bool idempotentQueue)
    {
        using var f = new Fixture(); await f.Queue(); f.Repository.AfterRead = () => f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => idempotentQueue ? f.Queue() : f.Get());
        Assert.Equal(1, f.Repository.Enqueues); Assert.Equal(0, f.Gateway.Plans);
    }
    [Theory][InlineData("Completed", false)][InlineData("Completed", null)][InlineData("Unknown", true)][InlineData("Unknown", false)]
    [InlineData("Aborted", true)][InlineData("Aborted", null)]
    public async Task InvalidTriStateConfirmationCannotReleaseProtectionOrReportSuccess(string state, bool? confirmed)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, state) with { AclConfirmed = confirmed };
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier); Assert.Null(row.CompletedAtUtc);
        Assert.Null(row.AclConfirmed); Assert.Null(row.ReceiptJson); Assert.Null(row.PublishedVersion);
    }
    [Theory][InlineData("type")][InlineData("mode")][InlineData("history")][InlineData("digest")]
    public async Task MismatchedAclReceiptCannotBecomeBusinessSuccess(string field)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { var r = Gateway.ReceiptFor(op, "Completed"); return field switch {
            "type" => r with { AclType = "default" }, "mode" => r with { TargetMode = 511 },
            "history" => r with { HistoryVerified = false, HistoryDigest = "" }, _ => r with { HistoryDigest = new('Z', 64) } }; };
        await f.Dispatcher.TickAsync(default); Assert.Equal("Unknown", f.Repository.Row!.State);
        Assert.True(f.Repository.Row.PublishBarrier); Assert.Null(f.Repository.Row.ReceiptJson);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ValidAclEntryTamperingCannotReachAnyRootCall(bool plan)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        if (plan) {
            var value = ContainerAclWorkflowValidation.ReadPlan(row);
            row.PlanJson = ContainerAclWorkflowValidation.Serialize(value with { Command = value.Command with { Entries = ContainerAclWorkflowFixture.Named(0) } });
        } else typeof(ContainerAclIntent).GetProperty(nameof(row.CommandJson))!.SetValue(row,
            ContainerAclWorkflowValidation.Serialize(f.Command with { Entries = ContainerAclWorkflowFixture.Named(0) }));
        await f.Dispatcher.TickAsync(default); Assert.Equal("Unknown", row.State);
        Assert.Equal(0, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName(); public Clock Clock { get; } = new();
        public Identity Identity { get; } public Logins Logins { get; } = new(); public Gateway Gateway { get; }
        public Repository Repository { get; } public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("copy-test-admin", null, [], new('A', 64), "copy-test-trace", null);
        public string Key { get; } = Guid.NewGuid().ToString("D"); public ContainerAclCommandDto Command => Gateway.PlanValue.Command;
        public ContainerAclWorkflowService Service { get; } public ContainerAclDispatcher Dispatcher { get; }
        public Fixture(bool directory = false, IContainerAclRestoreSourceGateway? restoreSource = null)
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock); Gateway = new(directory); Repository = new(Protector, Clock);
            var files = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var auth = new ContainerFileUploadService(files, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Service = new(auth, Repository, Protector, Gateway, Clock, Identity, restoreSource); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerAclIntentDto> Queue() => Service.QueueAsync(Actor, Command, Key, default);
        public Task<ContainerAclIntentDto> Get() => Service.GetAsync(Actor, Command.Location.ContainerId, Repository.Row!.Id, default);
        public async Task Ready() { await Queue(); await Dispatcher.TickAsync(default); }
        public async Task<ContainerAclIntentDto> Confirm() { var r = await Get(); return await Service.ConfirmAsync(Actor, Command.Location.ContainerId, r.Id, new(r.Confirmation!.PlanHash, true, r.Version), default); }
        public async Task Create() { await Ready(); await Confirm(); }
        public void Dispose() => File.Delete(_key);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(TimeProvider clock) : IIdentityDirectory, IPrivilegedIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Unavailable { get; set; }
        public bool? LiveAdmin { get; set; } public bool LiveUnavailable { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken token) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic identity unavailable")) : Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken token) =>
            LiveUnavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic live identity unavailable")) :
            LiveAdmin is { } admin ? Task.FromResult(new CurrentUserDto(subject, name, [], admin, clock.GetUtcNow().AddHours(2))) : ResolveCurrentAsync(subject, name, [], token);
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Logins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string binding, CancellationToken token) => Unavailable ? Task.FromException<bool>(new IOException("synthetic cache unavailable")) : Task.FromResult(Active);
    }
    private sealed class Gateway(bool directory) : IContainerAclGateway
    {
        public ContainerAclPlanDto PlanValue { get; set; } = ContainerAclWorkflowFixture.Plan(directory);
        public Exception? PlanFailure;
        public int Requires, Plans, Inspects, Applies, Discards; public bool Unavailable; public Action? AfterPlan; public Action? AfterRequire;
        public ContainerAclReceiptDto? Receipt; public Exception? DiscardFailure; public ContainerAclOperationDto? LastOperation;
        public Func<ContainerAclOperationDto, ContainerAclReceiptDto>? ApplyHandler;
        public Func<ContainerAclOperationDto, CancellationToken, Task<ContainerAclReceiptDto>>? AsyncApply;
        public Task RequireContainerAclWorkflowAsync(CancellationToken token) { Requires++; AfterRequire?.Invoke(); if (Unavailable) throw new IOException("synthetic unsupported Agent"); return Task.CompletedTask; }
        public Task<ContainerAclPlanDto> PlanContainerAclAsync(TerminalActor actor, ContainerAclCommandDto command, CancellationToken token) { Plans++; if (PlanFailure is not null) throw PlanFailure; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerAclReceiptDto?> InspectContainerAclAsync(ContainerAclOperationDto op, CancellationToken token) { Inspects++; LastOperation = op; return Task.FromResult(Receipt); }
        public async Task<ContainerAclReceiptDto> ApplyContainerAclAsync(ContainerAclOperationDto op, CancellationToken token)
        { Applies++; LastOperation = op; Receipt = AsyncApply is null ? ApplyHandler?.Invoke(op) ?? ReceiptFor(op, "Completed") : await AsyncApply(op, token); return Receipt; }
        public Task<ContainerAclReceiptDto> DiscardContainerAclAsync(ContainerAclOperationDto op, CancellationToken token) { Discards++; LastOperation = op; if (DiscardFailure is not null) throw DiscardFailure; return Task.FromResult(Receipt ?? ReceiptFor(op, "Aborted")); }
        public static ContainerAclReceiptDto ReceiptFor(ContainerAclOperationDto op, string state) => new(op.Id, op.Plan.Command.Location.ContainerId, op.Plan.Command.Location.InstanceId,
            op.Plan.Fingerprint, op.Plan.TargetProof, state, state == "Completed" ? new('E', 64) : "", state == "Completed", false, op.Plan.Kind,
            state == "Unknown" ? null : state == "Completed", op.Plan.NoChange, state != "Aborted", state == "Completed", state == "Completed" ? new('F', 64) : "", op.Plan.Command.AclType, op.Plan.TargetMode);
    }
    private sealed class Repository(IContainerAclProtector protector, Clock clock) : IContainerAclRepository
    {
        public ContainerAclIntent? Row; public int Enqueues, Deferrals, Confirms; public bool Canceled; public bool Renewal = true; public Action? AfterStart; public Action? AfterRead;
        public ContainerFileUploadStartResult Start = ContainerFileUploadStartResult.Started;
        public Task<ContainerAclIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken token) { var r = Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null; AfterRead?.Invoke(); return Task.FromResult(r); }
        public Task<ContainerAclIntent?> FindAsync(Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id ? Row : null);
        public Task<IReadOnlyList<ContainerAclIntent>> UnknownAsync(int take, CancellationToken token) => Task.FromResult<IReadOnlyList<ContainerAclIntent>>(Row?.State == "Unknown" ? [Row] : []);
        public Task<ContainerAclIntent> EnqueueAsync(ContainerAclIntent row, CancellationToken token) { Enqueues++; Row = row; return Task.FromResult(row); }
        public Task<ContainerAclIntent> ConfirmAsync(TerminalActor actor, ContainerAclConfirmation claims, long version, CancellationToken token)
        {
            var r = Row!; if (r.ReadyExpiresAtUtc <= clock.Now) throw new InvalidOperationException("CONTAINER_ACL_PLAN_EXPIRED");
            Confirms++; r.ExecutionJobId = ContainerAclWorkflowValidation.ExecutionId(r.Id); r.ConfirmedAtUtc = clock.Now; r.ExecutionExpiresAtUtc = clock.Now.AddMinutes(15);
            r.ExecutionHash = protector.FingerprintAclExecution(ContainerAclWorkflowValidation.ExecutionBinding(r)); r.State = "Pending"; r.Version++; return Task.FromResult(r);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerAclIntent?> ClaimAsync(DateTimeOffset now, CancellationToken token) { if (Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown") return Task.FromResult<ContainerAclIntent?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerAclIntent?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerAclIntent row, CancellationToken token) { row.State = "Planning"; return Task.FromResult(true); }
        public Task FinishPlanningAsync(ContainerAclIntent row, ContainerAclPlanDto? plan, string code, CancellationToken token)
        {
            row.ResultCode = code; row.Version++;
            if (plan is null) { row.State = code == "CONTAINER_ACL_CANCELED" ? "Canceled" : "Failed"; row.CompletedAtUtc = clock.Now; }
            else { row.PlanJson = ContainerAclWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = clock.Now.AddMinutes(5); row.PlanHash = protector.FingerprintAclPlan(ContainerAclWorkflowValidation.PlanBinding(row)); row.State = "Ready"; }
            return Task.CompletedTask;
        }
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerAclIntent row, DateTimeOffset now, CancellationToken token)
        { if (Start == ContainerFileUploadStartResult.Started) { row.RootStartedAtUtc = now; row.PublishBarrier = true; row.State = "Applying"; AfterStart?.Invoke(); } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerAclIntent row, DateTimeOffset now, CancellationToken token) => Task.FromResult(Renewal);
        public Task<bool> FinishAsync(ContainerAclIntent row, string state, string code, ContainerAclReceiptDto? receipt, CancellationToken token)
        { row.State = state; row.ResultCode = code; row.HistoryMayExist = state == "Unknown" || receipt?.HistoryMayExist == true || row.HistoryMayExist;
          if (receipt is not null) { row.ReceiptJson = ContainerAclWorkflowValidation.Serialize(receipt); row.ReceiptHash = protector.FingerprintAclReceipt(new(ContainerAclWorkflowValidation.Reference(row), receipt)); row.AclConfirmed = receipt.AclConfirmed;
              row.PublishedVersion = receipt.State == "Completed" ? receipt.Version : null; row.HistoryVerified = receipt.HistoryVerified;
              row.HistoryDigest = receipt.HistoryVerified ? receipt.HistoryDigest : null; }
          row.Version++; if (state != "Unknown") { row.CompletedAtUtc = clock.Now; row.PublishBarrier = false; } return Task.FromResult(true); }
        public Task DeferAsync(ContainerAclIntent row, CancellationToken token) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerAclReceiptDto receipt, CancellationToken token)
        { ContainerAclWorkflowValidation.RequireCompatibleReceipt(Row!, receipt); Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject; Row.AcknowledgementJson = JsonSerializer.Serialize(receipt); Row.Version++; return Task.CompletedTask; }
    }
}
