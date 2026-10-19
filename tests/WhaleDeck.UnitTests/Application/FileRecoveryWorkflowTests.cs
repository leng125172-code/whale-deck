using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileRecoveryWorkflowTests : IDisposable
{
    private readonly FileTreeMoveTests _move = new();
    private FilePlanProtector Protector => (FilePlanProtector)_move.Protector;
    private FileRecoveryMaterial Material => new(Protector, Protector, Protector, Protector, Protector, Protector);
    private static TerminalActor Actor => new("replacement-admin", null, [], new('C', 64), "recovery-workflow", null);
    public void Dispose() => _move.Dispose();
    private FileRecoveryOperation Row(bool execution = true, string rootState = "Unknown")
    {
        var original = _move.Operation(); original.State = "Unknown"; original.Version = 17; original.LeaseToken = null; original.LeaseExpiresAtUtc = null;
        var snapshot = Material.Capture(original); var key = Guid.NewGuid().ToString("D"); var now = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow);
        var row = new FileRecoveryOperation { Id = FileRecoveryValidation.RegistrationId(Actor, key), ActorSubject = Actor.Subject, LoginBinding = Actor.LoginBinding,
            IdempotencyKey = key, Kind = snapshot.Kind, OriginalIntentId = original.Id, OriginalExecutionJobId = original.ExecutionJobId!.Value,
            OriginalVersion = original.Version, OriginalBindingHash = snapshot.Binding.OriginalBindingHash, OriginalJson = snapshot.OriginalJson,
            Disposition = rootState == "Completed" ? "ReconcileCompleted" : "AcknowledgeUnknown", ReasonCode = "OriginalSessionLost", RequestHash = "",
            CreatedAtUtc = now, PlanningExpiresAtUtc = now.AddMinutes(15), NextAttemptAtUtc = now, LeaseToken = Guid.NewGuid(), LeaseExpiresAtUtc = now.AddSeconds(90) };
        var node = JsonSerializer.SerializeToNode(row)!; node[nameof(row.RequestHash)] = Protector.FingerprintRecoveryRequest(FileRecoveryValidation.Request(row));
        row = node.Deserialize<FileRecoveryOperation>()!;
        if (execution)
        {
            var observation = Observe(row, rootState); row.RootState = rootState; row.ObservationHash = observation.ReceiptHash;
            row.ObservationJson = observation.ReceiptJson; row.ObservationBindingCurrent = observation.BindingCurrent;
            row.ReadyAtUtc = now; row.ReadyExpiresAtUtc = now.AddMinutes(5); row.PlanHash = Protector.FingerprintRecoveryReady(FileRecoveryValidation.Ready(row));
            row.ExecutionJobId = FileRecoveryValidation.ExecutionId(row.Id); row.ExecutionExpiresAtUtc = now.AddMinutes(15); row.State = "DispositionQueued";
        }
        Assert.True(FileRecoveryValidation.Bound(row, Protector)); return row;
    }
    private static FileRecoveryObservation Observe(FileRecoveryOperation row, string state)
    {
        var plan = FileTreeMoveTests.Plan(); var completed = state == "Completed";
        var receipt = state == "Missing" ? null : new FileTreeMoveReceipt(row.OriginalExecutionJobId, state, plan.Fingerprint, plan.EntryCount, plan.TotalBytes,
            completed ? new('E', 64) : "", completed || state == "Unknown", completed, completed, state == "Unknown");
        var json = JsonSerializer.Serialize(receipt);
        return new(state, FileRecoveryValidation.ReceiptHash(row.Kind, row.OriginalExecutionJobId, state, json), completed) { ReceiptJson = json };
    }
    [Fact]
    public void SnapshotAndObservationNeverPrintOrSerializePrivateOriginalMaterial()
    {
        var row = Row(); var snapshot = FileRecoveryValidation.Snapshot(row); var observation = Observe(row, "Unknown");
        Assert.DoesNotContain("OriginalJson", JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
        Assert.DoesNotContain("ReceiptJson", JsonSerializer.Serialize(observation), StringComparison.Ordinal);
        Assert.DoesNotContain(row.OriginalJson, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(observation.ReceiptJson, observation.ToString(), StringComparison.Ordinal);
        Assert.Equal("tree-admin", Material.Read<FileTreeMoveOperation>(snapshot).ActorSubject);
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Aborted")][InlineData("Completed")][InlineData("Missing")]
    public void OnlyBoundTerminalProofCanBeUsedAndJsonbPropertyOrderingIsNotAChange(string state)
    {
        var row = Row(rootState: state); var snapshot = FileRecoveryValidation.Snapshot(row); var observation = Observe(row, state);
        Material.ValidateObservation(snapshot, observation);
        Assert.Throws<FileTransferRejectedException>(() => Material.ValidateObservation(snapshot, observation with { ReceiptHash = new('A', 64) }));
        Assert.Throws<FileTransferRejectedException>(() => Material.ValidateObservation(snapshot, observation with { BindingCurrent = !observation.BindingCurrent }));
        if (state != "Missing")
        {
            var parsed = JsonNode.Parse(observation.ReceiptJson)!.AsObject(); var reordered = new JsonObject();
            foreach (var entry in parsed.Reverse()) reordered.Add(entry.Key, entry.Value?.DeepClone());
            Material.ValidateObservation(snapshot, observation with { ReceiptJson = reordered.ToJsonString() });
        }
    }
    [Theory]
    [InlineData("OriginalVersion")][InlineData("OriginalBindingHash")][InlineData("LoginBinding")][InlineData("Disposition")][InlineData("PlanHash")]
    public void ChangingRequestOrPlanMaterialCannotRetainItsMac(string field)
    {
        var row = Row(); var node = JsonSerializer.SerializeToNode(row)!;
        if (field == "OriginalVersion") node[field] = row.OriginalVersion + 1; else node[field] = field == "Disposition" ? "ReconcileCompleted" : new string('9', 64);
        Assert.False(FileRecoveryValidation.Bound(node.Deserialize<FileRecoveryOperation>()!, Protector));
    }
    [Fact]
    public async Task PlanQueriesOriginalOnlyAndDoesNotFenceOrInventAnExecutionJob()
    {
        var row = Row(false); var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(1, gateway.Inspections); Assert.Equal(0, gateway.Fences); Assert.Null(row.ExecutionJobId);
        Assert.Equal("Ready", repository.Finished); Assert.Equal(Actor.Subject, gateway.LastActor!.Subject);
        Assert.NotEqual(Material.Read<FileTreeMoveOperation>(FileRecoveryValidation.Snapshot(row)).ActorSubject, gateway.LastActor.Subject);
    }
    [Fact]
    public async Task ExecutionAlwaysUsesCurrentAdministratorAndFencesWithoutApply()
    {
        var row = Row(); var repository = new Repository(row); var gateway = new Gateway(row);
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(1, gateway.Inspections); Assert.Equal(1, gateway.Fences); Assert.Equal(1, repository.FenceStarts);
        Assert.Equal("Disposed", repository.Finished); Assert.Equal("Unknown", repository.Result!.State);
        Assert.Equal(Actor.Subject, gateway.LastActor!.Subject); Assert.Equal(Actor.LoginBinding, gateway.LastActor.LoginBinding);
        Assert.DoesNotContain(typeof(IFileRecoveryGateway).GetMethods(), method => method.Name.Contains("Apply", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("Unknown")][InlineData("Aborted")][InlineData("Completed")]
    public async Task ARestartAfterFenceStartInspectsTheSameReceiptAndDoesNotRepeatAnyWrite(string state)
    {
        var row = Row(rootState: state); row.FenceStartedAtUtc = DateTimeOffset.UtcNow;
        var repository = new Repository(row); var gateway = new Gateway(row) { State = state };
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(1, gateway.Inspections); Assert.Equal(0, gateway.Fences); Assert.Equal("Disposed", repository.Finished);
    }
    [Fact]
    public async Task MissingReceiptAfterRecordedFenceUsesOnlyTheSameIdempotentTombstone()
    {
        var row = Row(rootState: "Missing"); row.FenceStartedAtUtc = DateTimeOffset.UtcNow;
        var repository = new Repository(row); var gateway = new Gateway(row) { State = "Missing", FencedState = "Aborted" };
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(1, gateway.Fences); Assert.Equal("Aborted", repository.Result!.State);
    }
    [Fact]
    public async Task ChangedReceiptRequiresANewPlanBeforeAnyFence()
    {
        var row = Row(); var repository = new Repository(row); var gateway = new Gateway(row) { State = "Completed" };
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(0, gateway.Fences); Assert.Null(repository.Result); Assert.Equal("FILES_RECOVERY_RECEIPT_CHANGED", repository.Finished);
    }
    [Theory]
    [InlineData("role")][InlineData("subject")][InlineData("expiry")][InlineData("unavailable")]
    public async Task CurrentAuthorityFailureDoesNotUseOriginalAuthorityOrTouchRoot(string failure)
    {
        var row = Row(); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity();
        switch (failure) { case "role": identity.Admin = false; break; case "subject": identity.Subject = "original-admin"; break;
            case "expiry": identity.Expiry = DateTimeOffset.UtcNow.AddSeconds(-1); break; default: identity.Fail = true; break; }
        Assert.True(await Dispatcher(repository, gateway, identity).TickAsync(default));
        Assert.Equal(0, gateway.Inspections); Assert.Equal(0, gateway.Fences); Assert.Null(repository.Result);
        Assert.Equal("FILES_RECOVERY_AUTHORITY_UNAVAILABLE", repository.Finished);
    }
    [Fact]
    public async Task FenceResponseLossIsDeferredWithoutCallingItSuccessful()
    {
        var row = Row(); var repository = new Repository(row); var gateway = new Gateway(row) { LoseFenceResponse = true };
        Assert.True(await Dispatcher(repository, gateway).TickAsync(default));
        Assert.Equal(1, gateway.Fences); Assert.Equal(1, repository.Deferrals); Assert.Null(repository.Finished); Assert.Null(repository.Result);
    }
    [Theory]
    [InlineData("role")][InlineData("lease")][InlineData("deadline")]
    public async Task FinalGuardAfterARootReplyMustPassBeforeAnyDisposition(string failure)
    {
        var row = Row(); var repository = new Repository(row); var gateway = new Gateway(row); var identity = new Identity(); using var clock = new FileUploadLeaseTests.StepClock();
        gateway.OnFence = () => { if (failure == "role") identity.Admin = false; else if (failure == "lease") repository.Renew = false; else clock.Now = row.ExecutionExpiresAtUtc!.Value.AddSeconds(1); };
        Assert.True(await Dispatcher(repository, gateway, identity, clock).TickAsync(default));
        Assert.Equal(1, gateway.Fences); Assert.Null(repository.Result); Assert.Equal("FILES_RECOVERY_AUTHORITY_OR_LEASE_LOST", repository.Finished);
    }
    [Fact]
    public async Task LongInspectionRenewsTheSameLeaseAndShutdownDoesNotInventAResult()
    {
        var row = Row(false); var repository = new Repository(row); var gateway = new Gateway(row) { HoldInspection = true };
        using var clock = new FileUploadLeaseTests.StepClock(); using var cancellation = new CancellationTokenSource();
        var tick = Dispatcher(repository, gateway, clock: clock).TickAsync(cancellation.Token);
        await clock.AdvanceAsync(); await clock.AdvanceAsync(); await repository.ThirdRenewal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tick);
        Assert.Null(repository.Finished); Assert.Equal(0, repository.Deferrals); Assert.Equal(0, gateway.Fences);
    }
    [Fact]
    public async Task ServiceReturnsOnlyTheCurrentLoginPlanAndNeverPrivateOriginalMaterial()
    {
        var row = Row(); row.ExecutionJobId = null; row.ExecutionExpiresAtUtc = null; row.State = "Ready";
        var identity = new Identity(); var repository = new Repository(row);
        var service = new FileRecoveryService(new(null!, null!, null!, identity, TimeProvider.System), repository, Protector, TimeProvider.System);
        var result = await service.GetAsync(Actor, row.Id, default); Assert.NotNull(result.Confirmation);
        var claims = Protector.VerifyRecovery(result.Confirmation.PlanHash); Assert.Equal(Actor.Subject, claims.Actor); Assert.Equal(Actor.LoginBinding, claims.LoginBinding);
        var json = JsonSerializer.SerializeToElement(result);
        Assert.DoesNotContain(json.EnumerateObject(), property => property.Name is "OriginalJson" or "ObservationJson" or "LoginBinding" or "OwnerHash" or "RequestHash");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Actor with { LoginBinding = new('D', 64) }, row.Id, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConfirmAsync(Actor, row.Id, new(result.Confirmation.PlanHash, false, row.Version), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync(Actor with { LoginBinding = new('D', 64) }, row.Id, new(result.Confirmation.PlanHash, true, row.Version), default));
        identity.Expiry = DateTimeOffset.UtcNow.AddSeconds(-1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Actor, row.Id, default));
    }
    [Fact]
    public async Task AnExpiredReadyPlanDoesNotReturnAFreshConfirmation()
    {
        var row = Row(); row.ExecutionJobId = null; row.ExecutionExpiresAtUtc = null; row.State = "Ready";
        using var clock = new FileUploadLeaseTests.StepClock { Now = row.ReadyExpiresAtUtc!.Value };
        var identity = new Identity(); var service = new FileRecoveryService(new(null!, null!, null!, identity, clock), new Repository(row), Protector, clock);
        var result = await service.GetAsync(Actor, row.Id, default); Assert.Equal("Expired", result.State); Assert.Null(result.Confirmation);
    }
    private FileRecoveryDispatcher Dispatcher(Repository repository, Gateway gateway, Identity? identity = null, TimeProvider? clock = null) =>
        new(repository, gateway, identity ?? new(), Protector, clock ?? TimeProvider.System);
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Fail { get; set; } public string Subject { get; set; } = Actor.Subject;
        public DateTimeOffset Expiry { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Fail ? Task.FromException<CurrentUserDto>(new InvalidOperationException("identity test unavailable")) : Task.FromResult(new CurrentUserDto(Subject, null, groups, Admin, Expiry));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Gateway(FileRecoveryOperation row) : IFileRecoveryGateway
    {
        public string State { get; set; } = "Unknown"; public string FencedState { get; set; } = "Unknown";
        public bool LoseFenceResponse { get; set; } public bool HoldInspection { get; set; }
        public int Inspections { get; private set; } public int Fences { get; private set; }
        public TerminalActor? LastActor { get; private set; } public Action? OnFence { get; set; }
        public async Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken)
        {
            Assert.Equal(FileRecoveryValidation.Target(row), original.Binding); Inspections++; LastActor = currentActor;
            if (HoldInspection) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Observe(row, State);
        }
        public Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original, TerminalActor currentActor, CancellationToken cancellationToken)
        {
            Assert.Equal(FileRecoveryValidation.Target(row), original.Binding); Fences++; LastActor = currentActor; OnFence?.Invoke();
            return LoseFenceResponse ? Task.FromException<FileRecoveryObservation>(new IOException("test lost reply")) : Task.FromResult(Observe(row, FencedState));
        }
    }
    private sealed class Repository(FileRecoveryOperation row) : IFileRecoveryRepository
    {
        public int FenceStarts { get; private set; } public int Deferrals { get; private set; } public int Renewals { get; private set; }
        public TaskCompletionSource ThirdRenewal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Renew { get; set; } = true; public string? Finished { get; private set; } public FileRecoveryObservation? Result { get; private set; }
        public Task<FileRecoveryOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<FileRecoveryOperation?>(row);
        public Task<bool> RenewAsync(FileRecoveryOperation claimed, DateTimeOffset now, CancellationToken cancellationToken) { Renewals++; if (Renewals >= 3) ThirdRenewal.TrySetResult(); return Task.FromResult(Renew); }
        public Task<bool> FinishPlanningAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken) { Finished = observation is null ? code : "Ready"; Result = observation; return Task.FromResult(true); }
        public Task<bool> BeginFenceAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken) { FenceStarts++; return Task.FromResult(true); }
        public Task<bool> FinishAsync(FileRecoveryOperation claimed, FileRecoveryObservation? observation, string code, CancellationToken cancellationToken) { Finished = observation is null ? code : "Disposed"; Result = observation; return Task.FromResult(true); }
        public Task DeferAsync(FileRecoveryOperation claimed, CancellationToken cancellationToken) { Deferrals++; return Task.CompletedTask; }
        public Task<FileRecoveryOperation> QueueAsync(TerminalActor actor, string key, QueueFileRecoveryCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileRecoveryOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult<FileRecoveryOperation?>(row.ActorSubject == actor.Subject && row.LoginBinding == actor.LoginBinding && row.Id == id ? row : null);
        public Task<FileRecoveryOperation> ConfirmAsync(TerminalActor actor, FileRecoveryPlanClaims claims, long version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
