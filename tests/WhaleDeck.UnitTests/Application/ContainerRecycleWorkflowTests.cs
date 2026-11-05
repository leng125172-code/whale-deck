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
public sealed partial class ContainerRecycleWorkflowTests
{
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task OrdinaryUserAndRevokedLoginCannotQueueOrReachAgent(bool admin, bool login)
    {
        using var f = new Fixture(); f.Identity.Admin = admin; f.Logins.Active = login;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Queue());
        Assert.Equal(0, f.Gateway.Requires); Assert.Null(f.Repository.Row);
    }
    [Fact]
    public async Task QueueNeverRemovesAndPlanningNeedsSeparateConfirmation()
    {
        using var f = new Fixture(); var queued = await f.Queue();
        Assert.Equal("Queued", queued.State); Assert.Null(queued.Confirmation); Assert.Null(queued.ExecutionJobId);
        Assert.Equal(0, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default);
        var ready = await f.Get(); Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation);
        Assert.Equal(1000U, ready.OriginalUid); Assert.Equal(1001U, ready.OriginalGid);
        Assert.Equal(5100U, ready.ExecutorUid); Assert.Equal(5101U, ready.ExecutorGid); Assert.Equal(7, ready.SizeBytes); Assert.Equal(1, ready.AttributeCount);
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
        Assert.Equal(first, await f.Queue()); Assert.Equal(expiry, f.Repository.Row.PlanningExpiresAtUtc); Assert.Equal(1, f.Repository.Enqueues);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, f.Command with { ExpectedVersion = new('9', 64) }, f.Key, default));
    }
    [Fact]
    public async Task ReadAndConfirmationStayBoundToExactOriginalLoginContainerAndPlan()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        var other = f.Actor with { LoginBinding = new('F', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(other, f.Command.Location.ContainerId, ready.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(f.Actor, new('b', 64), ready.Id, default));
        var token = f.Protector.SignRecycleConfirmation(new(ready.Id, "different-admin", f.Actor.LoginBinding, f.Repository.Row!.PlanHash!, ready.ReadyExpiresAtUtc!.Value));
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
        f.Gateway.ApplyHandler = op => { if (committed) f.Gateway.Receipt = Gateway.ReceiptFor(op, "Trashed"); throw new IOException("synthetic lost reply"); };
        await f.Dispatcher.TickAsync(default); Assert.True(f.Repository.Row!.PublishBarrier); Assert.Equal(1, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Gateway.Applies);
        Assert.Equal(committed ? "Succeeded" : "Failed", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task PostApplyRevocationDoesNotEraseHistoricalCompletion()
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { f.Gateway.Receipt = Gateway.ReceiptFor(op, "Trashed"); f.Logins.Active = false; return f.Gateway.Receipt; };
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Repository.Deferrals); Assert.True(f.Repository.Row!.PublishBarrier);
        await f.Dispatcher.TickAsync(default); Assert.Equal("Succeeded", f.Repository.Row.State); Assert.Equal(1, f.Gateway.Applies);
    }
    [Fact]
    public async Task ChangedCompletedTargetIsSupersededNotFreshSuccess()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Trashed") with { BindingCurrent = false };
        await f.Dispatcher.TickAsync(default); Assert.Equal("Superseded", f.Repository.Row!.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Theory][InlineData("job")][InlineData("proof")][InlineData("state")]
    public async Task InvalidRootResponseRetainsUnknownAndCannotBeReportedAsSuccess(string field)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => { var r = Gateway.ReceiptFor(op, "Trashed"); return field switch {
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
    public async Task ActiveRecycleCancellationAndLeaseLossCancelRpcThenOnlyFenceOriginalExecution(bool lostLease)
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
        var list = await f.Service.UnresolvedAsync(admin, 50, default); Assert.Equal(row.Id, Assert.Single(list).Id);
        f.Gateway.DiscardFailure = new IOException("synthetic missing fence");
        await Assert.ThrowsAsync<IOException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); f.Gateway.DiscardFailure = null;
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.False(row.PublishBarrier); Assert.True(row.HistoryMayExist);
        Assert.Equal(original, f.Gateway.LastOperation!.Id); Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation.Context.ActorSubject);
        Assert.Equal(admin.Subject, row.AcknowledgedBy); Assert.Equal("Unknown", row.RootState); Assert.Null(row.SourceRetained);
    }
    [Theory][InlineData("Id")][InlineData("IdempotencyKey")][InlineData("ActorSubject")][InlineData("LoginBinding")][InlineData("TraceId")]
    [InlineData("OwnerHash")][InlineData("ContainerId")][InlineData("InstanceId")][InlineData("RelativePath")]
    [InlineData("CommandJson")][InlineData("RequestHash")][InlineData("CreatedAtUtc")][InlineData("PlanningExpiresAtUtc")]
    [InlineData("PlanJson")][InlineData("PlanHash")][InlineData("ReadyExpiresAtUtc")][InlineData("ExecutionJobId")]
    [InlineData("ConfirmedAtUtc")][InlineData("ExecutionExpiresAtUtc")][InlineData("ExecutionHash")]
    public async Task EveryImmutableRequestPlanAndExecutionFieldIsBoundBeforeAnyRootCall(string name)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        var property = typeof(ContainerRecycleIntent).GetProperty(name)!; var value = property.GetValue(row)!;
        object changed = value switch { Guid => Guid.NewGuid(), DateTimeOffset t => t.AddMinutes(1), string s => s + "x", _ => throw new NotSupportedException() };
        property.SetValue(row, changed); Assert.False(ContainerRecycleWorkflowValidation.HasValidBinding(row, f.Protector));
        var inspects = f.Gateway.Inspects; await f.Dispatcher.TickAsync(default);
        Assert.Equal(inspects, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies); Assert.Equal("Unknown", row.State);
    }

    [Theory]
    [InlineData("CONTAINER_RECYCLE_SOURCE_CHANGED")][InlineData("CONTAINER_RECYCLE_EXTENDED_METADATA_REQUIRED")]
    [InlineData("CONTAINER_RECYCLE_SOURCE_UNSUPPORTED")][InlineData("CONTAINER_RECYCLE_TARGET_CHANGED")]
    public async Task ExplicitPreflightRefusalFailsPlanningPromptlyWithoutRootMutation(string code)
    {
        using var f = new Fixture(); await f.Queue(); f.Gateway.PlanFailure = new ContainerRecyclePreflightRejectedException(code);
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
    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName(); public Clock Clock { get; } = new();
        public Identity Identity { get; } public Logins Logins { get; } = new(); public Gateway Gateway { get; }
        public Repository Repository { get; } public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("copy-test-admin", null, [], new('A', 64), "copy-test-trace", null);
        public string Key { get; } = Guid.NewGuid().ToString("D"); public ContainerRecycleCommandDto Command => Gateway.PlanValue.Command;
        public ContainerRecycleWorkflowService Service { get; } public ContainerRecycleDispatcher Dispatcher { get; }
        public Fixture()
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock); Gateway = new(); Repository = new(Protector, Clock);
            var files = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var auth = new ContainerFileUploadService(files, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Service = new(auth, Repository, Protector, Gateway, Clock, Identity); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerRecycleIntentDto> Queue() => Service.QueueAsync(Actor, Command, Key, default);
        public Task<ContainerRecycleIntentDto> Get() => Service.GetAsync(Actor, Command.Location.ContainerId, Repository.Row!.Id, default);
        public async Task Ready() { await Queue(); await Dispatcher.TickAsync(default); }
        public async Task<ContainerRecycleIntentDto> Confirm() { var r = await Get(); return await Service.ConfirmAsync(Actor, Command.Location.ContainerId, r.Id, new(r.Confirmation!.PlanHash, true, r.Version), default); }
        public async Task Create() { await Ready(); await Confirm(); }
        public void Dispose() => File.Delete(_key);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(TimeProvider clock) : IIdentityDirectory, IPrivilegedIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Unavailable { get; set; }
        public bool? LiveAdmin { get; set; } public bool LiveUnavailable { get; set; }
        public Action? OnLiveRead;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken token) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic identity unavailable")) : Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken token)
        {
            OnLiveRead?.Invoke();
            return LiveUnavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic live identity unavailable")) :
                LiveAdmin is { } admin ? Task.FromResult(new CurrentUserDto(subject, name, [], admin, clock.GetUtcNow().AddHours(2))) : ResolveCurrentAsync(subject, name, [], token);
        }
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Logins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string binding, CancellationToken token) => Unavailable ? Task.FromException<bool>(new IOException("synthetic cache unavailable")) : Task.FromResult(Active);
    }
    private sealed class Gateway : IContainerRecycleGateway
    {
        public ContainerRecyclePlanDto PlanValue { get; set; } = ContainerRecycleWorkflowFixture.Plan();
        public Exception? PlanFailure;
        public int Requires, Plans, Inspects, Applies, Discards; public bool Unavailable; public Action? AfterPlan;
        public ContainerRecycleReceiptDto? Receipt; public Exception? DiscardFailure; public ContainerRecycleOperationDto? LastOperation;
        public Func<ContainerRecycleOperationDto, ContainerRecycleReceiptDto>? ApplyHandler;
        public Func<ContainerRecycleOperationDto, CancellationToken, Task<ContainerRecycleReceiptDto>>? AsyncApply;
        public Task RequireContainerRecycleAsync(CancellationToken token) { Requires++; if (Unavailable) throw new IOException("synthetic unsupported Agent"); return Task.CompletedTask; }
        public Task<ContainerRecyclePlanDto> PlanContainerRecycleAsync(TerminalActor actor, ContainerRecycleCommandDto command, CancellationToken token) { Plans++; if (PlanFailure is not null) throw PlanFailure; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerRecycleReceiptDto?> InspectContainerRecycleAsync(ContainerRecycleOperationDto op, CancellationToken token) { Inspects++; LastOperation = op; return Task.FromResult(Receipt); }
        public Task<ContainerRecycleReceiptDto> ApplyContainerRecycleAsync(ContainerRecycleOperationDto op, CancellationToken token) { Applies++; LastOperation = op; return AsyncApply?.Invoke(op, token) ?? Task.FromResult(Receipt = ApplyHandler?.Invoke(op) ?? ReceiptFor(op, "Trashed")); }
        public Task<ContainerRecycleReceiptDto> DiscardContainerRecycleAsync(ContainerRecycleOperationDto op, CancellationToken token) { Discards++; LastOperation = op; if (DiscardFailure is not null) throw DiscardFailure; return Task.FromResult(Receipt ?? ReceiptFor(op, "Aborted")); }
        public Task<ContainerRecycleReceiptDto> RestoreContainerRecycleAsync(ContainerRecycleOperationDto op, CancellationToken token) => throw new InvalidOperationException("Restore must have its own business intent.");
        public static ContainerRecycleReceiptDto ReceiptFor(ContainerRecycleOperationDto op, string state) => new(op.Id,
            op.Plan.Command.Location.ContainerId, op.Plan.Command.Location.InstanceId, op.Plan.Fingerprint, op.Plan.TargetProof,
            state, state is "Trashed" or "Restored", false, state != "Aborted", state is "Trashed" or "Restored",
            state is "Trashed" or "Restored" ? new('F',64) : "", state is "Trashed" or "Restored",
            state == "Unknown" ? null : state == "Trashed", state == "Unknown" ? null : state == "Restored");
    }

    private sealed class Repository(IContainerRecycleProtector protector, Clock clock) : IContainerRecycleRepository
    {
        public ContainerRecycleIntent? Row; public int Enqueues, Deferrals, Confirms; public bool Canceled; public bool Renewal = true; public Action? AfterStart; public Action? AfterFind;
        public ContainerFileUploadStartResult Start = ContainerFileUploadStartResult.Started;
        public Task<ContainerRecycleIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null);
        public Task<ContainerRecycleIntent?> FindAsync(Guid id, CancellationToken token) { AfterFind?.Invoke(); return Task.FromResult(Row?.Id == id ? Row : null); }
        public Task<IReadOnlyList<ContainerRecycleIntent>> UnresolvedAsync(int take, CancellationToken token) => Task.FromResult<IReadOnlyList<ContainerRecycleIntent>>(Row?.State == "Unknown" ? [Row] : []);
        public Task<ContainerRecycleIntent> EnqueueAsync(ContainerRecycleIntent row, CancellationToken token) { Enqueues++; Row = row; return Task.FromResult(row); }
        public Task<ContainerRecycleIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleConfirmation claims, long version, CancellationToken token)
        {
            var r = Row!; if (r.ReadyExpiresAtUtc <= clock.Now) throw new InvalidOperationException("CONTAINER_RECYCLE_PLAN_EXPIRED");
            Confirms++; r.ExecutionJobId = ContainerRecycleWorkflowValidation.ExecutionId(r.Id); r.ConfirmedAtUtc = clock.Now; r.ExecutionExpiresAtUtc = clock.Now.AddMinutes(15);
            r.ExecutionHash = protector.FingerprintRecycleExecution(ContainerRecycleWorkflowValidation.ExecutionBinding(r)); r.State = "Pending"; r.Version++; return Task.FromResult(r);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerRecycleIntent?> ClaimAsync(DateTimeOffset now, CancellationToken token) { if (Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown") return Task.FromResult<ContainerRecycleIntent?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerRecycleIntent?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerRecycleIntent row, CancellationToken token) { row.State = "Planning"; return Task.FromResult(true); }
        public Task FinishPlanningAsync(ContainerRecycleIntent row, ContainerRecyclePlanDto? plan, string code, CancellationToken token)
        {
            row.ResultCode = code; row.Version++;
            if (plan is null) { row.State = code == "CONTAINER_RECYCLE_CANCELED" ? "Canceled" : "Failed"; row.CompletedAtUtc = clock.Now; }
            else { row.PlanJson = ContainerRecycleWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = clock.Now.AddMinutes(5); row.PlanHash = protector.FingerprintRecyclePlan(ContainerRecycleWorkflowValidation.PlanBinding(row)); row.State = "Ready"; }
            return Task.CompletedTask;
        }
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleIntent row, DateTimeOffset now, CancellationToken token)
        { if (Start == ContainerFileUploadStartResult.Started) { row.RootStartedAtUtc = now; row.PublishBarrier = true; row.State = "Applying"; AfterStart?.Invoke(); } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerRecycleIntent row, DateTimeOffset now, CancellationToken token) => Task.FromResult(Renewal);
        private void SaveReceipt(ContainerRecycleIntent row, ContainerRecycleReceiptDto? receipt)
        {
            if (receipt is null) return;
            ContainerRecycleWorkflowValidation.RequireCompatibleReceipt(row, receipt);
            row.ReceiptJson = ContainerRecycleWorkflowValidation.Serialize(receipt);
            row.ReceiptHash = protector.FingerprintRecycleReceipt(new(ContainerRecycleWorkflowValidation.Reference(row), receipt));
            row.RootState = receipt.State; row.SourceRetained = receipt.SourceRetained; row.SourceRestored = receipt.SourceRestored;
            row.BindingCurrent = receipt.BindingCurrent; row.HistoryAvailable = receipt.HistoryAvailable;
            row.HistoryVerified = receipt.HistoryVerified; row.HistoryDigest = receipt.HistoryDigest.Length == 0 ? null : receipt.HistoryDigest;
        }
        public Task<bool> FinishAsync(ContainerRecycleIntent row, string state, string code, ContainerRecycleReceiptDto? receipt, CancellationToken token)
        {
            SaveReceipt(row, receipt); row.State = state; row.ResultCode = code;
            row.HistoryMayExist = state == "Unknown" || receipt?.HistoryMayExist == true || row.HistoryMayExist; row.Version++;
            if (state != "Unknown") { row.CompletedAtUtc = clock.Now; row.PublishBarrier = false; }
            return Task.FromResult(true);
        }
        public Task DeferAsync(ContainerRecycleIntent row, CancellationToken token) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleReceiptDto receipt, CancellationToken token)
        {
            SaveReceipt(Row!, receipt); Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject;
            Row.AcknowledgementJson = JsonSerializer.Serialize(receipt); Row.Version++; return Task.CompletedTask;
        }
    }
}
