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
using static WhaleDeck.UnitTests.Application.ContainerRecursiveAclWorkflowFixture;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Business/cryptographic boundaries with an explicit in-memory repository; not SQL or installed evidence.</summary>
public sealed partial class ContainerRecursiveAclWorkflowTests
{
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task OrdinaryUserAndRevokedLoginCannotQueueOrReachAgent(bool admin, bool login)
    {
        using var f = new Fixture(); f.Identity.Admin = admin; f.Logins.Active = login;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Queue());
        Assert.Equal(0, f.Gateway.Requires); Assert.Null(f.Repository.Row);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task QueueNeverScansOrChangesAclsAndPlanningNeedsSeparateConfirmation(bool directory)
    {
        using var f = new Fixture(directory); var queued = await f.Queue();
        Assert.Equal("Queued", queued.State); Assert.Null(queued.Confirmation); Assert.Null(queued.ExecutionJobId);
        Assert.Equal(0, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default);
        var ready = await f.Get(); Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation);
        Assert.Equal(4U, ready.EntryCount); Assert.Equal(2U, ready.FileCount); Assert.Equal(1000U, ready.RootUid); Assert.Equal(1001U, ready.RootGid);
        Assert.Equal(5100U, ready.ExecutorUid); Assert.Equal(5101U, ready.ExecutorGid); Assert.Equal(directory ? 0U : 4U, ready.ChangeCount);
        Assert.True(ContainerRecursiveAclValidation.SameCommand(f.Command,ready.Command)); Assert.Equal(16872U,ready.RootMode);
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
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(await f.Queue())); Assert.Equal(expiry, f.Repository.Row.PlanningExpiresAtUtc); Assert.Equal(1, f.Repository.Enqueues);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, f.Command with { FileEntries = Entries(5) }, f.Key, default));
    }
    [Fact]
    public async Task ReadAndConfirmationStayBoundToExactOriginalLoginContainerAndPlan()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        var other = f.Actor with { LoginBinding = new('F', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(other, f.Command.Location.ContainerId, ready.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(f.Actor, new('b', 64), ready.Id, default));
        var token = f.Protector.SignRecursiveAclConfirmation(new(ready.Id, "different-admin", f.Actor.LoginBinding, f.Repository.Row!.PlanHash!, ready.ReadyExpiresAtUtc!.Value));
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
    public async Task ActiveRecursiveAclCancellationAndLeaseLossCancelRpcThenOnlyFenceOriginalExecution(bool lostLease)
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
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => f.Gateway.Receipt = Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var original = row.ExecutionJobId;
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier); Assert.True(row.HistoryMayExist);
        var admin = f.Actor with { Subject = "current-other-admin", LoginBinding = new('F', 64) };
        var list = await f.Service.UnresolvedAsync(admin, 50, default); Assert.Equal(row.Id, Assert.Single(list).Id);
        f.Gateway.DiscardFailure = new IOException("synthetic missing fence");
        await Assert.ThrowsAsync<IOException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); f.Gateway.DiscardFailure = null;
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.False(row.PublishBarrier); Assert.True(row.HistoryMayExist);
        Assert.Equal(original, f.Gateway.LastOperation!.Id); Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation.Context.ActorSubject);
        Assert.Equal(admin.Subject, row.AcknowledgedBy); Assert.Equal("Unknown", row.RootState);
    }
    [Theory][InlineData("Id")][InlineData("IdempotencyKey")][InlineData("ActorSubject")][InlineData("LoginBinding")][InlineData("TraceId")]
    [InlineData("OwnerHash")][InlineData("ContainerId")][InlineData("InstanceId")][InlineData("RelativePath")]
    [InlineData("CommandJson")][InlineData("RequestHash")][InlineData("CreatedAtUtc")][InlineData("PlanningExpiresAtUtc")]
    [InlineData("PlanJson")][InlineData("PlanHash")][InlineData("ReadyExpiresAtUtc")][InlineData("ExecutionJobId")]
    [InlineData("ConfirmedAtUtc")][InlineData("ExecutionExpiresAtUtc")][InlineData("ExecutionHash")]
    public async Task EveryImmutableRequestPlanAndExecutionFieldIsBoundBeforeAnyRootCall(string name)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        var property = typeof(ContainerRecursiveAclIntent).GetProperty(name)!; var value = property.GetValue(row)!;
        object changed = value switch { Guid => Guid.NewGuid(), DateTimeOffset t => t.AddMinutes(1), string s => s + "x", _ => throw new NotSupportedException() };
        property.SetValue(row, changed); Assert.False(ContainerRecursiveAclWorkflowValidation.HasValidBinding(row, f.Protector));
        var inspects = f.Gateway.Inspects; await f.Dispatcher.TickAsync(default);
        Assert.Equal(inspects, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies); Assert.Equal("Unknown", row.State);
    }

    [Theory]
    [InlineData("CONTAINER_RECURSIVE_ACL_VERSION_CONFLICT")][InlineData("CONTAINER_RECURSIVE_ACL_EXTENDED_METADATA_REQUIRED")]
    [InlineData("CONTAINER_RECURSIVE_ACL_SOURCE_UNSUPPORTED")][InlineData("CONTAINER_RECURSIVE_ACL_TARGET_CHANGED")]
    public async Task ExplicitPreflightRefusalFailsPlanningPromptlyWithoutRootMutation(string code)
    {
        using var f = new Fixture(); await f.Queue(); f.Gateway.PlanFailure = new ContainerRecursiveAclPreflightRejectedException(code);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Failed", f.Repository.Row!.State); Assert.Equal(code, f.Repository.Row.ResultCode);
        Assert.NotNull(f.Repository.Row.CompletedAtUtc); Assert.Null(f.Repository.Row.ExecutionJobId); Assert.False(f.Repository.Row.PublishBarrier);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards); Assert.Equal(0, f.Repository.Deferrals);
    }
    [Theory][InlineData("Partial")][InlineData("Unknown")]
    public async Task UnresolvedCountsStayVisibleAndAnotherAdminMustFenceOriginalIdBeforeRelease(string state)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => f.Gateway.Receipt = Gateway.ReceiptFor(op, state);
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.Equal(state, row.State); Assert.True(row.PublishBarrier); Assert.Null(row.CompletedAtUtc);
        var shown = await f.Get(); Assert.Equal(state, shown.Receipt!.State);
        Assert.Equal(state == "Partial" ? 1U : 0U, shown.Receipt.ChangedCount);
        Assert.Equal(state == "Partial" ? 3U : 0U, shown.Receipt.NotStartedCount);
        var calls = f.Gateway.Applies; await f.Dispatcher.TickAsync(default); Assert.Equal(calls, f.Gateway.Applies);
        var admin = f.Actor with { Subject = "new-current-admin", LoginBinding = new('9', 64) };
        Assert.Equal(row.Id, Assert.Single(await f.Service.UnresolvedAsync(admin, 50, default)).Id);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, false), default));
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.Equal(state, row.RootState); Assert.False(row.PublishBarrier);
        Assert.Equal(admin.Subject, row.AcknowledgedBy); Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation!.Context.ActorSubject);
        Assert.Equal(calls, f.Gateway.Applies); Assert.NotNull(row.ReceiptJson);
    }
    [Theory][InlineData("FileEntries")][InlineData("DirectoryEntries")][InlineData("ManifestDigest")][InlineData("EntryCount")][InlineData("ChangeCount")]
    public async Task RecursivePlanMutationCannotAcquirePublicationAuthority(string field)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        var plan = ContainerRecursiveAclWorkflowValidation.ReadPlan(row);
        plan = field switch { "FileEntries" => plan with { Command = plan.Command with { FileEntries = Entries(5) } },
            "DirectoryEntries" => plan with { Command = plan.Command with { DirectoryEntries = Entries(5) } },
            "ManifestDigest" => plan with { ManifestDigest = new('9', 64) }, "EntryCount" => plan with { EntryCount = 8, DirectoryCount = 6 },
            _ => plan with { ChangeCount = 1 } };
        row.PlanJson = ContainerRecursiveAclWorkflowValidation.Serialize(plan);
        Assert.False(ContainerRecursiveAclWorkflowValidation.HasValidBinding(row, f.Protector));
        await f.Dispatcher.TickAsync(default); Assert.Equal("Unknown", row.State); Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Inspects);
    }
    [Theory][InlineData("Partial")][InlineData("Unknown")]
    public async Task AcknowledgementCannotReplaceAlreadyObservedRootFactsWithContradictoryFence(string state)
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => f.Gateway.Receipt = Gateway.ReceiptFor(op, state);
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var original = row.ReceiptJson;
        f.Gateway.Receipt = Gateway.ReceiptFor(f.Gateway.LastOperation!, "Aborted");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default));
        Assert.Equal("CONTAINER_RECURSIVE_ACL_FACT_CHANGED", error.Message); Assert.Equal(original, row.ReceiptJson);
        Assert.True(row.PublishBarrier); Assert.Equal(state, row.State); Assert.Null(row.AcknowledgedBy);
    }
    [Fact]
    public async Task AcknowledgementMayObserveUnavailableHistoryWithoutErasingHistoricalProofOrPartialCounts()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => f.Gateway.Receipt = Gateway.ReceiptFor(op, "Partial");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var digest = row.HistoryDigest;
        f.Gateway.Receipt = f.Gateway.Receipt! with { HistoryAvailable = false, Replayed = true };
        await f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default);
        var shown = await f.Get(); Assert.Equal("Acknowledged", shown.State); Assert.Equal("Partial", shown.Receipt!.State);
        Assert.True(shown.HistoryVerified); Assert.Equal(digest, shown.HistoryDigest); Assert.False(shown.Receipt.HistoryAvailable);
        Assert.Equal(1U, shown.Receipt.ChangedCount); Assert.Equal(3U, shown.Receipt.NotStartedCount);
    }
    [Fact]
    public async Task AcknowledgementCannotErasePreviouslyObservedPossibleHistory()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => f.Gateway.Receipt = Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        f.Gateway.Receipt = f.Gateway.Receipt! with { HistoryMayExist = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); Assert.True(row.HistoryMayExist); Assert.Equal("Unknown", row.State);
    }
    [Fact]
    public async Task PersistedReceiptAndVisibleCountersCannotDivergeSilently()
    {
        using var f = new Fixture(); await f.Create(); await f.Dispatcher.TickAsync(default);
        f.Repository.Row!.ChangedCount = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get());
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
    [Theory][InlineData("owned")][InlineData("list")][InlineData("recovery")][InlineData("retry")][InlineData("cancel")]
    public async Task AuthorityLostDuringRepositoryReadCannotReturnPrivilegedMetadata(string operation)
    {
        using var f = new Fixture(); await f.Ready();
        f.Repository.AfterRead = () => f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            if (operation == "owned") await f.Get();
            else if (operation == "list") await f.Service.UnresolvedAsync(f.Actor,50,default);
            else if (operation == "retry") await f.Queue();
            else if (operation == "cancel") await f.Service.CancelAsync(f.Actor,f.Command.Location.ContainerId,f.Repository.Row!.Id,default);
            else await f.Service.RecoveryGetAsync(f.Actor,f.Repository.Row!.Id,default);
        });
        Assert.True(f.Identity.Admin); Assert.Equal(0,f.Gateway.Applies); Assert.False(f.Repository.Canceled);
    }
    [Theory][InlineData("queue")][InlineData("confirm")][InlineData("cancel")]
    public async Task AuthorityLostDuringRepositoryWriteCannotReturnPrivateResultOrPermitLaterApply(string operation)
    {
        using var f = new Fixture();
        if (operation != "queue") await f.Ready();
        f.Repository.AfterWrite = () => f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => {
            if (operation == "queue") await f.Queue();
            else if (operation == "confirm") await f.Confirm();
            else await f.Service.CancelAsync(f.Actor,f.Command.Location.ContainerId,f.Repository.Row!.Id,default);
        });
        Assert.NotNull(f.Repository.Row); Assert.True(f.Identity.Admin);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0,f.Gateway.Applies); Assert.Equal("Canceled",f.Repository.Row.State);
    }
    [Fact]
    public async Task NamedRootAclIdStaysDistinctFromInodeOwnerAndExecutor()
    {
        using var f = new Fixture(); f.Gateway.PlanValue = f.Gateway.PlanValue with { Command = f.Command with { FileEntries = Entries(4,0,0), DirectoryEntries = Entries(7,0,0) } };
        await f.Ready(); var ready = await f.Get();
        Assert.Equal(0U,ready.Command.FileEntries![1].Id); Assert.Equal(0U,ready.Command.FileEntries[1].Permissions);
        Assert.Equal((1000U,1001U),(ready.RootUid,ready.RootGid)); Assert.Equal((5100U,5101U),(ready.ExecutorUid,ready.ExecutorGid));
        Assert.Contains("不更改inode所有者",ready.Confirmation!.Warning,StringComparison.Ordinal);
        await f.Confirm(); await f.Dispatcher.TickAsync(default);
        Assert.True(ContainerRecursiveAclValidation.SameCommand(ready.Command,f.Gateway.LastOperation!.Plan.Command));
    }
    [Theory]
    [InlineData("Partial",false)][InlineData("Partial",true)][InlineData("Unknown",false)][InlineData("Unknown",true)]
    [InlineData("Aborted",false)][InlineData("Aborted",true)]
    public async Task TrustedRootOutcomeSurvivesIdentityOutageEvenPastExecutionExpiry(string rootState,bool expired)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => {
            f.Gateway.Receipt = Gateway.ReceiptFor(op,rootState); f.Identity.LiveUnavailable = true; return f.Gateway.Receipt;
        };
        await f.Dispatcher.TickAsync(default); Assert.Equal(1,f.Gateway.Applies); Assert.True(f.Repository.Row!.PublishBarrier);
        if (expired) f.Clock.Now = f.Clock.Now.AddMinutes(16);
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row;
        Assert.Equal(rootState == "Aborted" ? "Failed" : rootState,row.State); Assert.Equal(rootState,row.RootState);
        Assert.Equal(f.Gateway.Receipt!.ChangedCount,(uint)row.ChangedCount!.Value);
        Assert.Equal(f.Gateway.Receipt.UnknownCount,(uint)row.UnknownCount!.Value);
        Assert.Equal(f.Gateway.Receipt.HistoryDigest,row.HistoryDigest ?? "");
        Assert.Equal(rootState != "Aborted",row.PublishBarrier); Assert.Equal(1,f.Gateway.Applies);
        Assert.Equal(0,f.Gateway.Discards);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName(); public Clock Clock { get; } = new();
        public Identity Identity { get; } public Logins Logins { get; } = new(); public Gateway Gateway { get; }
        public Repository Repository { get; } public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("copy-test-admin", null, [], new('A', 64), "copy-test-trace", null);
        public string Key { get; } = Guid.NewGuid().ToString("D"); public ContainerRecursiveAclCommandDto Command => Gateway.PlanValue.Command;
        public ContainerRecursiveAclWorkflowService Service { get; } public ContainerRecursiveAclDispatcher Dispatcher { get; }
        public Fixture(bool directory = false)
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock); Gateway = new(directory); Repository = new(Protector, Clock);
            var files = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var auth = new ContainerFileUploadService(files, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Service = new(auth, Repository, Protector, Gateway, Clock, Identity); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerRecursiveAclIntentDto> Queue() => Service.QueueAsync(Actor, Command, Key, default);
        public Task<ContainerRecursiveAclIntentDto> Get() => Service.GetAsync(Actor, Command.Location.ContainerId, Repository.Row!.Id, default);
        public async Task Ready() { await Queue(); await Dispatcher.TickAsync(default); }
        public async Task<ContainerRecursiveAclIntentDto> Confirm() { var r = await Get(); return await Service.ConfirmAsync(Actor, Command.Location.ContainerId, r.Id, new(r.Confirmation!.PlanHash, true, r.Version), default); }
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
    private sealed class Gateway(bool directory) : IContainerRecursiveAclGateway
    {
        public ContainerRecursiveAclPlanDto PlanValue { get; set; } = ContainerRecursiveAclWorkflowFixture.Plan(directory);
        public Exception? PlanFailure;
        public int Requires, Plans, Inspects, Applies, Discards; public bool Unavailable; public Action? AfterPlan;
        public ContainerRecursiveAclReceiptDto? Receipt; public Exception? DiscardFailure; public ContainerRecursiveAclOperationDto? LastOperation;
        public Func<ContainerRecursiveAclOperationDto, ContainerRecursiveAclReceiptDto>? ApplyHandler;
        public Func<ContainerRecursiveAclOperationDto, CancellationToken, Task<ContainerRecursiveAclReceiptDto>>? AsyncApply;
        public Task RequireContainerRecursiveAclWorkflowAsync(CancellationToken token) { Requires++; if (Unavailable) throw new IOException("synthetic unsupported Agent"); return Task.CompletedTask; }
        public Task<ContainerRecursiveAclPlanDto> PlanContainerRecursiveAclAsync(TerminalActor actor, ContainerRecursiveAclCommandDto command, CancellationToken token) { Plans++; if (PlanFailure is not null) throw PlanFailure; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerRecursiveAclReceiptDto?> InspectContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto op, CancellationToken token) { Inspects++; LastOperation = op; return Task.FromResult(Receipt); }
        public Task<ContainerRecursiveAclReceiptDto> ApplyContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto op, CancellationToken token) { Applies++; LastOperation = op; return AsyncApply?.Invoke(op, token) ?? Task.FromResult(ApplyHandler?.Invoke(op) ?? ReceiptFor(op, "Completed")); }
        public Task<ContainerRecursiveAclReceiptDto> DiscardContainerRecursiveAclAsync(ContainerRecursiveAclOperationDto op, CancellationToken token) { Discards++; LastOperation = op; if (DiscardFailure is not null) throw DiscardFailure; return Task.FromResult(Receipt ?? ReceiptFor(op, "Aborted")); }
        public static ContainerRecursiveAclReceiptDto ReceiptFor(ContainerRecursiveAclOperationDto op, string state) => new(op.Id, op.Plan.Command.Location.ContainerId, op.Plan.Command.Location.InstanceId,
            op.Plan.Fingerprint, op.Plan.TargetProof, state, state == "Completed", false,
            state == "Completed" ? 4U : state == "Partial" ? 1U : 0U, state == "Completed" ? op.Plan.ChangeCount : state == "Partial" ? 1U : 0U,
            0, state == "Aborted" ? 4U : state == "Partial" ? 3U : 0U, state == "Unknown" ? 4U : 0U,
            state != "Aborted", state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('F', 64) : "",
            state is "Completed" or "Partial", state is "Completed" or "Partial" ? new('9', 64) : "", op.Plan.Command.AclType);
    }
    private sealed class Repository(IContainerRecursiveAclProtector protector, Clock clock) : IContainerRecursiveAclRepository
    {
        public ContainerRecursiveAclIntent? Row; public int Enqueues, Deferrals, Confirms; public bool Canceled; public bool Renewal = true; public Action? AfterStart, AfterRead, AfterWrite, AfterRenew;
        public ContainerFileUploadStartResult Start = ContainerFileUploadStartResult.Started;
        public Task<ContainerRecursiveAclIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken token)
        { var value = Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null;AfterRead?.Invoke();return Task.FromResult(value); }
        public Task<ContainerRecursiveAclIntent?> FindAsync(Guid id, CancellationToken token)
        { var value = Row?.Id == id ? Row : null;AfterRead?.Invoke();return Task.FromResult(value); }
        public Task<IReadOnlyList<ContainerRecursiveAclIntent>> UnresolvedAsync(int take, CancellationToken token)
        { IReadOnlyList<ContainerRecursiveAclIntent> value = Row?.State is "Unknown" or "Partial" ? [Row] : [];AfterRead?.Invoke();return Task.FromResult(value); }
        public Task<ContainerRecursiveAclIntent> EnqueueAsync(ContainerRecursiveAclIntent row, CancellationToken token) { Enqueues++; Row = row; AfterWrite?.Invoke(); return Task.FromResult(row); }
        public Task<ContainerRecursiveAclIntent> ConfirmAsync(TerminalActor actor, ContainerRecursiveAclConfirmation claims, long version, CancellationToken token)
        {
            var r = Row!; if (r.ReadyExpiresAtUtc <= clock.Now) throw new InvalidOperationException("CONTAINER_RECURSIVE_ACL_PLAN_EXPIRED");
            Confirms++; r.ExecutionJobId = ContainerRecursiveAclWorkflowValidation.ExecutionId(r.Id); r.ConfirmedAtUtc = clock.Now; r.ExecutionExpiresAtUtc = clock.Now.AddMinutes(15);
            r.ExecutionHash = protector.FingerprintRecursiveAclExecution(ContainerRecursiveAclWorkflowValidation.ExecutionBinding(r)); r.State = "Pending"; r.Version++; AfterWrite?.Invoke(); return Task.FromResult(r);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; AfterWrite?.Invoke(); return Task.CompletedTask; }
        public Task<ContainerRecursiveAclIntent?> ClaimAsync(DateTimeOffset now, CancellationToken token) { if (Row is null || Row.CompletedAtUtc is not null || Row.State is "Unknown" or "Partial") return Task.FromResult<ContainerRecursiveAclIntent?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerRecursiveAclIntent?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerRecursiveAclIntent row, CancellationToken token) { row.State = "Planning"; return Task.FromResult(true); }
        public Task FinishPlanningAsync(ContainerRecursiveAclIntent row, ContainerRecursiveAclPlanDto? plan, string code, CancellationToken token)
        {
            row.ResultCode = code; row.Version++;
            if (plan is null) { row.State = code == "CONTAINER_RECURSIVE_ACL_CANCELED" ? "Canceled" : "Failed"; row.CompletedAtUtc = clock.Now; }
            else { row.PlanJson = ContainerRecursiveAclWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = clock.Now.AddMinutes(5); row.PlanHash = protector.FingerprintRecursiveAclPlan(ContainerRecursiveAclWorkflowValidation.PlanBinding(row)); row.State = "Ready"; }
            return Task.CompletedTask;
        }
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecursiveAclIntent row, DateTimeOffset now, CancellationToken token)
        { if (Start == ContainerFileUploadStartResult.Started) { row.RootStartedAtUtc = now; row.PublishBarrier = true; row.State = "Applying"; AfterStart?.Invoke(); } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerRecursiveAclIntent row, DateTimeOffset now, CancellationToken token) { AfterRenew?.Invoke(); return Task.FromResult(Renewal); }
        public Task<bool> FinishAsync(ContainerRecursiveAclIntent row, string state, string code, ContainerRecursiveAclReceiptDto? receipt, CancellationToken token)
        { row.State = state; row.ResultCode = code; row.HistoryMayExist = state == "Unknown" || receipt?.HistoryMayExist == true || row.HistoryMayExist; row.Version++; Facts(row, receipt); if (state is not ("Unknown" or "Partial")) { row.CompletedAtUtc = clock.Now; row.PublishBarrier = false; } return Task.FromResult(true); }
        public Task DeferAsync(ContainerRecursiveAclIntent row, CancellationToken token) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecursiveAclReceiptDto receipt, CancellationToken token)
        { Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject; Row.AcknowledgementJson = JsonSerializer.Serialize(receipt); Row.Version++; Facts(Row, receipt); return Task.CompletedTask; }
        private void Facts(ContainerRecursiveAclIntent row, ContainerRecursiveAclReceiptDto? receipt)
        {
            if (receipt is null) return; row.ReceiptJson = ContainerRecursiveAclWorkflowValidation.Serialize(receipt); row.RootState = receipt.State;
            row.CompletedCount = (int)receipt.CompletedCount; row.ChangedCount = (int)receipt.ChangedCount; row.AbortedCount = (int)receipt.AbortedCount;
            row.NotStartedCount = (int)receipt.NotStartedCount; row.UnknownCount = (int)receipt.UnknownCount; row.OutcomeDigest = receipt.OutcomeDigest;
            row.BindingCurrent = receipt.BindingCurrent; row.HistoryAvailable = receipt.HistoryAvailable; row.HistoryMayExist |= receipt.HistoryMayExist; row.HistoryVerified = receipt.HistoryVerified;
            row.HistoryDigest = receipt.HistoryDigest.Length == 0 ? null : receipt.HistoryDigest;
            row.ReceiptHash = protector.FingerprintRecursiveAclReceipt(new(ContainerRecursiveAclWorkflowValidation.Reference(row),receipt));
        }
    }
}
