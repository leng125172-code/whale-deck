using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileTreePurgingLeaseTests
{
    private static FileTreePurgingOperation Row(FileTreePurgingBindingTests fixture, bool planning)
    {
        var row = fixture.Operation(!planning);
        if (planning) { row.PlanJson = null; row.PlanHash = null; row.ReadyExpiresAtUtc = null; row.State = "Queued"; }
        return row;
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task LongPlanningAndPurgingKeepTheSameLeaseAliveUntilTheSameCallReturns(bool planning)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row);
        using var clock = new FileUploadLeaseTests.StepClock();
        repository.OnRenew = () => { if (repository.Renewals == (planning ? 3 : 5)) gateway.Complete(); return true; };
        var tick = new FileTreePurgingDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, clock).TickAsync(default);
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
        using var fixture = new FileTreePurgingBindingTests(); var row = Row(fixture, planning); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(row.ActorSubject);
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
        var tick = new FileTreePurgingDispatcher(repository, gateway, identity, fixture.Protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task RootStartedWithoutAReceiptIsFencedNeverBlindlyReapplied()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = Row(fixture, false); row.RootStartedAtUtc = DateTimeOffset.UtcNow; row.State = "Purging";
        var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await new FileTreePurgingDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, TimeProvider.System).TickAsync(default));
        Assert.Equal(0, gateway.Applies); Assert.Equal(1, gateway.Discards); Assert.Equal("Failed", repository.Finished);
    }
    [Fact]
    public async Task WorkerShutdownDoesNotWriteAFakeTerminalState()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = Row(fixture, false); var repository = new Repository(row); var gateway = new Gateway(row);
        using var cancellation = new CancellationTokenSource(); gateway.OnStart = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTreePurgingDispatcher(repository, gateway, new Identity(row.ActorSubject), fixture.Protector, TimeProvider.System).TickAsync(cancellation.Token));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    [Theory]
    [InlineData("cancel")][InlineData("role")]
    public async Task CompletedRootWriteWhoseFinalGuardFailsIsOnlyInspectedOnNextTick(string reason)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = Row(fixture, false);
        var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(row.ActorSubject);
        gateway.OnStart = () => { if (reason == "cancel") repository.Canceled = true; else identity.Admin = false; gateway.Complete(); };
        var dispatcher = new FileTreePurgingDispatcher(repository, gateway, identity, fixture.Protector, TimeProvider.System);
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
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false);
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
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false);
        var repository = new Repository(row); var identity = new Identity(row.ActorSubject);
        var claims = new FileTreePurgingConfirmation(failure == "id" ? Guid.NewGuid() : row.Id,
            failure == "actor" ? "other" : row.ActorSubject, failure == "login" ? new('0', 64) : row.LoginBinding,
            row.CaptureId, FileTreePurgingBindingTests.Plan().Command.Scope, FileTreePurgingBindingTests.Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        var service = Service(fixture, repository, new Gateway(row), identity, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync(Actor(row), row.Id,
            new(fixture.Protector.SignConfirmation(claims), true, row.Version), default));
        Assert.Equal(0, repository.Confirmations);
    }
    [Fact]
    public async Task ExpiredPlanStillReadsButDoesNotIssueAnotherConfirmation()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false);
        var repository = new Repository(row); using var clock = new FileUploadLeaseTests.StepClock { Now = row.ReadyExpiresAtUtc!.Value.AddSeconds(1) };
        var dto = await Service(fixture, repository, new Gateway(row), new Identity(row.ActorSubject), clock).GetAsync(Actor(row), row.Id, default);
        Assert.Equal("Expired", dto.State); Assert.Null(dto.Confirmation);
        Assert.Equal(FileTreePurgingBindingTests.Plan().EntryCount, dto.EntryCount);
        Assert.Equal(FileTreePurgingBindingTests.Plan().Command.Scope, dto.Command.Scope);
    }
    [Theory]
    [InlineData("Archive")][InlineData("Originals")][InlineData("All")]
    public async Task ReadyWarningsReflectScopeAndNeverExposeHistoricalOwnerOrPrivateStorageProof(string scope)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false, scope);
        var dto = await Service(fixture, new Repository(row), new Gateway(row), new Identity(row.ActorSubject), TimeProvider.System).GetAsync(Actor(row), row.Id, default);
        Assert.NotNull(dto.Confirmation); Assert.Contains("不可撤销", dto.Confirmation.Warning, StringComparison.Ordinal);
        if (scope == "Archive") Assert.Contains("原件可能已经不可用", dto.Confirmation.Warning, StringComparison.Ordinal);
        else if (scope == "Originals") Assert.Contains("最后副本", dto.Confirmation.Warning, StringComparison.Ordinal);
        else Assert.Contains("全部可恢复副本", dto.Confirmation.Warning, StringComparison.Ordinal);
        Assert.Equal(scope, dto.Command.Scope); Assert.False(dto.DeletionStarted); Assert.False(dto.ArchiveRemoved); Assert.False(dto.OriginalsRemoved);
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        foreach (var hidden in new[] { "CaptureOwner", "CatalogueProofHash", "CaptureMetadataHash", "StorageHash", "RetainedIdentity", "SourcePolicyHash" })
            Assert.DoesNotContain(hidden, json, StringComparison.Ordinal);
    }
    [Fact]
    public async Task LostRootResponseShowsUnknownEffectsInsteadOfFalseNoDeletion()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation();
        row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.State = "Unknown";
        var dto = await Service(fixture, new Repository(row), new Gateway(row), new Identity(row.ActorSubject), TimeProvider.System).GetAsync(Actor(row), row.Id, default);
        Assert.Equal("Unknown", dto.State); Assert.Null(dto.DeletionStarted); Assert.Null(dto.ArchiveRemoved); Assert.Null(dto.OriginalsRemoved); Assert.Null(dto.Confirmation);
    }
    [Theory]
    [InlineData("capture")][InlineData("scope")][InlineData("fingerprint")][InlineData("expiry")]
    public async Task AuthenticatedButDifferentConfirmationCannotReachConfirmationTransaction(string changed)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false); var repo = new Repository(row);
        var claims = new FileTreePurgingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, row.CaptureId, "All", FileTreePurgingBindingTests.Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        claims = changed switch { "capture" => claims with { CaptureId = Guid.NewGuid() }, "scope" => claims with { Scope = "Archive" },
            "fingerprint" => claims with { Fingerprint = new('8', 64) }, _ => claims with { ExpiresAtUtc = claims.ExpiresAtUtc.AddMicroseconds(1) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fixture, repo, new Gateway(row), new Identity(row.ActorSubject), TimeProvider.System)
            .ConfirmAsync(Actor(row), row.Id, new(fixture.Protector.SignConfirmation(claims), true, row.Version), default));
        Assert.Equal(0, repo.Confirmations);
    }
    [Theory]
    [InlineData("expiry")][InlineData("cancel")][InlineData("version")]
    public async Task ExpiredCanceledOrStaleConfirmationCannotReachRepository(string changed)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false); var repo = new Repository(row);
        using var clock = new FileUploadLeaseTests.StepClock { Now = changed == "expiry" ? row.ReadyExpiresAtUtc!.Value.AddSeconds(1) : row.CreatedAtUtc.AddSeconds(1) };
        if (changed == "cancel") row.CancelRequestedAtUtc = row.CreatedAtUtc.AddSeconds(1);
        var claims = new FileTreePurgingConfirmation(row.Id, row.ActorSubject, row.LoginBinding, row.CaptureId, "All", FileTreePurgingBindingTests.Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fixture, repo, new Gateway(row), new Identity(row.ActorSubject), clock)
            .ConfirmAsync(Actor(row), row.Id, new(fixture.Protector.SignConfirmation(claims), true, changed == "version" ? row.Version + 1 : row.Version), default));
        Assert.Equal(0, repo.Confirmations);
    }
    [Theory]
    [InlineData("get")][InlineData("by-key")][InlineData("queue")][InlineData("confirm")][InlineData("cancel")][InlineData("acknowledge")]
    public async Task EveryPublicBusinessEntryRejectsCurrentNonAdministratorBeforeRepositoryOrRoot(string action)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(false); var repository = new Repository(row);
        var gateway = new Gateway(row); var identity = new Identity(row.ActorSubject) { Admin = false };
        var service = Service(fixture, repository, gateway, identity, TimeProvider.System); var actor = Actor(row);
        Task Call() => action switch { "get" => service.GetAsync(actor, row.Id, default), "by-key" => service.GetByKeyAsync(actor, row.IdempotencyKey, default),
            "queue" => service.QueueAsync(actor, new(row.CaptureId, "All"), row.IdempotencyKey, default),
            "confirm" => service.ConfirmAsync(actor, row.Id, new("not-a-plan", true, row.Version), default),
            "cancel" => service.CancelAsync(actor, row.Id, default), _ => service.AcknowledgeAsync(actor, row.Id, new(row.Version, true), default) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Call);
        Assert.Equal(0, repository.OwnedReads); Assert.Equal(0, repository.Confirmations);
        Assert.Equal(0, gateway.Plans); Assert.Equal(0, gateway.Applies); Assert.Equal(0, gateway.Discards);
    }
    [Theory]
    [InlineData("Archive")][InlineData("Originals")][InlineData("All")]
    public async Task UnknownReceiptWithoutPositiveComponentProofDoesNotMeanThatSelectedBytesAreIntact(string scope)
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(true, scope);
        var plan = FileTreePurgingValidation.ReadPlan(row);
        var receipt = new FileTreePurgingReceipt(row.ExecutionJobId!.Value, "DirectoryPurge", "Unknown", plan.Fingerprint,
            row.CaptureId, scope, plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, true, false, false);
        row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.State = "Unknown"; row.DeletionStarted = true;
        row.ReceiptJson = System.Text.Json.JsonSerializer.Serialize(receipt); row.ReceiptObservedAtUtc = row.CreatedAtUtc.AddSeconds(2);
        row.ReceiptHash = fixture.Protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
        var dto = await Service(fixture, new Repository(row), new Gateway(row), new Identity(row.ActorSubject), TimeProvider.System).GetAsync(Actor(row), row.Id, default);
        Assert.True(dto.DeletionStarted);
        if (FileTreePurgingValidation.Selects(scope, "Archive")) Assert.Null(dto.ArchiveRemoved); else Assert.False(dto.ArchiveRemoved);
        if (FileTreePurgingValidation.Selects(scope, "Originals")) Assert.Null(dto.OriginalsRemoved); else Assert.False(dto.OriginalsRemoved);
    }
    [Fact]
    public async Task UnknownPartialReceiptKeepsEstablishedRemovalAndLeavesOtherSelectedComponentUnknown()
    {
        using var fixture = new FileTreePurgingBindingTests(); var row = fixture.Operation(); var plan = FileTreePurgingValidation.ReadPlan(row);
        var receipt = new FileTreePurgingReceipt(row.ExecutionJobId!.Value, "DirectoryPurge", "Unknown", plan.Fingerprint,
            row.CaptureId, "All", plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, true, false, true);
        row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.State = "Unknown"; row.DeletionStarted = true; row.OriginalsRemoved = true;
        row.ReceiptJson = System.Text.Json.JsonSerializer.Serialize(receipt); row.ReceiptObservedAtUtc = row.CreatedAtUtc.AddSeconds(2);
        row.ReceiptHash = fixture.Protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
        var dto = await Service(fixture, new Repository(row), new Gateway(row), new Identity(row.ActorSubject), TimeProvider.System).GetAsync(Actor(row), row.Id, default);
        Assert.True(dto.DeletionStarted); Assert.True(dto.OriginalsRemoved); Assert.Null(dto.ArchiveRemoved);
    }
    private static TerminalActor Actor(FileTreePurgingOperation row) => new(row.ActorSubject, null, [], row.LoginBinding, "unit-service", null);
    private static FileTreePurgingService Service(FileTreePurgingBindingTests fixture, Repository repository, Gateway gateway, Identity identity, TimeProvider clock) =>
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
    private sealed class Gateway(FileTreePurgingOperation row) : IFileTreePurgingGateway
    {
        private readonly TaskCompletionSource<FileTreePurgingPlan> _plan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<FileTreePurgingReceipt> _copy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Plans { get; private set; } public int Applies { get; private set; } public int Discards { get; private set; } public bool Canceled { get; private set; }
        public Action? OnStart { get; set; }
        public void Complete() { _plan.TrySetResult(FileTreePurgingBindingTests.Plan()); if (row.ExecutionJobId is not null) _copy.TrySetResult(Result("Completed")); }
        public FileTreePurgingReceipt Result(string state) => new(row.ExecutionJobId!.Value, "DirectoryPurge", state,
            FileTreePurgingBindingTests.Plan().Fingerprint, row.CaptureId, "All", FileTreePurgingBindingTests.Plan().EntryCount,
            FileTreePurgingBindingTests.Plan().TotalBytes, FileTreePurgingBindingTests.Plan().ArchiveBytes,
            state != "Aborted", state == "Completed", state == "Completed");
        public async Task<FileTreePurgingPlan> PlanTreePurgingAsync(TerminalActor actor, FileTreePurgingCommand command, CancellationToken cancellationToken)
        { Plans++; OnStart?.Invoke(); try { return await _plan.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public async Task<FileTreePurgingReceipt> ApplyTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken)
        { Assert.Same(row, operation); Applies++; OnStart?.Invoke(); try { return await _copy.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Canceled = true; throw; } }
        public FileTreePurgingReceipt? Observed { get; set; }
        public Task<FileTreePurgingReceipt?> InspectTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken) => Task.FromResult(Observed);
        public Task<FileTreePurgingReceipt> DiscardTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken) { Discards++; return Task.FromResult(Result("Aborted")); }
    }
    private sealed class Repository(FileTreePurgingOperation row) : IFileTreePurgingRepository
    {
        public int Renewals { get; private set; } public int Deferrals { get; private set; } public string? Finished { get; private set; }
        public int OwnedReads { get; private set; } public int Confirmations { get; private set; }
        public bool Canceled { get; set; } public Func<bool> OnRenew { get; set; } = () => true;
        public Task<FileTreePurgingOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileTreePurgingOperation?>(row);
        public Task<bool> CancellationRequestedAsync(FileTreePurgingOperation row, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> FinishPlanningAsync(FileTreePurgingOperation claimed, FileTreePurgingPlan? plan, string code, CancellationToken cancellationToken) { Finished = plan is null ? code : "Ready"; return Task.FromResult(true); }
        public Task<FileUploadStartResult> StartedAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken)
        { row.RootStartedAtUtc = DateTimeOffset.UtcNow; row.State = "Purging"; return Task.FromResult(FileUploadStartResult.Started); }
        public Task<bool> RenewAsync(FileTreePurgingOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> FinishAsync(FileTreePurgingOperation claimed, string state, string code, FileTreePurgingReceipt? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileTreePurgingOperation claimed, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task<FileTreePurgingOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
        { OwnedReads++; return Task.FromResult<FileTreePurgingOperation?>(id == row.Id && actor.Subject == row.ActorSubject && actor.LoginBinding == row.LoginBinding ? row : null); }
        public Task<FileTreePurgingOperation> QueueAsync(TerminalActor actor, string key, CreateFileTreePurgingCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileTreePurgingOperation> ConfirmAsync(TerminalActor actor, FileTreePurgingConfirmation claims, long version, CancellationToken cancellationToken)
        { Confirmations++; return Task.FromResult(row); }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileTreePurgingReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
