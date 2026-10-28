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
public sealed class ContainerCopyWorkflowTests
{
    [Theory][InlineData(false, true)][InlineData(true, false)]
    public async Task OrdinaryUserAndRevokedLoginCannotQueueOrReachAgent(bool admin, bool login)
    {
        using var f = new Fixture(); f.Identity.Admin = admin; f.Logins.Active = login;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Queue());
        Assert.Equal(0, f.Gateway.Requires); Assert.Null(f.Repository.Row);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task HttpQueueNeverScansOrCopiesAndWorkerPlanningNeedsSeparateConfirmation(bool directory)
    {
        using var f = new Fixture(directory); var queued = await f.Queue();
        Assert.Equal("Queued", queued.State); Assert.Null(queued.Confirmation); Assert.Null(queued.ExecutionJobId);
        Assert.Equal(0, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await f.Dispatcher.TickAsync(default);
        var ready = await f.Get(); Assert.Equal("Ready", ready.State); Assert.NotNull(ready.Confirmation);
        Assert.Equal(directory ? "Directory" : "File", ready.Kind); Assert.Equal(directory ? 3 : 1, ready.EntryCount); Assert.Equal(7, ready.TotalBytes);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, f.Command with { TargetPath = "work/changed" }, f.Key, default));
    }
    [Fact]
    public async Task ReadAndConfirmationStayBoundToExactOriginalLoginContainerAndPlan()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        var other = f.Actor with { LoginBinding = new('F', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(other, f.Command.Source.ContainerId, ready.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(f.Actor, new('b', 64), ready.Id, default));
        var token = f.Protector.SignCopyConfirmation(new(ready.Id, "different-admin", f.Actor.LoginBinding, f.Repository.Row!.PlanHash!, ready.ReadyExpiresAtUtc!.Value));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Source.ContainerId, ready.Id, new(token, true, ready.Version), default));
        Assert.Null(f.Repository.Row.ExecutionJobId); Assert.Equal(0, f.Gateway.Applies);
    }
    [Theory][InlineData(false, 0)][InlineData(true, -1)]
    public async Task CancelingConfirmationOrInvalidVersionCannotCreateExecution(bool confirm, long version)
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Source.ContainerId, ready.Id, new(ready.Confirmation!.PlanHash, confirm, version), default));
        Assert.Null(f.Repository.Row!.ExecutionJobId); Assert.Equal(0, f.Repository.Confirms);
    }
    [Fact]
    public async Task ExpiredReadyReadDoesNotReplanRenewOrOfferConfirmation()
    {
        using var f = new Fixture(); await f.Ready(); var ready = await f.Get(); var original = f.Repository.Row!.ReadyExpiresAtUtc;
        f.Clock.Now = f.Clock.Now.AddMinutes(6); var expired = await f.Get();
        Assert.Equal("Expired", expired.State); Assert.Null(expired.Confirmation); Assert.Equal(original, expired.ReadyExpiresAtUtc);
        Assert.Equal(1, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmAsync(f.Actor, f.Command.Source.ContainerId, ready.Id, new(ready.Confirmation!.PlanHash, true, ready.Version), default));
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
    public async Task ActiveCopyCancellationAndLeaseLossCancelRpcThenOnlyFenceOriginalExecution(bool lostLease)
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
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier); Assert.True(row.PrivateMayExist);
        var admin = f.Actor with { Subject = "current-other-admin", LoginBinding = new('F', 64) };
        var list = await f.Service.UnknownAsync(admin, 50, default); Assert.Equal(row.Id, Assert.Single(list).Id);
        f.Gateway.DiscardFailure = new IOException("synthetic missing fence");
        await Assert.ThrowsAsync<IOException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); f.Gateway.DiscardFailure = null;
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.False(row.PublishBarrier); Assert.True(row.PrivateMayExist);
        Assert.Equal(original, f.Gateway.LastOperation!.Id); Assert.Equal(f.Actor.Subject, f.Gateway.LastOperation.Context.ActorSubject);
        Assert.Equal(admin.Subject, row.AcknowledgedBy); Assert.Null(row.PublishedVersion);
    }
    [Theory][InlineData("Id")][InlineData("IdempotencyKey")][InlineData("ActorSubject")][InlineData("LoginBinding")][InlineData("TraceId")]
    [InlineData("OwnerHash")][InlineData("ContainerId")][InlineData("InstanceId")][InlineData("SourcePath")][InlineData("TargetPath")]
    [InlineData("CommandJson")][InlineData("RequestHash")][InlineData("CreatedAtUtc")][InlineData("PlanningExpiresAtUtc")]
    [InlineData("PlanJson")][InlineData("PlanHash")][InlineData("ReadyExpiresAtUtc")][InlineData("ExecutionJobId")]
    [InlineData("ConfirmedAtUtc")][InlineData("ExecutionExpiresAtUtc")][InlineData("ExecutionHash")]
    public async Task EveryImmutableRequestPlanAndExecutionFieldIsBoundBeforeAnyRootCall(string name)
    {
        using var f = new Fixture(); await f.Create(); var row = f.Repository.Row!;
        var property = typeof(ContainerCopyIntent).GetProperty(name)!; var value = property.GetValue(row)!;
        object changed = value switch { Guid => Guid.NewGuid(), DateTimeOffset t => t.AddMinutes(1), string s => s + "x", _ => throw new NotSupportedException() };
        property.SetValue(row, changed); Assert.False(ContainerCopyWorkflowValidation.HasValidBinding(row, f.Protector));
        var inspects = f.Gateway.Inspects; await f.Dispatcher.TickAsync(default);
        Assert.Equal(inspects, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies); Assert.Equal("Unknown", row.State);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName(); public Clock Clock { get; } = new();
        public Identity Identity { get; } public Logins Logins { get; } = new(); public Gateway Gateway { get; }
        public Repository Repository { get; } public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("copy-test-admin", null, [], new('A', 64), "copy-test-trace", null);
        public string Key { get; } = Guid.NewGuid().ToString("D"); public ContainerCopyCommandDto Command => Gateway.PlanValue.Command;
        public ContainerCopyWorkflowService Service { get; } public ContainerCopyDispatcher Dispatcher { get; }
        public Fixture(bool directory = false)
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock); Gateway = new(directory); Repository = new(Protector, Clock);
            var files = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var auth = new ContainerFileUploadService(files, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Service = new(auth, Repository, Protector, Gateway, Clock); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerCopyIntentDto> Queue() => Service.QueueAsync(Actor, Command, Key, default);
        public Task<ContainerCopyIntentDto> Get() => Service.GetAsync(Actor, Command.Source.ContainerId, Repository.Row!.Id, default);
        public async Task Ready() { await Queue(); await Dispatcher.TickAsync(default); }
        public async Task<ContainerCopyIntentDto> Confirm() { var r = await Get(); return await Service.ConfirmAsync(Actor, Command.Source.ContainerId, r.Id, new(r.Confirmation!.PlanHash, true, r.Version), default); }
        public async Task Create() { await Ready(); await Confirm(); }
        public void Dispose() => File.Delete(_key);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(TimeProvider clock) : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Unavailable { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken token) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic identity unavailable")) : Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Logins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string binding, CancellationToken token) => Unavailable ? Task.FromException<bool>(new IOException("synthetic cache unavailable")) : Task.FromResult(Active);
    }
    private sealed class Gateway(bool directory) : IContainerCopyGateway
    {
        public ContainerCopyPlanDto PlanValue { get; set; } = ContainerCopyGatewayTests.Plan(directory);
        public int Requires, Plans, Inspects, Applies, Discards; public bool Unavailable; public Action? AfterPlan;
        public ContainerCopyReceiptDto? Receipt; public Exception? DiscardFailure; public ContainerCopyOperationDto? LastOperation;
        public Func<ContainerCopyOperationDto, ContainerCopyReceiptDto>? ApplyHandler;
        public Func<ContainerCopyOperationDto, CancellationToken, Task<ContainerCopyReceiptDto>>? AsyncApply;
        public Task RequireContainerCopyWorkflowAsync(CancellationToken token) { Requires++; if (Unavailable) throw new IOException("synthetic unsupported Agent"); return Task.CompletedTask; }
        public Task<ContainerCopyPlanDto> PlanContainerCopyAsync(TerminalActor actor, ContainerCopyCommandDto command, CancellationToken token) { Plans++; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerCopyReceiptDto?> InspectContainerCopyAsync(ContainerCopyOperationDto op, CancellationToken token) { Inspects++; LastOperation = op; return Task.FromResult(Receipt); }
        public Task<ContainerCopyReceiptDto> ApplyContainerCopyAsync(ContainerCopyOperationDto op, CancellationToken token) { Applies++; LastOperation = op; return AsyncApply?.Invoke(op, token) ?? Task.FromResult(ApplyHandler?.Invoke(op) ?? ReceiptFor(op, "Completed")); }
        public Task<ContainerCopyReceiptDto> DiscardContainerCopyAsync(ContainerCopyOperationDto op, CancellationToken token) { Discards++; LastOperation = op; if (DiscardFailure is not null) throw DiscardFailure; return Task.FromResult(Receipt ?? ReceiptFor(op, "Aborted")); }
        public static ContainerCopyReceiptDto ReceiptFor(ContainerCopyOperationDto op, string state) => new(op.Id, op.Plan.Command.Source.ContainerId, op.Plan.Command.Source.InstanceId,
            op.Plan.Kind, op.Plan.Fingerprint, op.Plan.TargetProof, state, state == "Completed" ? new('E', 64) : "", state == "Completed", state == "Unknown", false);
    }
    private sealed class Repository(IContainerCopyProtector protector, Clock clock) : IContainerCopyRepository
    {
        public ContainerCopyIntent? Row; public int Enqueues, Deferrals, Confirms; public bool Canceled; public bool Renewal = true; public Action? AfterStart;
        public ContainerFileUploadStartResult Start = ContainerFileUploadStartResult.Started;
        public Task<ContainerCopyIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null);
        public Task<ContainerCopyIntent?> FindAsync(Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id ? Row : null);
        public Task<IReadOnlyList<ContainerCopyIntent>> UnknownAsync(int take, CancellationToken token) => Task.FromResult<IReadOnlyList<ContainerCopyIntent>>(Row?.State == "Unknown" ? [Row] : []);
        public Task<ContainerCopyIntent> EnqueueAsync(ContainerCopyIntent row, CancellationToken token) { Enqueues++; Row = row; return Task.FromResult(row); }
        public Task<ContainerCopyIntent> ConfirmAsync(TerminalActor actor, ContainerCopyConfirmation claims, long version, CancellationToken token)
        {
            var r = Row!; if (r.ReadyExpiresAtUtc <= clock.Now) throw new InvalidOperationException("CONTAINER_COPY_PLAN_EXPIRED");
            Confirms++; r.ExecutionJobId = ContainerCopyWorkflowValidation.ExecutionId(r.Id); r.ConfirmedAtUtc = clock.Now; r.ExecutionExpiresAtUtc = clock.Now.AddMinutes(15);
            r.ExecutionHash = protector.FingerprintCopyExecution(ContainerCopyWorkflowValidation.ExecutionBinding(r)); r.State = "Pending"; r.Version++; return Task.FromResult(r);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerCopyIntent?> ClaimAsync(DateTimeOffset now, CancellationToken token) { if (Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown") return Task.FromResult<ContainerCopyIntent?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerCopyIntent?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerCopyIntent row, CancellationToken token) { row.State = "Planning"; return Task.FromResult(true); }
        public Task FinishPlanningAsync(ContainerCopyIntent row, ContainerCopyPlanDto? plan, string code, CancellationToken token)
        {
            row.ResultCode = code; row.Version++;
            if (plan is null) { row.State = code == "CONTAINER_COPY_CANCELED" ? "Canceled" : "Failed"; row.CompletedAtUtc = clock.Now; }
            else { row.PlanJson = ContainerCopyWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = clock.Now.AddMinutes(5); row.PlanHash = protector.FingerprintCopyPlan(ContainerCopyWorkflowValidation.PlanBinding(row)); row.State = "Ready"; }
            return Task.CompletedTask;
        }
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerCopyIntent row, DateTimeOffset now, CancellationToken token)
        { if (Start == ContainerFileUploadStartResult.Started) { row.RootStartedAtUtc = now; row.PublishBarrier = true; row.State = "Applying"; AfterStart?.Invoke(); } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerCopyIntent row, DateTimeOffset now, CancellationToken token) => Task.FromResult(Renewal);
        public Task<bool> FinishAsync(ContainerCopyIntent row, string state, string code, ContainerCopyReceiptDto? receipt, CancellationToken token)
        { row.State = state; row.ResultCode = code; row.PrivateMayExist = receipt?.PrivateMayExist ?? row.PrivateMayExist; row.Version++; if (state != "Unknown") { row.CompletedAtUtc = clock.Now; row.PublishBarrier = false; } return Task.FromResult(true); }
        public Task DeferAsync(ContainerCopyIntent row, CancellationToken token) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerCopyReceiptDto receipt, CancellationToken token)
        { Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject; Row.AcknowledgementJson = JsonSerializer.Serialize(receipt); Row.Version++; return Task.CompletedTask; }
    }
}
