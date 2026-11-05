using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
namespace WhaleDeck.UnitTests.Application;

// Explicit memory persistence and typed gateway doubles: not SQL, Authentik, installed RPC or UI evidence.
public sealed partial class ContainerRecycleRestoreWorkflowTests
{
    private static ContainerRecycleIntent MakeSource(FilePlanProtector protector, DateTimeOffset now)
    {
        var actor = new TerminalActor("original-recycle-admin", null, [], new('8',64), "original-trace", null); var key = Guid.NewGuid();
        var created = now.AddHours(-1); var id = ContainerRecycleWorkflowValidation.IntentId(actor.Subject, actor.LoginBinding, key);
        var plan = ContainerRecycleWorkflowFixture.Plan();
        var request = new ContainerRecycleRequestBinding(id, key, actor.Subject, actor.LoginBinding, actor.TraceId, ContainerRecycleWorkflowValidation.Owner(actor.Subject, actor.LoginBinding), plan.Command, created, created.AddMinutes(15));
        var row = new ContainerRecycleIntent { Id=id, IdempotencyKey=key, ActorSubject=actor.Subject, LoginBinding=actor.LoginBinding, TraceId=actor.TraceId, OwnerHash=request.Owner,
            ContainerId=plan.Command.Location.ContainerId, InstanceId=plan.Command.Location.InstanceId, RelativePath=plan.Command.Location.Path,
            CommandJson=ContainerRecycleWorkflowValidation.Serialize(plan.Command), RequestHash=protector.FingerprintRecycleRequest(request), CreatedAtUtc=created, PlanningExpiresAtUtc=request.PlanningExpiresAtUtc,
            PlanJson=ContainerRecycleWorkflowValidation.Serialize(plan), ReadyExpiresAtUtc=created.AddMinutes(5), ExecutionJobId=ContainerRecycleWorkflowValidation.ExecutionId(id), ConfirmedAtUtc=created.AddMinutes(1), ExecutionExpiresAtUtc=created.AddMinutes(16),
            State="Succeeded", RootStartedAtUtc=created.AddMinutes(1), CompletedAtUtc=created.AddMinutes(2), Version=4 };
        row.PlanHash=protector.FingerprintRecyclePlan(ContainerRecycleWorkflowValidation.PlanBinding(row)); row.ExecutionHash=protector.FingerprintRecycleExecution(ContainerRecycleWorkflowValidation.ExecutionBinding(row));
        var op=ContainerRecycleWorkflowValidation.Reference(row); var receipt=new ContainerRecycleReceiptDto(op.Id,row.ContainerId,row.InstanceId,plan.Fingerprint,plan.TargetProof,"Trashed",true,false,true,true,new('7',64),true,true,false);
        row.ReceiptJson=ContainerRecycleWorkflowValidation.Serialize(receipt);row.ReceiptHash=protector.FingerprintRecycleReceipt(new(op,receipt));row.RootState="Trashed";row.SourceRetained=true;row.SourceRestored=false;row.BindingCurrent=true;row.HistoryMayExist=true;row.HistoryVerified=true;row.HistoryDigest=receipt.HistoryDigest;row.HistoryAvailable=true;
        Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(row,protector)); return row;
    }
    private sealed class SourceRepository : IContainerRecycleRepository
    {
        public ContainerRecycleIntent Row { get; set; } = null!;
        public Action? OnFind { get; set; }
        public Task<ContainerRecycleIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<ContainerRecycleIntent?> FindAsync(Guid id, CancellationToken cancellationToken) { OnFind?.Invoke(); return Task.FromResult<ContainerRecycleIntent?>(Row.Id==id?Row:null); }
        public Task<IReadOnlyList<ContainerRecycleIntent>> UnresolvedAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<ContainerRecycleIntent> EnqueueAsync(ContainerRecycleIntent row, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<ContainerRecycleIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleConfirmation claims, long version, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<ContainerRecycleIntent?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<bool> MarkPlanningAsync(ContainerRecycleIntent claimed, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task FinishPlanningAsync(ContainerRecycleIntent claimed, ContainerRecyclePlanDto? plan, string code, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleIntent claimed, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<bool> RenewAsync(ContainerRecycleIntent claimed, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task<bool> FinishAsync(ContainerRecycleIntent claimed, string state, string code, ContainerRecycleReceiptDto? receipt, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task DeferAsync(ContainerRecycleIntent claimed, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleReceiptDto receipt, CancellationToken cancellationToken) => throw new NotSupportedException("read-only source repository");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _key = Path.GetTempFileName(); public Clock Clock { get; } = new();
        public Identity Identity { get; } public Logins Logins { get; } = new(); public Gateway Gateway { get; }
        public Repository Repository { get; } public FilePlanProtector Protector { get; }
        public TerminalActor Actor { get; } = new("copy-test-admin", null, [], new('A', 64), "copy-test-trace", null);
        public string Key { get; } = Guid.NewGuid().ToString("D"); public ContainerRecycleRestoreCommandDto Command => Gateway.PlanValue.Command;
        public SourceRepository Source { get; } = new(); public ContainerRecycleRestoreSourceResolver Resolver { get; }
        public ContainerRecycleRestoreWorkflowService Service { get; } public ContainerRecycleRestoreDispatcher Dispatcher { get; }
        public Fixture()
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock); Source.Row = MakeSource(Protector, Clock.Now); Gateway = new(Source.Row); Repository = new(Protector, Clock);
            var files = new FileManagementService(Unused<IFileGateway>(), Unused<IFileUploadRepository>(), Protector, Identity, Clock);
            var auth = new ContainerFileUploadService(files, Logins, Unused<IContainerFileWriteGateway>(), Unused<IContainerFileUploadRepository>(), Protector, Clock);
            Resolver = new(Source, Protector, Protector, auth, Identity, Clock);
            Service = new(auth, Repository, Protector, Gateway, Clock, Identity, Resolver); Dispatcher = new(Repository, Gateway, Service, Protector, Clock);
        }
        private static T Unused<T>() where T : class => DispatchProxy.Create<T, ContainerUploadServiceTests.Unused>();
        public Task<ContainerRecycleRestoreIntentDto> Queue() => Service.QueueAsync(Actor, Command.Source.Plan.Command.Location.ContainerId, Source.Row.Id, Key, default);
        public Task<ContainerRecycleRestoreIntentDto> Get() => Service.GetAsync(Actor, Command.Source.Plan.Command.Location.ContainerId, Repository.Row!.Id, default);
        public async Task Ready() { await Queue(); await Dispatcher.TickAsync(default); }
        public async Task<ContainerRecycleRestoreIntentDto> Confirm() { var r = await Get(); return await Service.ConfirmAsync(Actor, Command.Source.Plan.Command.Location.ContainerId, r.Id, new(r.Confirmation!.PlanHash, true, r.Version), default); }
        public async Task Create() { await Ready(); await Confirm(); }
        public void Dispose() => File.Delete(_key);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 1, 2, 3, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity(TimeProvider clock) : IIdentityDirectory, IPrivilegedIdentityDirectory
    {
        public bool Admin { get; set; } = true; public bool Unavailable { get; set; }
        public bool? LiveAdmin { get; set; } public bool LiveUnavailable { get; set; }
        public Action? OnLiveRead { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken token) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic identity unavailable")) : Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken token)
        {
            OnLiveRead?.Invoke();
            return LiveUnavailable ? Task.FromException<CurrentUserDto>(new IOException("synthetic live identity unavailable")) :
                LiveAdmin is { } admin ? Task.FromResult(new CurrentUserDto(subject, name, [], admin, clock.GetUtcNow().AddHours(2))) : ResolveCurrentAsync(subject, name, [], token);
        }
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Logins : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true; public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string binding, CancellationToken token) => Unavailable ? Task.FromException<bool>(new IOException("synthetic cache unavailable")) : Task.FromResult(Active);
    }
    private sealed class Gateway : IContainerRecycleRestoreGateway
    {
        public ContainerRecycleRestorePlanDto PlanValue { get; set; }
        public Gateway(ContainerRecycleIntent source)
        {
            var original = ContainerRecycleWorkflowValidation.Reference(source); var old = original.Plan;
            var command = new ContainerRecycleRestoreCommandDto(new(original.Id, original.Owner, ContainerRecycleWorkflowValidation.SnapshotPlan(old)), source.HistoryDigest!);
            PlanValue = new(1, command, old.TargetProof, old.ParentIdentity, old.ParentVersion, old.OwnerUid, old.OwnerGid, new(2049, 7), old.SourceIdentity, old.SourceChangedAtUnixNs + 100, new('F',64));
        }
        public Exception? PlanFailure { get; set; }
        public int Requires { get; set; } public int Plans { get; set; } public int Inspects { get; set; } public int Applies { get; set; } public int Discards { get; set; } public bool Unavailable { get; set; } public Action? AfterPlan { get; set; }
        public ContainerRecycleRestoreReceiptDto? Receipt { get; set; } public Exception? DiscardFailure { get; set; } public ContainerRecycleRestoreOperationDto? LastOperation { get; set; }
        public Func<ContainerRecycleRestoreOperationDto, ContainerRecycleRestoreReceiptDto>? ApplyHandler { get; set; }
        public Func<ContainerRecycleRestoreOperationDto, CancellationToken, Task<ContainerRecycleRestoreReceiptDto>>? AsyncApply { get; set; }
        public Task RequireContainerRecycleRestoreAsync(CancellationToken token) { Requires++; if (Unavailable) throw new IOException("synthetic unsupported Agent"); return Task.CompletedTask; }
        public Task<ContainerRecycleRestorePlanDto> PlanContainerRecycleRestoreAsync(TerminalActor actor, ContainerRecycleRestoreCommandDto command, CancellationToken token) { Plans++; if (PlanFailure is not null) throw PlanFailure; AfterPlan?.Invoke(); return Task.FromResult(PlanValue); }
        public Task<ContainerRecycleRestoreReceiptDto?> InspectContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto op, CancellationToken token) { Inspects++; LastOperation = op; return Task.FromResult(Receipt); }
        public Task<ContainerRecycleRestoreReceiptDto> ApplyContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto op, CancellationToken token) { Applies++; LastOperation = op; return AsyncApply?.Invoke(op, token) ?? Task.FromResult(Receipt = ApplyHandler?.Invoke(op) ?? ReceiptFor(op, "Completed")); }
        public Task<ContainerRecycleRestoreReceiptDto> DiscardContainerRecycleRestoreAsync(ContainerRecycleRestoreOperationDto op, CancellationToken token) { Discards++; LastOperation = op; if (DiscardFailure is not null) throw DiscardFailure; return Task.FromResult(Receipt ?? ReceiptFor(op, "Aborted")); }
        public static ContainerRecycleRestoreReceiptDto ReceiptFor(ContainerRecycleRestoreOperationDto op, string state) => new(op.Id,
            op.Plan.Command.Source.OperationId, op.Plan.Fingerprint, op.Plan.TargetProof, state, state == "Completed", false,
            state != "Aborted", state != "Aborted" ? op.Plan.Command.HistoryDigest : "", state != "Aborted",
            state == "Completed" ? "Restored" : null, state == "Completed" ? false : null, state == "Completed" ? true : null,
            state == "Unknown" ? null : state == "Completed");
    }

    private sealed class Repository(IContainerRecycleRestoreProtector protector, Clock clock) : IContainerRecycleRestoreRepository
    {
        public ContainerRecycleRestoreIntent? Row { get; set; } public int Enqueues { get; set; } public int Deferrals { get; set; } public int Confirms { get; set; } public bool Canceled { get; set; } public bool Renewal { get; set; } = true; public Action? AfterStart { get; set; } public Action? AfterFind { get; set; }
        public ContainerFileUploadStartResult Start = ContainerFileUploadStartResult.Started;
        public Task<ContainerRecycleRestoreIntent?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id && Row.ActorSubject == actor.Subject && Row.LoginBinding == actor.LoginBinding ? Row : null);
        public Task<ContainerRecycleRestoreIntent?> FindAsync(Guid id, CancellationToken token) { AfterFind?.Invoke(); return Task.FromResult(Row?.Id == id ? Row : null); }
        public Task<IReadOnlyList<ContainerRecycleRestoreIntent>> UnresolvedAsync(int take, CancellationToken token) => Task.FromResult<IReadOnlyList<ContainerRecycleRestoreIntent>>(Row?.State == "Unknown" ? [Row] : []);
        public Task<ContainerRecycleRestoreIntent> EnqueueAsync(ContainerRecycleRestoreIntent row, CancellationToken token) { Enqueues++; Row = row; return Task.FromResult(row); }
        public Task<ContainerRecycleRestoreIntent> ConfirmAsync(TerminalActor actor, ContainerRecycleRestoreConfirmation claims, long version, CancellationToken token)
        {
            var r = Row!; if (r.ReadyExpiresAtUtc <= clock.Now) throw new InvalidOperationException("CONTAINER_RECYCLE_RESTORE_PLAN_EXPIRED");
            Confirms++; r.ExecutionJobId = ContainerRecycleRestoreWorkflowValidation.ExecutionId(r.Id); r.ConfirmedAtUtc = clock.Now; r.ExecutionExpiresAtUtc = clock.Now.AddMinutes(15);
            r.ExecutionHash = protector.FingerprintRecycleRestoreExecution(ContainerRecycleRestoreWorkflowValidation.ExecutionBinding(r)); r.State = "Pending"; r.Version++; return Task.FromResult(r);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerRecycleRestoreIntent?> ClaimAsync(DateTimeOffset now, CancellationToken token) { if (Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown" || Row.State == "Ready" && Row.ReadyExpiresAtUtc > now) return Task.FromResult<ContainerRecycleRestoreIntent?>(null); Row.LeaseToken = Guid.NewGuid(); return Task.FromResult<ContainerRecycleRestoreIntent?>(Row); }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerRecycleRestoreIntent row, CancellationToken token) { row.State = "Planning"; return Task.FromResult(true); }
        public Task FinishPlanningAsync(ContainerRecycleRestoreIntent row, ContainerRecycleRestorePlanDto? plan, string code, CancellationToken token)
        {
            row.ResultCode = code; row.Version++;
            if (plan is null) { row.State = code == "CONTAINER_RECYCLE_RESTORE_CANCELED" ? "Canceled" : "Failed"; row.CompletedAtUtc = clock.Now; }
            else { row.PlanJson = ContainerRecycleRestoreWorkflowValidation.Serialize(plan); row.ReadyExpiresAtUtc = clock.Now.AddMinutes(5); row.PlanHash = protector.FingerprintRecycleRestorePlan(ContainerRecycleRestoreWorkflowValidation.PlanBinding(row)); row.State = "Ready"; }
            return Task.CompletedTask;
        }
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerRecycleRestoreIntent row, DateTimeOffset now, CancellationToken token)
        { if (Start == ContainerFileUploadStartResult.Started) { row.RootStartedAtUtc = now; row.PublishBarrier = true; row.State = "Applying"; AfterStart?.Invoke(); } return Task.FromResult(Start); }
        public Task<bool> RenewAsync(ContainerRecycleRestoreIntent row, DateTimeOffset now, CancellationToken token) => Task.FromResult(Renewal);
        private void SaveReceipt(ContainerRecycleRestoreIntent row, ContainerRecycleRestoreReceiptDto? receipt)
        {
            if (receipt is null) return;
            ContainerRecycleRestoreWorkflowValidation.RequireCompatibleReceipt(row, receipt);
            row.ReceiptJson = ContainerRecycleRestoreWorkflowValidation.Serialize(receipt);
            row.ReceiptHash = protector.FingerprintRecycleRestoreReceipt(new(ContainerRecycleRestoreWorkflowValidation.Reference(row), receipt));
            row.RootState = receipt.State; row.SourceState = receipt.SourceState; row.RestoredByThisOperation = receipt.RestoredByThisOperation; row.SourceRetained = receipt.SourceRetained; row.SourceRestored = receipt.SourceRestored;
            row.BindingCurrent = receipt.BindingCurrent; row.HistoryAvailable = receipt.HistoryAvailable;
            row.HistoryVerified = receipt.HistoryVerified; row.HistoryDigest = receipt.HistoryDigest.Length == 0 ? null : receipt.HistoryDigest;
        }
        public Task<bool> FinishAsync(ContainerRecycleRestoreIntent row, string state, string code, ContainerRecycleRestoreReceiptDto? receipt, CancellationToken token)
        {
            SaveReceipt(row, receipt); row.State = state; row.ResultCode = code;
            row.Version++;
            if (state != "Unknown") { row.CompletedAtUtc = clock.Now; row.PublishBarrier = false; }
            return Task.FromResult(true);
        }
        public Task DeferAsync(ContainerRecycleRestoreIntent row, CancellationToken token) { Deferrals++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleRestoreReceiptDto receipt, CancellationToken token)
        {
            SaveReceipt(Row!, receipt); Row!.State = "Acknowledged"; Row.PublishBarrier = false; Row.AcknowledgedBy = actor.Subject;
            Row.AcknowledgementJson = JsonSerializer.Serialize(receipt); Row.Version++; return Task.CompletedTask;
        }
    }
}
