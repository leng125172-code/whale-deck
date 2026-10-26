using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileUploadLeaseTests
{
    private sealed class FastClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, TimeSpan.FromMilliseconds(5), period);
    }
    internal sealed class StepClock : TimeProvider, IDisposable
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
        private readonly Queue<StepTimer> _timers = new();
        private readonly SemaphoreSlim _created = new(0);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new StepTimer(callback, state);
            lock (_timers) _timers.Enqueue(timer);
            _created.Release(); return timer;
        }
        public async Task AdvanceAsync()
        {
            Assert.True(await _created.WaitAsync(TimeSpan.FromSeconds(5)));
            StepTimer timer; lock (_timers) timer = _timers.Dequeue(); timer.Fire();
        }
        public void Dispose() => _created.Dispose();
        private sealed class StepTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;
            public void Fire() { if (Interlocked.Exchange(ref _disposed, 1) == 0) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Plans : IFilePlanProtector
    {
        public string Fingerprint(FileUploadBinding binding) => new('D', 64);
        public string Sign(FileUploadPlanClaims claims) => throw new NotSupportedException();
        public FileUploadPlanClaims Verify(string plan) => throw new NotSupportedException();
    }
    private static FileUploadOperation Operation() => new()
    {
        Id = Guid.NewGuid(), ActorSubject = "lease-admin", LoginBinding = new('A', 64),
        OwnerHash = FileManagementService.Hash("file-owner-v1:lease-admin:" + new string('A', 64)),
        RootId = "files.shared", RelativePath = "sample.txt", TargetHash = FileManagementService.Hash("files.shared:sample.txt"),
        RequestHash = new('D', 64), ExpectedVersion = new('B', 64), Sha256 = new('C', 64), SizeBytes = 3,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15), LeaseToken = Guid.NewGuid(), State = "Ready"
    };
    private static FileUploadReceiptDto Completed(FileUploadOperation operation) => new("Completed", operation.SizeBytes, operation.Sha256,
        new('E', 64), true, true, false, new(operation.Id.ToString("N"), "database-platform.backup.hdd", operation.ExpectedVersion, 7, new('F', 64), 1760000000000, false));

    [Fact]
    public async Task PublicationRenewsItsLeaseUntilTheSameInvocationReturnsVerifiedProof()
    {
        var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation); var identity = new Identity(); using var clock = new StepClock();
        repository.OnRenew = () => { if (repository.Renewals == 3) gateway.Result.TrySetResult(Completed(operation)); return true; };
        var tick = new FileUploadDispatcher(repository, gateway, identity, clock, new Plans(), new Sessions()).TickAsync(default);
        await clock.AdvanceAsync(); await clock.AdvanceAsync();
        Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, repository.Renewals); Assert.Equal(1, gateway.Publishes); Assert.False(gateway.Canceled);
        Assert.Equal("Succeeded", repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    [Theory]
    [InlineData("cancel")][InlineData("lease")][InlineData("role")][InlineData("subject")][InlineData("snapshot-expired")]
    [InlineData("identity-failure")][InlineData("database-failure")][InlineData("intent-expired")][InlineData("logout")][InlineData("session-failure")]
    public async Task FailedGuardCancelsAndObservesTheOnlyInFlightPublishWithoutClaimingSuccess(string failure)
    {
        var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation); var identity = new Identity(); var clock = new FastClock(); var logins = new Sessions();
        gateway.OnPublish = () =>
        {
            switch (failure)
            {
                case "cancel": repository.Canceled = true; break;
                case "lease": repository.OnRenew = () => false; break;
                case "role": identity.Administrator = false; break;
                case "subject": identity.Subject = "other-admin"; break;
                case "snapshot-expired": identity.Expires = clock.Now.AddSeconds(-1); break;
                case "identity-failure": identity.Fail = true; break;
                case "database-failure": repository.OnRenew = () => throw new InvalidOperationException("test database unavailable"); break;
                case "intent-expired": clock.Now = operation.ExpiresAtUtc.AddSeconds(1); break;
                case "logout": logins.Active = false; break;
                case "session-failure": logins.Fail = true; break;
            }
        };
        Assert.True(await new FileUploadDispatcher(repository, gateway, identity, clock, new Plans(), logins).TickAsync(default).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Equal(1, gateway.Publishes); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task ExternalWorkerCancellationAlsoStopsTheSamePublishAndDoesNotReleaseItsLeaseAsSuccess()
    {
        using var cancellation = new CancellationTokenSource(); var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation);
        gateway.OnPublish = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileUploadDispatcher(repository, gateway, new Identity(), new FastClock(), new Plans(), new Sessions())
            .TickAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    [Fact]
    public async Task LeaseLostBeforePublicationNeverStartsAHelper()
    {
        var operation = Operation(); var repository = new Repository(operation) { OnRenew = () => false }; var gateway = new Gateway(operation);
        Assert.True(await new FileUploadDispatcher(repository, gateway, new Identity(), new FastClock(), new Plans(), new Sessions()).TickAsync(default));
        Assert.Equal(0, gateway.Publishes); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task LoggedOutOriginCannotPublishEvenWhenItsSubjectRemainsAdministrator()
    {
        var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation);
        await new FileUploadDispatcher(repository, gateway, new Identity(), new FastClock(), new Plans(), new Sessions { Active = false }).TickAsync(default);
        Assert.Equal(0, gateway.Publishes); Assert.Equal(1, gateway.Discards); Assert.Equal("Canceled", repository.Finished);
    }
    [Fact]
    public async Task SessionRegistryFailureBeforePublicationDefersWithoutSigningOrClaimingCancellation()
    {
        var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation);
        await new FileUploadDispatcher(repository, gateway, new Identity(), new FastClock(), new Plans(), new Sessions { Fail = true }).TickAsync(default);
        Assert.Equal(0, gateway.Publishes); Assert.Equal(0, gateway.Discards); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task AProvenPublicationIsReconciledAfterLogoutWithoutReissuingIt()
    {
        var operation = Operation(); var repository = new Repository(operation); var gateway = new Gateway(operation) { ReceiptOverride = Completed(operation) };
        await new FileUploadDispatcher(repository, gateway, new Identity(), new FastClock(), new Plans(), new Sessions { Active = false }).TickAsync(default);
        Assert.Equal(0, gateway.Publishes); Assert.Equal(0, gateway.Discards); Assert.Equal("Succeeded", repository.Finished);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Administrator { get; set; } = true; public string Subject { get; set; } = "lease-admin";
        public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddHours(2); public bool Fail { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("test identity unavailable")) : Task.FromResult(new CurrentUserDto(Subject, null, [], Administrator, Expires));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Sessions : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true;
        public bool Fail { get; set; }
        public Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken)
        {
            Assert.Equal("lease-admin", subject); Assert.Equal(new string('A', 64), loginBinding);
            return Fail ? Task.FromException<bool>(new InvalidOperationException("fixture session store unavailable")) : Task.FromResult(Active);
        }
    }
    private sealed class Gateway(FileUploadOperation operation) : IFileGateway
    {
        public TaskCompletionSource<FileUploadReceiptDto> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OnPublish { get; set; } public int Publishes { get; private set; } public bool Canceled { get; private set; }
        public int Discards { get; private set; }
        public FileUploadReceiptDto? ReceiptOverride { get; set; }
        public async Task<FileUploadReceiptDto> PublishFileAsync(FileUploadOperation value, CancellationToken cancellationToken)
        {
            Assert.Same(operation, value); Publishes++; OnPublish?.Invoke();
            try { return await Result.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
        public Task<FileUploadReceiptDto?> UploadReceiptAsync(FileUploadOperation value, CancellationToken cancellationToken) =>
            Task.FromResult<FileUploadReceiptDto?>(ReceiptOverride ?? new("Staged", operation.SizeBytes, operation.Sha256, "", false, false, false));
        public Task<IReadOnlyCollection<FileRootDto>> RootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FilePageDto> ListFilesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileEntryDto?> InspectFileAsync(FileLocationDto location, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<FileDownloadPart> DownloadFileAsync(FileLocationDto location, string expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto> StageFileAsync(FileUploadOperation value, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadReceiptDto> DiscardFileAsync(FileUploadOperation value, CancellationToken cancellationToken)
        { Discards++; return Task.FromResult(new FileUploadReceiptDto("Aborted", operation.SizeBytes, operation.Sha256, "", false, false, false)); }
    }
    private sealed class Repository(FileUploadOperation operation) : IFileUploadRepository
    {
        public Func<bool> OnRenew { get; set; } = () => true; public int Renewals { get; private set; }
        public bool Canceled { get; set; } public string? Finished { get; private set; } public int Deferrals { get; private set; }
        public Task<FileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileUploadOperation?>(operation);
        public Task<bool> RenewAsync(FileUploadOperation value, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<FileUploadStartResult> StartedAsync(FileUploadOperation value, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(FileUploadStartResult.Started);
        public Task<bool> FinishAsync(FileUploadOperation value, string state, string code, FileUploadReceiptDto? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileUploadOperation value, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task AuditReadAsync(TerminalActor actor, FileLocationDto location, string result, FileReadPurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileUploadPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReadyAsync(FileUploadOperation value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
