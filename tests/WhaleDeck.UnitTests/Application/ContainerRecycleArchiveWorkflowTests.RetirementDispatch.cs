using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;
using StartResult = WhaleDeck.Application.Models.ContainerFileUploadStartResult;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleArchiveWorkflowTests
{
    [Fact]
    public async Task RetirementWorkflowRequiresCapabilityAndBindsReadAndConfirmToCurrentLoginAndContainer()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        f.Agent.Available = false;
        await Assert.ThrowsAsync<IOException>(() => f.Queue());
        Assert.Null(f.Store.Row);
        f.Agent.Available = true;
        var ready = await f.Ready();
        Assert.NotNull(ready.Confirmation);
        var wrongLogin = f.Actor with { LoginBinding = new('1', 64) };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(wrongLogin, f.Container, ready.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => f.Service.GetAsync(f.Actor, new string('b', 64), ready.Id, default));
        Assert.Null((await f.Service.GetForRecoveryAsync(wrongLogin, f.Container, ready.Id, default)).Confirmation);
        f.CopyFixture.Logins.Active = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Confirm(ready));
        Assert.Equal("Ready", f.Store.Row!.State);
        f.CopyFixture.Logins.Active = true;
        var result = await f.Confirm(ready);
        Assert.NotNull(result.ExecutionJobId);
        Assert.Equal(result.ExecutionJobId, (await f.Confirm(ready)).ExecutionJobId);
        Assert.Equal(result.Id, (await f.Queue()).Id);
        Assert.Empty(f.Agent.Actions);
    }

    [Fact]
    public async Task RetirementWorkerStopsBeforeReserveOnRevokedLiveRole()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        await f.Confirm(await f.Ready());
        f.CopyFixture.Identity.LiveAdmin = false;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.Empty(f.Agent.Actions);
        Assert.Equal(Retirement.Prefix + "AUTHORITY_REVOKED", f.Store.Code);
        Assert.Null(f.Store.Row!.RootStartedAtUtc);
    }

    [Fact]
    public async Task RetirementWorkerRechecksLogoutAfterReserveAndOnlyDiscardsOriginalOperation()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        var execution = await f.Confirm(await f.Ready());
        f.Agent.AfterReserve = () => f.CopyFixture.Logins.Active = false;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.Equal("Reserve", string.Join(' ', f.Agent.Actions.Select(a => a.Method)));
        Assert.Null(f.Store.Row!.ApplyStartedAtUtc);
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.Equal("Reserve Inspect Discard", string.Join(' ', f.Agent.Actions.Select(a => a.Method)));
        Assert.All(f.Agent.Actions, a => Assert.Equal(execution.ExecutionJobId, a.Id));
        Assert.Equal("Aborted", f.Store.Receipt!.State);
    }

    [Fact]
    public async Task RetirementWorkerLosesReplyAfterApplyAndReclaimsOnlyForObservation()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        var execution = await f.Confirm(await f.Ready());
        f.Agent.ApplyFailure = true;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.NotNull(f.Store.Row!.ApplyStartedAtUtc);
        var oldLease = f.Store.Row.LeaseToken;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.NotEqual(oldLease, f.Store.Row.LeaseToken);
        Assert.Equal("Reserve Apply Inspect", string.Join(' ', f.Agent.Actions.Select(a => a.Method)));
        Assert.All(f.Agent.Actions, a => Assert.Equal(execution.ExecutionJobId, a.Id));
        Assert.True(f.Store.Receipt!.RetirementApplied);
    }

    [Fact]
    public async Task RetirementWorkerCancelsInFlightApplyAfterRevocationAndRecoversSameExecution()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        var execution = await f.Confirm(await f.Ready());
        f.Agent.InFlight = async token =>
        {
            f.CopyFixture.Identity.LiveAdmin = false;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await f.Dispatcher.TickAsync(testTimeout.Token));
        Assert.True(f.Agent.InFlightCanceled);
        Assert.True(await f.Dispatcher.TickAsync(testTimeout.Token));
        Assert.Equal("Reserve Apply Inspect Discard", string.Join(' ', f.Agent.Actions.Select(a => a.Method)));
        Assert.All(f.Agent.Actions, a => Assert.Equal(execution.ExecutionJobId, a.Id));
    }

    [Fact]
    public async Task RetirementWorkerDoesNotReportCompletionWithoutDurableApplyEntry()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        await f.Confirm(await f.Ready());
        f.Agent.PretendReserveCompleted = true;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.Null(f.Store.Row!.ApplyStartedAtUtc);
        Assert.Null(f.Store.Receipt);
        Assert.Equal("Reserve", string.Join(' ', f.Agent.Actions.Select(a => a.Method)));
    }

    [Fact]
    public async Task RetirementWorkerNeverStartsNativeWorkForPendingCancellation()
    {
        using var f = new RetirementFixture();
        await f.Copy();
        await f.Confirm(await f.Ready());
        f.Store.Canceled = true;
        Assert.True(await f.Dispatcher.TickAsync(default));
        Assert.Empty(f.Agent.Actions);
        Assert.Null(f.Store.Row!.RootStartedAtUtc);
        Assert.Null(f.Store.Receipt);
    }

    private sealed class RetirementFixture : IDisposable
    {
        public Fixture CopyFixture { get; } = new();
        public RetirementStore Store { get; }
        public RetirementAgent Agent { get; } = new();
        public ContainerRecycleArchiveRetirementWorkflowService Service { get; }
        public ContainerRecycleArchiveRetirementDispatcher Dispatcher { get; }
        public TerminalActor Actor => CopyFixture.Actor;
        public string Container => CopyFixture.Repository.Row!.ContainerId;
        private string Key { get; } = Guid.NewGuid().ToString("D");

        public RetirementFixture()
        {
            var f = CopyFixture;
            Store = new(f);
            Service = new(Store, f.Repository, f.Protector, f.Protector, Agent,
                new(f.Repository, f.Protector, f.Protector, f.Resolver, f.Service), f.Service, f.Clock);
            Dispatcher = new(Store, Agent, Service, f.Protector, f.Clock);
        }

        public async Task Copy()
        {
            await CopyFixture.Create();
            await CopyFixture.Dispatcher.TickAsync(default);
            var proof = await ResolveRetirement(CopyFixture);
            var a = proof.Binding.Command.Archive.Plan;
            var created = a.Command.Purge.Plan.HistoryCreatedAtUnixMs;
            var hash = new string('B', 64);
            Agent.Plan = new(1, proof.Binding.Command, hash, a.SourceRecordDigest, a.PurgeRecordDigest, a.PurgeMarkerDigest, hash,
                a.HistoryMetadataDigest, created, created + 30L * 86400000, true, true, true, a.StorageHash, a.StoreIdentity,
                a.ArchiveIdentity, a.ArchiveChangedAtUnixNs, a.ArchiveBytes, a.ArchiveSha256, proof.Binding.PublishedIdentity, proof.Binding.PublishedChangedAtUnixNs, hash);
        }
        public Task<ContainerRecycleArchiveRetirementIntentDto> Queue() => Service.QueueAsync(Actor, Container, CopyFixture.Repository.Row!.Id, 30, Key, default);
        public async Task<ContainerRecycleArchiveRetirementIntentDto> Ready()
        {
            var queued = await Queue();
            Assert.True(await Dispatcher.TickAsync(default));
            return await Service.GetAsync(Actor, Container, queued.Id, default);
        }
        public Task<ContainerRecycleArchiveRetirementIntentDto> Confirm(ContainerRecycleArchiveRetirementIntentDto ready) =>
            Service.ConfirmAsync(Actor, Container, ready.Id, ready.Confirmation!.Token, ready.Version, true, default);
        public void Dispose() => CopyFixture.Dispose();
    }

    private sealed class RetirementAgent : IContainerRecycleArchiveRetirementGateway
    {
        public bool Available { get; set; } = true;
        public bool ApplyFailure { get; set; }
        public bool PretendReserveCompleted { get; set; }
        public bool InFlightCanceled { get; private set; }
        public Action? AfterReserve { get; set; }
        public Func<CancellationToken, Task>? InFlight { get; set; }
        public ContainerRecycleArchiveRetirementPlanDto Plan { get; set; } = null!;
        private ContainerRecycleArchiveRetirementReceiptDto? history;
        public List<(string Method, Guid Id)> Actions { get; } = [];
        public Task RequireContainerRecycleArchiveRetirementAsync(CancellationToken cancellationToken) =>
            Available ? Task.CompletedTask : Task.FromException(new IOException("synthetic capability unavailable"));
        public Task<ContainerRecycleArchiveRetirementPlanDto> PlanContainerRecycleArchiveRetirementAsync(TerminalActor actor,
            ContainerRecycleArchiveRetirementCommandDto command, CancellationToken cancellationToken)
        {
            Assert.Equal(Retirement.Write(Plan.Command), Retirement.Write(command));
            return Task.FromResult(Plan);
        }
        public Task<ContainerRecycleArchiveRetirementReceiptDto> ReserveContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto op, CancellationToken token)
        {
            Actions.Add(("Reserve", op.Id));
            history = Make(op, PretendReserveCompleted ? "Completed" : "Reserved");
            AfterReserve?.Invoke();
            return Task.FromResult(history);
        }
        public async Task<ContainerRecycleArchiveRetirementReceiptDto> ApplyContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto op, CancellationToken token)
        {
            Actions.Add(("Apply", op.Id));
            try { if (InFlight is not null) await InFlight(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { InFlightCanceled = true; throw; }
            history = Make(op, "Completed");
            if (ApplyFailure) throw new IOException("synthetic reply loss");
            return history;
        }
        public Task<ContainerRecycleArchiveRetirementReceiptDto?> InspectContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto op, CancellationToken token)
        { Actions.Add(("Inspect", op.Id)); return Task.FromResult(history); }
        public Task<ContainerRecycleArchiveRetirementReceiptDto> DiscardContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto op, CancellationToken token)
        { Actions.Add(("Discard", op.Id)); return Task.FromResult(history = Make(op, "Aborted")); }
        private static ContainerRecycleArchiveRetirementReceiptDto Make(ContainerRecycleArchiveRetirementOperationDto op, string state) =>
            new(op.Id, op.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId, op.Plan.Command.Archive.OperationId, op.Plan.Fingerprint,
                state, state != "Aborted", state != "Aborted", state == "Completed", op.Plan.NotBeforeUnixMs);
    }

    private sealed class RetirementStore(Fixture f) : IContainerRecycleArchiveRetirementRepository
    {
        public ContainerRecycleArchiveRetirementIntent? Row { get; private set; }
        public ContainerRecycleArchiveRetirementReceiptDto? Receipt { get; private set; }
        public string? Code { get; private set; }
        public bool Canceled { get; set; }
        private bool claimed;
        public Task<ContainerRecycleArchiveRetirementIntent?> FindAsync(Guid id, CancellationToken token) => Task.FromResult(Row?.Id == id ? Row : null);
        public Task<ContainerRecycleArchiveRetirementIntent> EnqueueAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) => Task.FromResult(Row = row);
        public Task<ContainerRecycleArchiveRetirementIntent> ConfirmAsync(TerminalActor actor, string confirmation, long version, CancellationToken token)
        {
            var row = Row!;
            var claims = f.Protector.VerifyRetirementConfirmation(confirmation);
            Assert.Equal(row.Id, claims.Id);
            if (row.ExecutionJobId is not null) return Task.FromResult(row);
            row.ExecutionJobId = Retirement.ExecutionId(row.Id);
            row.ConfirmedAtUtc = f.Clock.Now;
            row.ExecutionExpiresAtUtc = f.Clock.Now.AddMinutes(15);
            row.ExecutionHash = f.Protector.FingerprintRetirementExecution(row);
            row.State = "Pending";
            return Task.FromResult(row);
        }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken token) { Canceled = true; return Task.CompletedTask; }
        public Task<ContainerRecycleArchiveRetirementIntent?> ClaimAsync(CancellationToken token)
        {
            if (Row is null || claimed || Row.State is "Ready" or "Done") return Task.FromResult<ContainerRecycleArchiveRetirementIntent?>(null);
            claimed = true;
            Row.LeaseToken = Guid.NewGuid();
            return Task.FromResult<ContainerRecycleArchiveRetirementIntent?>(Row);
        }
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken token) => Task.FromResult(Canceled);
        public Task<bool> MarkPlanningAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) => Task.FromResult(true);
        public Task FinishPlanningAsync(ContainerRecycleArchiveRetirementIntent row, ContainerRecycleArchiveRetirementPlanDto? plan, string code, CancellationToken token)
        {
            claimed = false;
            if (plan is null) { row.State = "Done"; return Task.CompletedTask; }
            row.PlanJson = Retirement.Write(plan);
            row.ReadyExpiresAtUtc = f.Clock.Now.AddMinutes(5);
            row.PlanHash = f.Protector.FingerprintRetirementPlan(row);
            row.State = "Ready";
            return Task.CompletedTask;
        }
        public Task<StartResult> StartAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
        { row.RootStartedAtUtc = f.Clock.Now; return Task.FromResult(StartResult.Started); }
        public Task<bool> ReserveAsync(ContainerRecycleArchiveRetirementIntent row, ContainerRecycleArchiveRetirementReceiptDto receipt, CancellationToken token)
        {
            row.ReservationJson = Retirement.Write(receipt);
            row.ReservationHash = f.Protector.FingerprintRetirementReceipt(Retirement.Operation(row), receipt);
            return Task.FromResult(true);
        }
        public Task<StartResult> EnterApplyAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token)
        { row.ApplyStartedAtUtc = f.Clock.Now; return Task.FromResult(StartResult.Started); }
        public Task<bool> RenewAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) => Task.FromResult(claimed);
        public Task DeferAsync(ContainerRecycleArchiveRetirementIntent row, CancellationToken token) { claimed = false; return Task.CompletedTask; }
        public Task<bool> FinishAsync(ContainerRecycleArchiveRetirementIntent row, string code, ContainerRecycleArchiveRetirementReceiptDto? receipt, CancellationToken token)
        { row.State = "Done"; Code = code; Receipt = receipt; return Task.FromResult(true); }
        public Task AcknowledgeAsync(TerminalActor actor, Guid id, long version, ContainerRecycleArchiveRetirementReceiptDto observation, CancellationToken token) => throw new NotSupportedException();
    }
}
