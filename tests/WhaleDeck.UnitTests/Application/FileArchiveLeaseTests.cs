using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileArchiveLeaseTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-archive-lease-" + Guid.NewGuid().ToString("N"));
    private readonly IFileArchiveProtector _protector;
    public FileArchiveLeaseTests()
    {
        File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
        _protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() => File.Delete(_key);
    private FileArchiveOperation Operation()
    {
        var plan = FileArchiveTests.Plan(); var id = Guid.NewGuid(); var expiry = DateTimeOffset.UtcNow.AddMinutes(15); var login = new string('E', 64);
        return new() { Id = id, ActorSubject = "archive-admin", LoginBinding = login, OwnerHash = FileManagementService.Hash("file-owner-v1:archive-admin:" + login),
            RequestHash = _protector.Fingerprint(new(id, "archive-admin", login, plan, expiry)), Kind = "trash", RootId = "test.files", RelativePath = "source",
            TargetHash = FileManagementService.Hash("test.files:source"), ExpectedVersion = plan.Command.ExpectedVersion, Sha256 = plan.Sha256, SizeBytes = 7,
            PlanJson = JsonSerializer.Serialize(plan), ExpiresAtUtc = expiry, LeaseToken = Guid.NewGuid() };
    }
    private static FileArchiveReceipt Completed(FileArchiveOperation row) => new("trash", "Completed", FileArchiveValidation.ReadPlan(row).Fingerprint, new('F', 64),
        7, row.Sha256, true, true, row.Id, new(row.Id.ToString("N"), "database-platform.backup.hdd", row.ExpectedVersion, 7, row.Sha256, 1760000000000, false));
    [Fact]
    public async Task LongArchiveRenewsTheSameLeaseAndInvocationUntilItsVerifiedReceipt()
    {
        var row = Operation(); var repository = new Repository(row); var gateway = new Gateway(row); using var clock = new FileUploadLeaseTests.StepClock();
        repository.OnRenew = () => { if (repository.Renewals == 4) gateway.Result.TrySetResult(Completed(row)); return true; };
        var tick = new FileArchiveDispatcher(repository, gateway, new Identity(), _protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(4, repository.Renewals); Assert.Equal(1, gateway.Applied); Assert.False(gateway.Canceled);
        Assert.Equal("Succeeded", repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    [Theory]
    [InlineData("cancel")][InlineData("lease")][InlineData("role")][InlineData("subject")][InlineData("snapshot-expired")]
    [InlineData("identity-failure")][InlineData("database-failure")][InlineData("intent-expired")]
    public async Task FailedInFlightGuardStopsTheExactHelperAndNeverInventsSuccess(string failure)
    {
        var row = Operation(); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(); using var clock = new FileUploadLeaseTests.StepClock();
        gateway.OnApply = () => {
            switch (failure)
            {
                case "cancel": repository.Canceled = true; break;
                case "lease": repository.OnRenew = () => false; break;
                case "role": identity.Admin = false; break;
                case "subject": identity.Subject = "other-admin"; break;
                case "snapshot-expired": identity.Expires = clock.Now.AddSeconds(-1); break;
                case "identity-failure": identity.Fail = true; break;
                case "database-failure": repository.OnRenew = () => throw new InvalidOperationException("owned fake database failure"); break;
                case "intent-expired": clock.Now = row.ExpiresAtUtc.AddSeconds(1); break;
            }
        };
        var tick = new FileArchiveDispatcher(repository, gateway, identity, _protector, clock).TickAsync(default);
        await clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, gateway.Applied); Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(1, repository.Deferrals);
    }
    [Fact]
    public async Task WorkerCancellationDoesNotReleaseAnUncertainLeaseAsSuccess()
    {
        var row = Operation(); var repository = new Repository(row); var gateway = new Gateway(row); using var clock = new FileUploadLeaseTests.StepClock(); using var cancellation = new CancellationTokenSource();
        gateway.OnApply = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileArchiveDispatcher(repository, gateway, new Identity(), _protector, clock).TickAsync(cancellation.Token));
        Assert.True(gateway.Canceled); Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public string Subject { get; set; } = "archive-admin"; public DateTimeOffset Expires { get; set; } = DateTimeOffset.UtcNow.AddHours(2); public bool Fail { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("owned fake identity failure")) : Task.FromResult(new CurrentUserDto(Subject, null, groups, Admin, Expires));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileArchiveOperation row) : IFileArchiveGateway
    {
        public TaskCompletionSource<FileArchiveReceipt> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Applied { get; private set; } public bool Canceled { get; private set; } public Action? OnApply { get; set; }
        public Task<FileArchiveReceipt?> InspectArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => Task.FromResult<FileArchiveReceipt?>(null);
        public async Task<FileArchiveReceipt> ApplyArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken)
        {
            Assert.Same(row, operation); Applied++; OnApply?.Invoke();
            try { return await Result.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; throw; }
        }
        public Task<FileArchiveReceipt> DiscardArchiveAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileHistoryPage> ListHistoryAsync(FileHistoryCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileArchivePlan> PlanArchiveAsync(TerminalActor actor, FileArchiveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Repository(FileArchiveOperation row) : IFileArchiveRepository
    {
        public Task<FileArchiveRegistration> RememberAsync(TerminalActor actor, Guid id, string key, FileArchivePlan plan, DateTimeOffset expires, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(FileArchiveRegistration? Registration, FileArchiveOperation? Operation, bool Expired)> RegistrationAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneRegistrationsAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelRegistrationAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Func<bool> OnRenew { get; set; } = () => true; public int Renewals { get; private set; } public bool Canceled { get; set; } public string? Finished { get; private set; } public int Deferrals { get; private set; }
        public Task<FileArchiveOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileArchiveOperation?>(row);
        public Task<bool> RenewAsync(FileArchiveOperation operation, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; return Task.FromResult(OnRenew()); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Canceled);
        public Task<FileUploadStartResult> StartedAsync(FileArchiveOperation operation, CancellationToken cancellationToken) => Task.FromResult(FileUploadStartResult.Started);
        public Task<bool> FinishAsync(FileArchiveOperation operation, string state, string code, FileArchiveReceipt? receipt, CancellationToken cancellationToken) { Finished = state; return Task.FromResult(true); }
        public Task DeferAsync(FileArchiveOperation operation, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task ObserveAsync(IReadOnlyCollection<FileVersionDto> versions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileArchiveOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileArchiveOperation?> ResolveExpiredRegistrationAsync(TerminalActor actor, Guid id, DateTimeOffset expiry, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileArchiveOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileArchivePlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, FileArchiveReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
