using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreeOverwriteRestoringLeaseTests
{
    private static FileTreeOverwriteRestoringOperation Row(FileTreeOverwriteRestoringBindingTests fixture, bool planning)
    {
        var row = fixture.Operation(!planning);
        if (planning) { row.PlanJson = null; row.ArchiveBytes = null; row.ArchiveSha256 = null; row.PlanHash = null; row.ReadyExpiresAtUtc = null; row.State = "Queued"; }
        return row;
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task LongPlanningAndOverwriteRestoringKeepTheSameLeaseAliveUntilTheSameCallReturns(bool planning)
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row);
        using var clock = new FileUploadLeaseTests.StepClock();
        repository.OnRenew = () => { if (repository.Renewals == (planning ? 3 : 5)) gateway.Complete(); return true; };
        var tick = new FileTreeOverwriteRestoringDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(planning ? "Ready" : "Succeeded", repository.Finished); Assert.Equal(0, repository.Deferrals);
        Assert.Equal(planning ? 1 : 0, gateway.Plans); Assert.Equal(planning ? 0 : 1, gateway.Applies); Assert.False(gateway.Canceled);
    }
    [Theory]
    [InlineData(true, "cancel")][InlineData(false, "cancel")][InlineData(true, "role")][InlineData(false, "role")]
    [InlineData(true, "lease")][InlineData(false, "lease")][InlineData(true, "identity-error")][InlineData(false, "identity-error")]
    [InlineData(true, "expired")][InlineData(false, "expired")][InlineData(true, "subject")][InlineData(false, "snapshot")]
    public async Task InFlightFailureCancelsOnlyItsOwnCallAndDoesNotInventAResult(bool planning, string failure)
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(row.ActorSubject);
        using var clock = new FileUploadLeaseTests.StepClock();
        gateway.OnStart = () => {
            switch (failure)
            {
                case "cancel": repository.Canceled = true; break;
                case "role": identity.Admin = false; break;
                case "lease": repository.OnRenew = () => false; break;
                case "identity-error": identity.Fail = true; break;
                case "expired": clock.Now = (planning ? row.PlanningExpiresAtUtc : row.ExecutionExpiresAtUtc!.Value).AddSeconds(1); break;
                case "subject": identity.Subject = "other"; break;
                case "snapshot": identity.Expiry = clock.Now.AddSeconds(-1); break;
            }
        };
        var tick = new FileTreeOverwriteRestoringDispatcher(repository, gateway, identity, fixture.Protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task RootStartedWithoutAReceiptIsFencedNeverBlindlyReapplied()
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = Row(fixture, false); row.RootStartedAtUtc = DateTimeOffset.UtcNow;
        var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await new FileTreeOverwriteRestoringDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, TimeProvider.System).TickAsync(default));
        Assert.Equal(0, gateway.Applies); Assert.Equal(1, gateway.Discards); Assert.Equal("Failed", repository.Finished);
    }
    [Fact]
    public async Task WorkerShutdownDoesNotWriteAFakeTerminalState()
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = Row(fixture, false); var repository = new Repository(row); var gateway = new Gateway(row);
        using var cancellation = new CancellationTokenSource(); gateway.OnStart = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTreeOverwriteRestoringDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, TimeProvider.System).TickAsync(cancellation.Token));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    [Theory]
    [InlineData("cancel")][InlineData("role")]
    public async Task CompletedRootWriteWhoseFinalGuardFailsIsOnlyInspectedOnNextTick(string reason)
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = Row(fixture, false);
        var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(row.ActorSubject);
        gateway.OnStart = () => { if (reason == "cancel") repository.Canceled = true; else identity.Admin = false; gateway.Complete(); };
        var dispatcher = new FileTreeOverwriteRestoringDispatcher(repository, gateway, identity, fixture.Protector, TimeProvider.System);
        Assert.True(await dispatcher.TickAsync(default)); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
        Assert.NotNull(row.RootStartedAtUtc); Assert.Equal(1, gateway.Applies);
        gateway.Observed = gateway.Result("Completed");
        Assert.True(await dispatcher.TickAsync(default)); Assert.Equal("Succeeded", repository.Finished);
        Assert.Equal(1, gateway.Applies); Assert.Equal(0, gateway.Discards);
    }
    [Theory]
    [InlineData("role")][InlineData("subject")][InlineData("expired")]
    public async Task ServiceRechecksCurrentIdentityBeforeReadingOwnedMaterial(string failure)
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = fixture.Operation(false);
        var repository = new Repository(row); var identity = new Identity(row.ActorSubject);
        if (failure == "role") identity.Admin = false;
        if (failure == "subject") identity.Subject = "other";
        if (failure == "expired") identity.Expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
        var service = Service(fixture, repository, new Gateway(row), identity, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Actor(row), row.Id, default));
        Assert.Equal(0, repository.OwnedReads);
    }
    [Theory]
    [InlineData("actor")][InlineData("login")][InlineData("id")]
    public async Task ConfirmationCannotBorrowAnotherActorLoginOrIntent(string failure)
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = fixture.Operation(false);
        var repository = new Repository(row); var identity = new Identity(row.ActorSubject);
        var claims = new FileTreeOverwriteRestoringConfirmation(failure == "id" ? Guid.NewGuid() : row.Id,
            failure == "actor" ? "other" : row.ActorSubject, failure == "login" ? new('0', 64) : row.LoginBinding,
            FileTreeOverwriteRestoringBindingTests.Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        var service = Service(fixture, repository, new Gateway(row), identity, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync(Actor(row), row.Id,
            new(fixture.Protector.SignConfirmation(claims), true, row.Version), default));
        Assert.Equal(0, repository.Confirmations);
    }
    [Fact]
    public async Task ExpiredPlanStillReadsButDoesNotIssueAnotherConfirmation()
    {
        using var fixture = new FileTreeOverwriteRestoringBindingTests(); var row = fixture.Operation(false);
        var repository = new Repository(row); using var clock = new FileUploadLeaseTests.StepClock { Now = row.ReadyExpiresAtUtc!.Value.AddSeconds(1) };
        var dto = await Service(fixture, repository, new Gateway(row), new Identity(row.ActorSubject), clock).GetAsync(Actor(row), row.Id, default);
        Assert.Equal("Expired", dto.State); Assert.Null(dto.Confirmation);
        Assert.Equal(FileTreeOverwriteRestoringBindingTests.Plan().TargetEntryCount, dto.TargetEntryCount);
        Assert.Equal(FileTreeOverwriteRestoringBindingTests.Plan().Command.TargetExpectedVersion, dto.Command.TargetExpectedVersion);
    }
    private static TerminalActor Actor(FileTreeOverwriteRestoringOperation row) => new(row.ActorSubject, null, [], row.LoginBinding, "unit-service", null);
    private static FileTreeOverwriteRestoringService Service(FileTreeOverwriteRestoringBindingTests fixture, Repository repository, Gateway gateway, Identity identity, TimeProvider clock) =>
        new(new FileManagementService(null!, null!, fixture.Protector, identity, clock), repository, fixture.Protector, gateway, clock);
    private sealed class Identity(string subject) : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Fail { get; set; } public string Subject { get; set; } = subject;
        public DateTimeOffset Expiry { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("test identity unavailable")) : Task.FromResult(new CurrentUserDto(Subject, null, groups, Admin, Expiry));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileTreeOverwriteRestoringOperation row) : IFileTreeOverwriteRestoringGateway
    {
        private readonly TaskCompletionSource<FileTreeOverwriteRestoringPlan> _plan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FileTreeOverwriteRestoringReceipt> _copy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Plans { get; private set; } public int Applies { get; private set; } public int Discards { get; private set; } public bool Canceled { get; private set; }
        public Action? OnStart { get; set; }
        public void Complete() { _plan.TrySetResult(FileTreeOverwriteRestoringBindingTests.Plan()); if (row.ExecutionJobId is not null) _copy.TrySetResult(Result("Completed")); }
        public FileTreeOverwriteRestoringReceipt Result(string state) => new(row.ExecutionJobId!.Value, state, FileTreeOverwriteRestoringBindingTests.Plan().Fingerprint, "tar.gz",
            FileTreeOverwriteRestoringBindingTests.Plan().EntryCount, FileTreeOverwriteRestoringBindingTests.Plan().TotalBytes, FileTreeOverwriteRestoringBindingTests.Plan().ArchiveBytes, FileTreeOverwriteRestoringBindingTests.Plan().ArchiveSha256, state == "Completed" ? new('F', 64) : "", false,
            "DirectoryOverwriteRestore", FileTreeOverwriteRestoringBindingTests.Plan().Command.CaptureId,
            state == "Completed" ? row.ExecutionJobId : null, state == "Completed", state == "Completed", state == "Completed", state == "Completed",
            state == "Completed" ? 4096 : 0, state == "Completed" ? new('A', 64) : "");
        public async Task<FileTreeOverwriteRestoringPlan> PlanTreeOverwriteRestoringAsync(TerminalActor actor, FileTreeOverwriteRestoringCommand command, CancellationToken cancellationToken)
        { Plans++; OnStart?.Invoke(); try { return await _plan.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public async Task<FileTreeOverwriteRestoringReceipt> ApplyTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken)
        { Assert.Same(row, operation); Applies++; OnStart?.Invoke(); try { return await _copy.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public FileTreeOverwriteRestoringReceipt? Observed { get; set; }
        public Task<FileTreeOverwriteRestoringReceipt?> InspectTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken) => Task.FromResult(Observed);
        public Task<FileTreeOverwriteRestoringReceipt> DiscardTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken) { Discards++; return Task.FromResult(Result("Aborted")); }
    }
    private sealed class Repository(FileTreeOverwriteRestoringOperation row) : IFileTreeOverwriteRestoringRepository
    {
        public int Renewals { get; private set; } public int Deferrals { get; private set; } public string? Finished { get; private set; }
        public int OwnedReads { get; private set; } public int Confirmations { get; private set; }
        public bool Canceled { get; set; } public Func<bool> OnRenew { get; set; } = () => true;
        public Task<FileTreeOverwriteRestoringOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileTreeOverwriteRestoringOperation?>(row);
        public Task<bool> CancellationRequestedAsync(FileTreeOverwriteRestoringOperation row, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> FinishPlanningAsync(FileTreeOverwriteRestoringOperation claimed, FileTreeOverwriteRestoringPlan? plan, string code, CancellationToken cancellationToken) { Finished = plan is null ? code : "Ready"; return Task.FromResult(true); }
        public Task<FileUploadStartResult> StartedAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken)
        { row.RootStartedAtUtc = DateTimeOffset.UtcNow; return Task.FromResult(FileUploadStartResult.Started); }
        public Task<bool> RenewAsync(FileTreeOverwriteRestoringOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> FinishAsync(FileTreeOverwriteRestoringOperation claimed, string state, string code, FileTreeOverwriteRestoringReceipt? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileTreeOverwriteRestoringOperation claimed, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task<FileTreeOverwriteRestoringOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
        { OwnedReads++; return Task.FromResult<FileTreeOverwriteRestoringOperation?>(id == row.Id && actor.Subject == row.ActorSubject && actor.LoginBinding == row.LoginBinding ? row : null); }
        public Task<FileTreeOverwriteRestoringOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreeOverwriteRestoringCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreeOverwriteRestoringOperation> ConfirmAsync(TerminalActor actor, FileTreeOverwriteRestoringConfirmation claims, long version, CancellationToken cancellationToken)
        { Confirmations++; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeOverwriteRestoringReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
