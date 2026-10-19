using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreePackingLeaseTests
{
    private static FileTreePackingOperation Row(FileTreePackingTests fixture, bool planning)
    {
        var row = fixture.Operation(!planning);
        if (planning) { row.PlanJson = null; row.PlanHash = null; row.ReadyExpiresAtUtc = null; row.State = "Queued"; }
        return row;
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task LongPlanningAndCopyKeepTheSameLeaseAliveUntilTheSameCallReturns(bool planning)
    {
        using var fixture = new FileTreePackingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row);
        using var clock = new FileUploadLeaseTests.StepClock();
        repository.OnRenew = () => { if (repository.Renewals == (planning ? 3 : 5)) gateway.Complete(); return true; };
        var tick = new FileTreePackingDispatcher(repository, gateway, new Identity(), fixture.Protector, clock).TickAsync(default);
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
        using var fixture = new FileTreePackingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity();
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
        var tick = new FileTreePackingDispatcher(repository, gateway, identity, fixture.Protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task RootStartedWithoutAReceiptIsFencedNeverBlindlyReapplied()
    {
        using var fixture = new FileTreePackingTests(); var row = Row(fixture, false); row.RootStartedAtUtc = DateTimeOffset.UtcNow;
        var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await new FileTreePackingDispatcher(repository, gateway, new Identity(), fixture.Protector, TimeProvider.System).TickAsync(default));
        Assert.Equal(0, gateway.Applies); Assert.Equal(1, gateway.Discards); Assert.Equal("Failed", repository.Finished);
    }
    [Fact]
    public async Task WorkerShutdownDoesNotWriteAFakeTerminalState()
    {
        using var fixture = new FileTreePackingTests(); var row = Row(fixture, false); var repository = new Repository(row); var gateway = new Gateway(row);
        using var cancellation = new CancellationTokenSource(); gateway.OnStart = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTreePackingDispatcher(repository, gateway, new Identity(), fixture.Protector, TimeProvider.System).TickAsync(cancellation.Token));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Fail { get; set; } public string Subject { get; set; } = "tree-admin";
        public DateTimeOffset Expiry { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("test identity unavailable")) : Task.FromResult(new CurrentUserDto(Subject, null, groups, Admin, Expiry));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileTreePackingOperation row) : IFileTreePackingGateway
    {
        private readonly TaskCompletionSource<FileTreePackingPlan> _plan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FileTreePackingReceipt> _copy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IAsyncEnumerable<FileDownloadPart> DownloadTreePackingAsync(FileTreePackingOperation operation, TerminalActor actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public int Plans { get; private set; } public int Applies { get; private set; } public int Discards { get; private set; } public bool Canceled { get; private set; }
        public Action? OnStart { get; set; }
        public void Complete() { _plan.TrySetResult(FileTreePackingTests.Plan()); if (row.ExecutionJobId is not null) _copy.TrySetResult(Result("Completed")); }
        private FileTreePackingReceipt Result(string state) => new(row.ExecutionJobId!.Value, state, FileTreePackingTests.Plan().Fingerprint, "tar",
            FileTreePackingTests.Plan().EntryCount, FileTreePackingTests.Plan().TotalBytes, state == "Completed" ? 2048 : 0, state == "Completed" ? new('D', 64) : "", state == "Completed" ? new('F', 64) : "", false);
        public async Task<FileTreePackingPlan> PlanTreePackingAsync(TerminalActor actor, FileTreePackingCommand command, CancellationToken cancellationToken)
        { Plans++; OnStart?.Invoke(); try { return await _plan.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public async Task<FileTreePackingReceipt> ApplyTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken)
        { Assert.Same(row, operation); Applies++; OnStart?.Invoke(); try { return await _copy.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public Task<FileTreePackingReceipt?> InspectTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => Task.FromResult<FileTreePackingReceipt?>(null);
        public Task<FileTreePackingReceipt> DiscardTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) { Discards++; return Task.FromResult(Result("Aborted")); }
    }
    private sealed class Repository(FileTreePackingOperation row) : IFileTreePackingRepository
    {
        public Task<FileTreePackingOperation?> ArtifactAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AuditDownloadAsync(TerminalActor actor, FileTreePackingOperation operation, string result, CancellationToken cancellationToken) => throw new NotSupportedException();
        public int Renewals { get; private set; } public int Deferrals { get; private set; } public string? Finished { get; private set; }
        public bool Canceled { get; set; } public Func<bool> OnRenew { get; set; } = () => true;
        public Task<FileTreePackingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileTreePackingOperation?>(row);
        public Task<bool> CancellationRequestedAsync(FileTreePackingOperation row, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> FinishPlanningAsync(FileTreePackingOperation claimed, FileTreePackingPlan? plan, string code, CancellationToken cancellationToken) { Finished = plan is null ? code : "Ready"; return Task.FromResult(true); }
        public Task<FileUploadStartResult> StartedAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) => Task.FromResult(FileUploadStartResult.Started);
        public Task<bool> RenewAsync(FileTreePackingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> FinishAsync(FileTreePackingOperation claimed, string state, string code, FileTreePackingReceipt? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileTreePackingOperation claimed, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task<FileTreePackingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingOperation> QueueAsync(TerminalActor actor, string key, FileTreePackingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePackingOperation> ConfirmAsync(TerminalActor actor, FileTreePackingConfirmation claims, long version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePackingReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
