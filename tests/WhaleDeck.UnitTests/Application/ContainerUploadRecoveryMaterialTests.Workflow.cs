using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerUploadRecoveryMaterialTests
{
    private static TerminalActor Current => new("replacement-container-admin",null,[],new('F',64),"current-container-recovery",null);
    private FileRecoveryOperation RecoveryRow()
    {
        var original = Original(); var snapshot = Material.Capture(original); var now = FileTreeCopyValidation.Timestamp(DateTimeOffset.UtcNow); var key = Guid.NewGuid().ToString("D");
        var row = new FileRecoveryOperation { Id = FileRecoveryValidation.RegistrationId(Current,key),ActorSubject = Current.Subject,LoginBinding = Current.LoginBinding,
            IdempotencyKey = key,Kind = snapshot.Kind,OriginalIntentId = original.Id,OriginalExecutionJobId = original.Id,OriginalVersion = original.Version,
            OriginalBindingHash = snapshot.Binding.OriginalBindingHash,OriginalJson = snapshot.OriginalJson,Disposition = "AcknowledgeUnknown",ReasonCode = "OperatorHandover",
            RequestHash = "",CreatedAtUtc = now,PlanningExpiresAtUtc = now.AddMinutes(15),LeaseToken = Guid.NewGuid(),LeaseExpiresAtUtc = now.AddSeconds(90) };
        var node = JsonSerializer.SerializeToNode(row)!; node[nameof(row.RequestHash)] = _protector.FingerprintRecoveryRequest(FileRecoveryValidation.Request(row));
        row = node.Deserialize<FileRecoveryOperation>()!;
        var observation = ContainerUploadRecoveryValidation.Wrap(snapshot,original,Facts(original));
        row.RootState = observation.State; row.ObservationHash = observation.ReceiptHash; row.ObservationJson = observation.ReceiptJson;
        row.ObservationBindingCurrent = false; row.ReadyAtUtc = now; row.ReadyExpiresAtUtc = now.AddMinutes(5);
        row.PlanHash = _protector.FingerprintRecoveryReady(FileRecoveryValidation.Ready(row)); row.ExecutionJobId = FileRecoveryValidation.ExecutionId(row.Id);
        row.ExecutionExpiresAtUtc = now.AddMinutes(15); row.State = "DispositionQueued";
        Assert.True(FileRecoveryValidation.Bound(row,_protector)); return row;
    }
    [Theory]
    [InlineData("absent")][InlineData("revoked")][InlineData("cache")][InlineData("demoted")]
    public async Task CurrentLoginAndAdministratorAuthorityAreRequiredBeforeAnyRecoveryRpc(string condition)
    {
        var row = RecoveryRow(); var registry = new RecoveryLogins { Active = condition!="revoked",Unavailable = condition=="cache" };
        var identity = new RecoveryIdentity { Administrator = condition!="demoted" }; var repository = new RecoveryRepository(row); var gateway = new RecoveryGateway(this);
        var worker = new FileRecoveryDispatcher(repository,gateway,identity,_protector,TimeProvider.System,condition=="absent"?null:registry);
        Assert.True(await worker.TickAsync(default)); Assert.Equal("FILES_RECOVERY_AUTHORITY_UNAVAILABLE",repository.Finished);
        Assert.Equal(0,gateway.Inspections); Assert.Equal(0,gateway.Fences); Assert.Equal(0,repository.FenceStarts);
    }
    [Fact]
    public async Task LostSealReplyIsReconciledByObservationWithoutRepeatingTheSeal()
    {
        var row = RecoveryRow(); var repository = new RecoveryRepository(row); var gateway = new RecoveryGateway(this) { LoseReply = true };
        var registry = new RecoveryLogins(); var identity = new RecoveryIdentity();
        Assert.True(await new FileRecoveryDispatcher(repository,gateway,identity,_protector,TimeProvider.System,registry).TickAsync(default));
        Assert.Equal(1,gateway.Fences); Assert.Equal(1,repository.Deferrals); Assert.Equal("Unknown",row.State); Assert.Null(repository.Result);
        Assert.True(await new FileRecoveryDispatcher(repository,gateway,identity,_protector,TimeProvider.System,registry).TickAsync(default));
        Assert.Equal(1,gateway.Fences); Assert.Equal(1,repository.FenceStarts); Assert.Equal("Disposed",repository.Finished);
        Assert.NotNull(repository.Result); Assert.True(ContainerUploadRecoveryValidation.Decode(FileRecoveryValidation.Snapshot(row),repository.Result).Sealed);
        Assert.Equal(Current.Subject,gateway.LastActor!.Subject); Assert.Equal("old-container-admin",gateway.OriginalSubject);
    }
    [Fact]
    public async Task LogoutDuringSealDoesNotAcknowledgeOrReleaseTheOriginal()
    {
        var row = RecoveryRow(); var registry = new RecoveryLogins(); var repository = new RecoveryRepository(row);
        var gateway = new RecoveryGateway(this) { OnFence = () => registry.Active = false };
        Assert.True(await new FileRecoveryDispatcher(repository,gateway,new RecoveryIdentity(),_protector,TimeProvider.System,registry).TickAsync(default));
        Assert.Equal(1,gateway.Fences); Assert.Equal("FILES_RECOVERY_AUTHORITY_OR_LEASE_LOST",repository.Finished); Assert.Null(repository.Result);
    }
    [Fact]
    public async Task ChangedHistoricalObservationNeedsAnotherHumanConfirmation()
    {
        var row = RecoveryRow(); var repository = new RecoveryRepository(row); var gateway = new RecoveryGateway(this) { RootState = "Unknown" };
        Assert.True(await new FileRecoveryDispatcher(repository,gateway,new RecoveryIdentity(),_protector,TimeProvider.System,new RecoveryLogins()).TickAsync(default));
        Assert.Equal("FILES_RECOVERY_RECEIPT_CHANGED",repository.Finished); Assert.Equal(0,gateway.Fences); Assert.Equal(0,repository.FenceStarts);
    }
    [Fact]
    public async Task ServiceRefusesQueueAndConfirmationWithAnInactiveCurrentLogin()
    {
        var row = RecoveryRow(); var repository = new RecoveryRepository(row); var registry = new RecoveryLogins { Active = false };
        var authorization = new FileManagementService(null!,null!,null!,new RecoveryIdentity(),TimeProvider.System);
        var service = new FileRecoveryService(authorization,repository,_protector,TimeProvider.System,registry);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueueAsync(Current,new(row.Kind,row.OriginalIntentId,row.OriginalVersion,row.Disposition,row.ReasonCode),Guid.NewGuid().ToString("D"),default));
        Assert.Equal(0,repository.Queues);
        var token = _protector.SignRecovery(FileRecoveryValidation.Ready(row));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConfirmAsync(Current,row.Id,new(token,true,row.Version),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Current,row.Id,default));
        Assert.Equal(0,repository.Confirmations);
    }
    private sealed class RecoveryIdentity : IIdentityDirectory
    {
        public bool Administrator { get; set; } = true;
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject,string? name,IReadOnlyCollection<string> groups,CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject,null,[],Administrator,DateTimeOffset.UtcNow.AddHours(1)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class RecoveryLogins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; init; }
        public Task<bool> IsActiveAsync(string subject,string loginBinding,CancellationToken cancellationToken) => Unavailable
            ? Task.FromException<bool>(new IOException("owned cache unavailable")) : Task.FromResult(Active&&subject==Current.Subject&&loginBinding==Current.LoginBinding);
    }
    private sealed class RecoveryGateway(ContainerUploadRecoveryMaterialTests fixture) : IFileRecoveryGateway
    {
        public int Inspections { get; private set; } public int Fences { get; private set; } public bool LoseReply { get; set; }
        public string RootState { get; init; } = "Published"; public Action? OnFence { get; init; } public TerminalActor? LastActor { get; private set; }
        public string? OriginalSubject { get; private set; } private Guid? _seal;
        public Task<FileRecoveryObservation> InspectForRecoveryAsync(FileRecoverySnapshot original,TerminalActor currentActor,CancellationToken cancellationToken)
        { Inspections++; return Task.FromResult(Observe(original)); }
        public Task<FileRecoveryObservation> FenceForRecoveryAsync(FileRecoverySnapshot original,TerminalActor currentActor,CancellationToken cancellationToken)
        {
            Fences++; LastActor=currentActor; _seal=original.RecoveryExecutionId; OnFence?.Invoke(); var observation=Observe(original);
            if (LoseReply) { LoseReply=false; throw new IOException("owned lost seal reply"); }
            return Task.FromResult(observation);
        }
        private FileRecoveryObservation Observe(FileRecoverySnapshot snapshot)
        {
            var row=fixture.Material.Read<ContainerFileUploadOperation>(snapshot); OriginalSubject=row.ActorSubject;
            return ContainerUploadRecoveryValidation.Wrap(snapshot,row,Facts(row,_seal.HasValue,_seal,RootState));
        }
    }
    private sealed class RecoveryRepository(FileRecoveryOperation row) : IFileRecoveryRepository
    {
        public int FenceStarts { get; private set; } public int Deferrals { get; private set; } public int Queues { get; private set; } public int Confirmations { get; private set; }
        public string? Finished { get; private set; } public FileRecoveryObservation? Result { get; private set; }
        public Task<FileRecoveryOperation?> ClaimAsync(DateTimeOffset now,CancellationToken cancellationToken) => Task.FromResult<FileRecoveryOperation?>(row);
        public Task<bool> RenewAsync(FileRecoveryOperation claimed,DateTimeOffset now,CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> BeginFenceAsync(FileRecoveryOperation claimed,CancellationToken cancellationToken)
        { FenceStarts++; row.FenceStartedAtUtc=DateTimeOffset.UtcNow; return Task.FromResult(true); }
        public Task<bool> FinishPlanningAsync(FileRecoveryOperation claimed,FileRecoveryObservation? observation,string code,CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> FinishAsync(FileRecoveryOperation claimed,FileRecoveryObservation? observation,string code,CancellationToken cancellationToken)
        { Finished=observation is null?code:"Disposed"; Result=observation; return Task.FromResult(true); }
        public Task DeferAsync(FileRecoveryOperation claimed,CancellationToken cancellationToken)
        { Deferrals++; row.State="Unknown"; return Task.CompletedTask; }
        public Task<FileRecoveryOperation> QueueAsync(TerminalActor actor,string key,QueueFileRecoveryCommand command,CancellationToken cancellationToken)
        { Queues++; throw new NotSupportedException(); }
        public Task<FileRecoveryOperation?> OwnedAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => Task.FromResult<FileRecoveryOperation?>(actor.Subject==row.ActorSubject&&actor.LoginBinding==row.LoginBinding&&id==row.Id?row:null);
        public Task<FileRecoveryPurgeDto?> PurgeSummaryAsync(FileRecoveryOperation original,CancellationToken cancellationToken) => Task.FromResult<FileRecoveryPurgeDto?>(null);
        public Task<FileRecoveryOperation> ConfirmAsync(TerminalActor actor,FileRecoveryPlanClaims claims,long version,CancellationToken cancellationToken)
        { Confirmations++; throw new NotSupportedException(); }
        public Task CancelAsync(TerminalActor actor,Guid id,CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
