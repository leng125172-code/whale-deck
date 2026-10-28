using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Business state machine boundaries. Persistence/concurrency are proved separately with real PostgreSQL.</summary>
public sealed class ContainerDirectoryWorkflowTests
{
    [Theory]
    [InlineData(false, true)][InlineData(true, false)]
    public async Task OrdinaryUserAndRevokedLoginCannotPlanOrSignWrites(bool admin, bool login)
    {
        using var f = new Fixture(); f.Identity.Admin = admin; f.Logins.Active = login;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.PlanAsync(f.Actor, f.Command, default));
        Assert.Equal(0, f.Gateway.Plans); Assert.Null(f.Repository.Row);
    }
    [Fact]
    public async Task OriginalLoginContainerAndExactKernelOwnerPlanAreImmutable()
    {
        using var f = new Fixture(); var plan = await f.Plan(); var command = new SubmitContainerDirectoryCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SubmitAsync(f.Actor with { LoginBinding = new('F', 64) }, f.Command.Location.ContainerId, command, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SubmitAsync(f.Actor, new('b', 64), command, default));
        f.Gateway.PlanValue = f.Gateway.PlanValue with { OwnerUid = 1001 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.Actor, f.Command.Location.ContainerId, command, default));
        Assert.Null(f.Repository.Row);
    }
    [Fact]
    public async Task ExpiryDuringRemoteRevalidationCannotRegisterAndCancelingConfirmationSignsNothingNew()
    {
        using var f = new Fixture(); var plan = await f.Plan();
        var command = new SubmitContainerDirectoryCommand(plan.PlanHash, false, Guid.NewGuid().ToString("D"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SubmitAsync(f.Actor, f.Command.Location.ContainerId, command, default));
        f.Gateway.AfterPlan = () => f.Clock.Now = f.Clock.Now.AddMinutes(6);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.Actor, f.Command.Location.ContainerId, command with { Confirmed = true }, default));
        Assert.Null(f.Repository.Row);
    }
    [Fact]
    public async Task RetryingSameRegistrationAfterExpiryKeepsOneIntentAndOriginalLifetime()
    {
        using var f = new Fixture(); var plan = await f.Plan(); var command = new SubmitContainerDirectoryCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
        var first = await f.Service.SubmitAsync(f.Actor, f.Command.Location.ContainerId, command, default);
        f.Clock.Now = f.Clock.Now.AddMinutes(6);
        var again = await f.Service.SubmitAsync(f.Actor, f.Command.Location.ContainerId, command, default);
        Assert.Equal(first, again); Assert.Equal(1, f.Repository.Enqueues); Assert.Equal(2, f.Gateway.Plans);
        Assert.True(ContainerDirectoryWorkflowService.HasValidBinding(f.Repository.Row!, f.Protector));
        Assert.Equal(f.Clock.Now.AddMinutes(9), first.ExpiresAtUtc);
    }
    [Theory]
    [InlineData("logout")][InlineData("demotion")][InlineData("cancel")][InlineData("expiry")]
    public async Task NoLongerAuthorizedOrExpiredIntentOnlyClosesAfterPositiveRootFence(string cause)
    {
        using var f = new Fixture(); await f.Create();
        if (cause == "logout") f.Logins.Active = false;
        if (cause == "demotion") f.Identity.Admin = false;
        if (cause == "cancel") f.Repository.Canceled = true;
        if (cause == "expiry") f.Clock.Now = f.Clock.Now.AddMinutes(16);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(1, f.Gateway.Discards);
        Assert.Equal(cause == "expiry" ? "Failed" : "Canceled", f.Repository.Row!.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task CacheOrDirectoryFailureFailsClosedWithoutInventingCancellation(bool cache)
    {
        using var f = new Fixture(); await f.Create(); f.Logins.Unavailable = cache; f.Identity.Unavailable = !cache;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards); Assert.Equal(1, f.Repository.Deferrals); Assert.Equal("Pending", f.Repository.Row!.State);
    }
    [Fact]
    public async Task SharedProtectionBusyOrCapabilityDowngradeDoesNotCallApply()
    {
        using var f = new Fixture(); await f.Create(); f.Repository.Start = ContainerFileUploadStartResult.Busy;
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Gateway.Applies); Assert.Null(f.Repository.Row!.RootStartedAtUtc);
        f.Gateway.WorkflowUnavailable = true;
        await f.Dispatcher.TickAsync(default); Assert.Equal(0, f.Gateway.Applies); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task AuthorityIsRecheckedAfterAcquiringPublicationProtection()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.AfterRequire = () => f.Identity.Admin = false;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0, f.Gateway.Applies); Assert.True(f.Repository.Row!.PublishBarrier); Assert.NotNull(f.Repository.Row.RootStartedAtUtc);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal("Canceled", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task LostApplyReplyIsObservedOrFencedAndNeverPhysicallyRetried(bool rootCommitted)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = operation => { if (rootCommitted) f.Gateway.Receipt = FakeGateway.ReceiptFor(operation, "Completed"); throw new IOException("synthetic lost response"); };
        await f.Dispatcher.TickAsync(default); Assert.True(f.Repository.Row!.PublishBarrier); Assert.Equal(1, f.Gateway.Applies);
        f.Repository.Canceled = rootCommitted;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal(rootCommitted ? "Succeeded" : "Failed", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier);
    }
    [Fact]
    public async Task UnknownRetainsProtectionAndAnotherCurrentAdministratorCanAcknowledgeOnlyAfterRootFence()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = operation => FakeGateway.ReceiptFor(operation, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier);
        var admin = f.Actor with { Subject = "another-current-admin", LoginBinding = new('F', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(admin, row.ContainerId, row.Id, default));
        var list = await f.Service.UnknownAsync(admin, 50, default); Assert.Equal(row.Id, Assert.Single(list).Id);
        f.Gateway.DiscardFailure = new IOException("cannot prove fence");
        await Assert.ThrowsAsync<IOException>(() => f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default));
        Assert.True(row.PublishBarrier); Assert.Equal("Unknown", row.State);
        f.Gateway.DiscardFailure = null;
        await f.Service.AcknowledgeAsync(admin, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.False(row.PublishBarrier); Assert.Equal(admin.Subject, row.AcknowledgedBy);
        Assert.NotNull(row.AcknowledgementJson); Assert.Equal(1, f.Gateway.Applies);
    }
    [Fact]
    public async Task TamperedPermanentBindingNeverReachesAgentAndCannotBeAcknowledged()
    {
        using var f = new Fixture(); await f.Create();
        var property = typeof(ContainerDirectoryOperation).GetProperty(nameof(ContainerDirectoryOperation.RequestHash))!;
        property.SetValue(f.Repository.Row, new string('F', 64));
        await f.Dispatcher.TickAsync(default); Assert.Equal("Unknown", f.Repository.Row!.State); Assert.Equal(0, f.Gateway.Inspects); Assert.Equal(0, f.Gateway.Applies);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AcknowledgeAsync(f.Actor, f.Repository.Row.Id, new(f.Repository.Row.Version, true), default));
        Assert.Equal(0, f.Gateway.Discards);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName();
        public Clock Clock { get; } = new(); public Identity Identity { get; } public Logins Logins { get; } = new();
        public FakeGateway Gateway { get; } = new(); public FakeRepository Repository { get; } = new(); public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("directory-test-admin", null, [], new('A', 64), "directory-test-trace", null);
        public ContainerDirectoryWorkflowService Service { get; } public ContainerDirectoryDispatcher Dispatcher { get; }
        public ContainerDirectoryCommandDto Command => Gateway.PlanValue.Command;
        public Fixture()
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock);
            var fileAuthorization = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var authorization = new ContainerFileUploadService(fileAuthorization, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Service = new(authorization, Gateway, Repository, Protector, Clock); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerDirectoryPlanResponse> Plan() => Service.PlanAsync(Actor, Command, default);
        public async Task<ContainerDirectoryIntentDto> Create() => await Service.SubmitAsync(Actor, Command.Location.ContainerId, new((await Plan()).PlanHash, true, Guid.NewGuid().ToString("D")), default);
        public void Dispose() => File.Delete(_key);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(TimeProvider clock) : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Unavailable { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic unavailable identity")) : Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Logins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken) => Unavailable ? Task.FromException<bool>(new IOException("synthetic unavailable cache")) : Task.FromResult(Active);
    }
    private sealed class FakeGateway : IContainerDirectoryGateway
    {
        public int Plans { get; private set; } public int Applies { get; private set; } public int Discards { get; private set; } public int Inspects { get; private set; }
        public bool WorkflowUnavailable { get; set; } public Action? AfterPlan { get; set; } public Action? AfterRequire { get; set; }
        public ContainerDirectoryReceiptDto? Receipt { get; set; } public Exception? DiscardFailure { get; set; }
        public Func<ContainerDirectoryOperationDto, ContainerDirectoryReceiptDto>? ApplyHandler { get; set; }
        public ContainerDirectoryPlanDto PlanValue { get; set; } = new(1, new(new(new('a', 64), "777:2026-10-28T01:02:03.123456789Z", "work/新目录"), "0750"), new('B', 64), new(101, 202), new('C', 64), 1000, 1000, new('D', 64));
        public Task RequireContainerDirectoryWorkflowAsync(CancellationToken cancellationToken) { AfterRequire?.Invoke(); return WorkflowUnavailable ? Task.FromException(new IOException("synthetic missing capability")) : Task.CompletedTask; }
        public Task<ContainerDirectoryPlanDto> PlanContainerDirectoryAsync(TerminalActor actor, ContainerDirectoryCommandDto command, CancellationToken cancellationToken) { Plans++; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerDirectoryReceiptDto> ApplyContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken) { Applies++; Receipt = ApplyHandler?.Invoke(operation) ?? ReceiptFor(operation, "Completed"); return Task.FromResult(Receipt); }
        public Task<ContainerDirectoryReceiptDto?> InspectContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken) { Inspects++; return Task.FromResult(Receipt); }
        public Task<ContainerDirectoryReceiptDto> DiscardContainerDirectoryAsync(ContainerDirectoryOperationDto operation, CancellationToken cancellationToken)
        { Discards++; if (DiscardFailure is not null) return Task.FromException<ContainerDirectoryReceiptDto>(DiscardFailure); Receipt ??= ReceiptFor(operation, "Aborted"); return Task.FromResult(Receipt); }
        public static ContainerDirectoryReceiptDto ReceiptFor(ContainerDirectoryOperationDto operation, string state) => new(operation.Id, operation.Plan.Command.Location.ContainerId,
            operation.Plan.Command.Location.InstanceId, operation.Plan.Fingerprint, operation.Plan.TargetProof, state, state == "Completed" ? new('E', 64) : "", state == "Completed", false);
    }
    private sealed class FakeRepository : IContainerDirectoryRepository
    {
        public ContainerDirectoryOperation? Row { get; set; } public int Enqueues { get; private set; } public int Deferrals { get; private set; } public bool Canceled { get; set; }
        public ContainerFileUploadStartResult Start { get; set; } = ContainerFileUploadStartResult.Started;
        public Task<ContainerDirectoryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult(Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null);
        public Task<ContainerDirectoryOperation?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Row?.Id == id ? Row : null);
        public Task<IReadOnlyList<ContainerDirectoryOperation>> UnknownAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDirectoryOperation>>(Row?.State == "Unknown" ? [Row] : []);
        public Task<ContainerDirectoryOperation> EnqueueAsync(ContainerDirectoryOperation row, DateTimeOffset planExpiry, CancellationToken cancellationToken) { Enqueues++; Row = row; return Task.FromResult(row); }
        public Task<ContainerDirectoryOperation?> ResolveAsync(TerminalActor actor, Guid id, DateTimeOffset planExpiry, CancellationToken cancellationToken) => OwnedAsync(actor, id, cancellationToken);
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerDirectoryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) { if (Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown") return Task.FromResult<ContainerDirectoryOperation?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerDirectoryOperation?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken)
        { if (Start == ContainerFileUploadStartResult.Started) { row.State = "Applying"; row.RootStartedAtUtc = now; row.PublishBarrier = true; } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerDirectoryOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> FinishAsync(ContainerDirectoryOperation row, string state, string code, ContainerDirectoryReceiptDto? receipt, CancellationToken cancellationToken)
        { row.State = state; row.ResultCode = code; row.Version++; if (state != "Unknown") { row.CompletedAtUtc = DateTimeOffset.UtcNow; row.PublishBarrier = false; } return Task.FromResult(true); }
        public Task DeferAsync(ContainerDirectoryOperation row, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerDirectoryReceiptDto receipt, CancellationToken cancellationToken)
        { Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject; Row.AcknowledgementJson = System.Text.Json.JsonSerializer.Serialize(receipt); Row.Version++; return Task.CompletedTask; }
    }
}
