using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreeRecyclingLeaseTests
{
    private static FileTreeRecyclingOperation Row(FileTreeRecyclingBindingTests fixture, bool planning)
    {
        var row = fixture.Operation(!planning);
        if (planning) { row.PlanJson = null; row.PlanHash = null; row.ReadyExpiresAtUtc = null; row.State = "Queued"; }
        return row;
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task LongPlanningAndRecyclingKeepTheSameLeaseAliveUntilTheSameCallReturns(bool planning)
    {
        using var fixture = new FileTreeRecyclingBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row);
        using var clock = new FileUploadLeaseTests.StepClock();
        repository.OnRenew = () => { if (repository.Renewals == (planning ? 3 : 5)) gateway.Complete(); return true; };
        var tick = new FileTreeRecyclingDispatcher(repository, gateway, new Identity(), fixture.Protector, clock).TickAsync(default);
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
        using var fixture = new FileTreeRecyclingBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity();
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
        var tick = new FileTreeRecyclingDispatcher(repository, gateway, identity, fixture.Protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task RootStartedWithoutAReceiptIsFencedNeverBlindlyReapplied()
    {
        using var fixture = new FileTreeRecyclingBindingTests(); var row = Row(fixture, false); row.RootStartedAtUtc = DateTimeOffset.UtcNow;
        var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await new FileTreeRecyclingDispatcher(repository, gateway, new Identity(), fixture.Protector, TimeProvider.System).TickAsync(default));
        Assert.Equal(0, gateway.Applies); Assert.Equal(1, gateway.Discards); Assert.Equal("Failed", repository.Finished);
    }
    [Fact]
    public async Task WorkerShutdownDoesNotWriteAFakeTerminalState()
    {
        using var fixture = new FileTreeRecyclingBindingTests(); var row = Row(fixture, false); var repository = new Repository(row); var gateway = new Gateway(row);
        using var cancellation = new CancellationTokenSource(); gateway.OnStart = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTreeRecyclingDispatcher(repository, gateway, new Identity(), fixture.Protector, TimeProvider.System).TickAsync(cancellation.Token));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Fail { get; set; } public string Subject { get; set; } = "recycle-admin";
        public DateTimeOffset Expiry { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("test identity unavailable")) : Task.FromResult(new CurrentUserDto(Subject, null, groups, Admin, Expiry));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileTreeRecyclingOperation row) : IFileTreeRecyclingGateway
    {
        private readonly TaskCompletionSource<FileTreeRecyclingPlan> _plan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FileTreeRecyclingReceipt> _copy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Plans { get; private set; } public int Applies { get; private set; } public int Discards { get; private set; } public bool Canceled { get; private set; }
        public Action? OnStart { get; set; }
        public void Complete() { _plan.TrySetResult(FileTreeRecyclingBindingTests.Plan()); if (row.ExecutionJobId is not null) _copy.TrySetResult(Result("Completed")); }
        private FileTreeRecyclingReceipt Result(string state) => new(row.ExecutionJobId!.Value, state, FileTreeRecyclingBindingTests.Plan().Fingerprint,
            row.ExecutionJobId.Value, FileTreeRecyclingBindingTests.Plan().ManifestSha256, FileTreeRecyclingBindingTests.Plan().EntryCount,
            FileTreeRecyclingBindingTests.Plan().TotalBytes, state == "Completed", state == "Completed", state == "Completed" ? 1234 : 0,
            state == "Completed" ? new('F', 64) : "", state == "Completed", state == "Completed", state == "Completed");
        public async Task<FileTreeRecyclingPlan> PlanTreeRecyclingAsync(TerminalActor actor, FileTreeRecyclingCommand command, CancellationToken cancellationToken)
        { Plans++; OnStart?.Invoke(); try { return await _plan.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public async Task<FileTreeRecyclingReceipt> ApplyTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken)
        { Assert.Same(row, operation); Applies++; OnStart?.Invoke(); try { return await _copy.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public Task<FileTreeRecyclingReceipt?> InspectTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken) => Task.FromResult<FileTreeRecyclingReceipt?>(null);
        public Task<FileTreeRecyclingReceipt> DiscardTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken) { Discards++; return Task.FromResult(Result("Aborted")); }
    }
    private sealed class Repository(FileTreeRecyclingOperation row) : IFileTreeRecyclingRepository
    {
        public Task<FileTreeCapturePage> CapturesAsync(FileLocationDto location, Guid? after, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public int Renewals { get; private set; } public int Deferrals { get; private set; } public string? Finished { get; private set; }
        public bool Canceled { get; set; } public Func<bool> OnRenew { get; set; } = () => true;
        public Task<FileTreeRecyclingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileTreeRecyclingOperation?>(row);
        public Task<bool> CancellationRequestedAsync(FileTreeRecyclingOperation row, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> FinishPlanningAsync(FileTreeRecyclingOperation claimed, FileTreeRecyclingPlan? plan, string code, CancellationToken cancellationToken) { Finished = plan is null ? code : "Ready"; return Task.FromResult(true); }
        public Task<FileUploadStartResult> StartedAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken) => Task.FromResult(FileUploadStartResult.Started);
        public Task<bool> RenewAsync(FileTreeRecyclingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> FinishAsync(FileTreeRecyclingOperation claimed, string state, string code, FileTreeRecyclingReceipt? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileTreeRecyclingOperation claimed, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task<FileTreeRecyclingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreeRecyclingOperation> QueueAsync(TerminalActor actor, string key, FileTreeRecyclingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreeRecyclingOperation> ConfirmAsync(TerminalActor actor, FileTreeRecyclingConfirmation claims, long version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreeRecyclingReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
