using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerUploadServiceTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OrdinaryOrRevokedLoginCannotInspectOrCreateAnUpload(bool administrator, bool active)
    {
        using var fixture = new Fixture(); fixture.Identity.Admin = administrator; fixture.Logins.Active = active;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.PlanAsync(fixture.Actor, fixture.Command, default));
        Assert.Equal(0, fixture.Gateway.Inspections); Assert.Equal(0, fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task MissingWorkflowCannotInspectPlanCreateNewIntentOrBeginReceiving()
    {
        using var fixture = new Fixture(); fixture.Gateway.WorkflowUnavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Plan());
        Assert.Equal(0, fixture.Gateway.Inspections); Assert.Equal(0, fixture.Repository.Enqueued);
        fixture.Gateway.WorkflowUnavailable = false; var plan = await fixture.Plan();
        fixture.Gateway.WorkflowUnavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitAsync(fixture.Actor, fixture.Command.Location.ContainerId,
            new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default));
        Assert.Equal(1, fixture.Gateway.Inspections); Assert.Equal(0, fixture.Repository.Enqueued);
        fixture.Gateway.WorkflowUnavailable = false; var intent = await fixture.Create();
        fixture.Gateway.WorkflowUnavailable = true; using var bytes = new MemoryStream(Fixture.Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReceiveAsync(fixture.Actor, fixture.Command.Location.ContainerId,
            intent.Id, bytes, Fixture.Bytes.Length, _ => Task.CompletedTask, default));
        Assert.Equal("AwaitingUpload", fixture.Repository.Row!.State);
        Assert.Equal(0, bytes.Position); Assert.Equal(0, fixture.Gateway.Stages); Assert.Equal(0, fixture.Repository.ReadyCalls);
        // Read/cancel remain available after a downgrade; existing intents
        // must not disappear just because no new bytes may be accepted.
        Assert.Equal(intent, await fixture.Service.GetAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, default));
        await fixture.Service.CancelAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, default);
        Assert.True(fixture.Repository.CancelRequested);
    }

    [Fact]
    public async Task PlanIsBoundToOriginalLoginAndAChangedTargetNeedsANewPlan()
    {
        using var fixture = new Fixture(); var plan = await fixture.Plan();
        var command = new SubmitFileUploadCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SubmitAsync(fixture.Actor with { LoginBinding = new('F',64) }, fixture.Command.Location.ContainerId, command, default));
        fixture.Gateway.Target = fixture.Gateway.Target with { ExpectedParentVersion = new('F',64) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitAsync(fixture.Actor, fixture.Command.Location.ContainerId, command, default));
        Assert.Equal(0, fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task APlanExpiringDuringInspectionCannotAcquireAPermanentIntent()
    {
        using var fixture = new Fixture(); var plan = await fixture.Plan();
        fixture.Gateway.AfterInspection = () => fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitAsync(fixture.Actor, fixture.Command.Location.ContainerId,
            new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default));
        Assert.Equal(0, fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task RouteContainerCannotRedirectPlanIntentReceiveOrCancellation()
    {
        using var fixture = new Fixture(); var plan = await fixture.Plan(); var wrong = new string('b',64);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SubmitAsync(fixture.Actor, wrong,
            new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default));
        Assert.Equal(0, fixture.Repository.Enqueued);
        var intent = await fixture.Create(); using var bytes = new MemoryStream(Fixture.Bytes);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GetAsync(fixture.Actor, wrong, intent.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.CancelAsync(fixture.Actor, wrong, intent.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.ReceiveAsync(fixture.Actor, wrong, intent.Id, bytes, Fixture.Bytes.Length, _ => Task.CompletedTask, default));
        Assert.Equal(0, fixture.Gateway.Stages); Assert.Equal(0, fixture.Repository.ReadyCalls);
    }
    [Fact]
    public async Task ExactRetryReturnsTheSameIntentWithoutAnotherInspectionOrExtendingItsLife()
    {
        using var fixture = new Fixture(); var plan = await fixture.Plan();
        var command = new SubmitFileUploadCommand(plan.PlanHash, true, Guid.NewGuid().ToString("D"));
        var first = await fixture.Service.SubmitAsync(fixture.Actor, fixture.Command.Location.ContainerId, command, default);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        var again = await fixture.Service.SubmitAsync(fixture.Actor, fixture.Command.Location.ContainerId, command, default);
        Assert.Equal(first, again); Assert.Equal(1, fixture.Repository.Enqueued); Assert.Equal(2, fixture.Gateway.Inspections);
        Assert.True(ContainerFileUploadService.HasValidBinding(fixture.Repository.Row!, fixture.Protector));
        Assert.Equal(fixture.Clock.Now.AddMinutes(9), first.ExpiresAtUtc);
    }
    [Fact]
    public async Task CompleteBytesAreOnlyStagedAndMarkedReadyAfterFinalLoginCheck()
    {
        using var fixture = new Fixture(); var intent = await fixture.Create();
        using var bytes = new MemoryStream(Fixture.Bytes);
        var result = await fixture.Service.ReceiveAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, bytes, Fixture.Bytes.Length, _ => Task.CompletedTask, default);
        Assert.Equal("Ready", result.State); Assert.Equal(1, fixture.Gateway.Stages); Assert.Equal(1, fixture.Repository.ReadyCalls);
        Assert.Equal(Fixture.Bytes, fixture.Gateway.Received); Assert.False(fixture.Repository.Row!.PublishBarrier);
        Assert.Null(fixture.Repository.Row.RootStartedAtUtc);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationAtEndOfStreamCannotMarkBytesReady(bool revokeCookieCallback)
    {
        using var fixture = new Fixture(); var intent = await fixture.Create(); var staged = false;
        fixture.Gateway.AfterStage = () => { staged = true; if (!revokeCookieCallback) fixture.Logins.Active = false; };
        Task Validate(CancellationToken _) => staged && revokeCookieCallback ? Task.FromException(new UnauthorizedAccessException("expired")) : Task.CompletedTask;
        using var bytes = new MemoryStream(Fixture.Bytes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ReceiveAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, bytes, Fixture.Bytes.Length, Validate, default));
        Assert.Equal(1, fixture.Gateway.Stages); Assert.Equal(0, fixture.Repository.ReadyCalls);
    }
    [Theory]
    [InlineData("Published")]
    [InlineData("Unknown")]
    [InlineData("wrong-id")]
    [InlineData("wrong-digest")]
    public async Task PrematureOrMismatchedRootOutputDoesNotMarkBytesReady(string output)
    {
        using var fixture = new Fixture(); var intent = await fixture.Create();
        fixture.Gateway.Output = output;
        using var bytes = new MemoryStream(Fixture.Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReceiveAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, bytes, Fixture.Bytes.Length, _ => Task.CompletedTask, default));
        Assert.Equal(0, fixture.Repository.ReadyCalls);
    }
    [Fact]
    public async Task PeriodicRevocationCancelsTheSingleInFlightReceive()
    {
        using var fixture = new Fixture(); var intent = await fixture.Create(); using var bytes = new BlockingStream();
        var receiving = fixture.Service.ReceiveAsync(fixture.Actor, fixture.Command.Location.ContainerId, intent.Id, bytes, Fixture.Bytes.Length, _ => Task.CompletedTask, default);
        await bytes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); fixture.Logins.Active = false;
        await fixture.Clock.AdvanceAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receiving.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fixture.Gateway.Stages); Assert.Equal(0, fixture.Repository.ReadyCalls);
    }
    private sealed class Fixture : IDisposable
    {
        public static readonly byte[] Bytes = [1, 2, 3];
        public FileUploadLeaseTests.StepClock Clock { get; } = new() { Now = ContainerFileUploadIntentValidation.CanonicalTime(DateTimeOffset.UtcNow) };
        public TerminalActor Actor { get; } = new("container-service-admin", null, [], new('A',64), "container-service-trace", null);
        public Registry Logins { get; } = new();
        public Directory Identity { get; }
        public Gateway Gateway { get; } = new();
        public Repository Repository { get; } = new();
        public FilePlanProtector Protector { get; }
        public ContainerFileUploadService Service { get; }
        public ContainerFileUploadCommand Command => new(Gateway.Target.Location, Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)), "ABSENT");
        private readonly string _key = Path.GetTempFileName();
        public Fixture()
        {
            File.WriteAllBytes(_key, RandomNumberGenerator.GetBytes(32));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
            Identity = new(Clock);
            var authorization = new FileManagementService(DispatchProxy.Create<IFileGateway, Unused>(), DispatchProxy.Create<IFileUploadRepository, Unused>(), Protector, Identity, Clock);
            Service = new(authorization, Logins, Gateway, Repository, Protector, Clock);
        }
        public Task<FileUploadPlanDto> Plan() => Service.PlanAsync(Actor, Command, default);
        public async Task<ContainerFileUploadIntentDto> Create() => await Service.SubmitAsync(Actor, Command.Location.ContainerId, new((await Plan()).PlanHash, true, Guid.NewGuid().ToString("D")), default);
        public void Dispose() { Clock.Dispose(); File.Delete(_key); }
    }
    public class Unused : DispatchProxy
    { protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(); }
    private sealed class Directory(TimeProvider clock) : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public bool Unavailable { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken) =>
            Unavailable ? Task.FromException<CurrentUserDto>(new IOException("directory-unavailable")) :
                Task.FromResult(new CurrentUserDto(subject, name, groups, Admin, clock.GetUtcNow().AddHours(2)));
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Registry : ILoginSessionRegistry
    {
        public bool Active { get; set; } = true;
        public bool Unavailable { get; set; }
        public Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken) =>
            Unavailable ? Task.FromException<bool>(new IOException("cache-unavailable")) : Task.FromResult(Active);
    }
    private sealed class Gateway : IContainerFileWriteGateway
    {
        public bool WorkflowUnavailable { get; set; }
        public Task RequireContainerUploadWorkflowAsync(CancellationToken cancellationToken) => WorkflowUnavailable
            ? Task.FromException(new InvalidOperationException("CONTAINER_UPLOAD_CAPABILITY_UNAVAILABLE")) : Task.CompletedTask;
        public int Inspections { get; private set; }
        public int Stages { get; private set; }
        public byte[] Received { get; private set; } = [];
        public string? Output { get; set; }
        public Action? AfterInspection { get; set; }
        public Action? AfterStage { get; set; }
        public int UploadInspections { get; private set; }
        public int Publications { get; private set; }
        public int Preventions { get; private set; }
        public bool PreventUnavailable { get; set; }
        public bool RejectFenceOutput { get; set; }
        public Action? BeforePrevent { get; set; }
        public ContainerFileUploadReceiptDto? Receipt { get; set; }
        public Func<CancellationToken, Task<ContainerFileUploadReceiptDto>>? PublishHandler { get; set; }
        public ContainerFileUploadTargetDto Target { get; set; } = new(new(new('a',64), "777:2026-10-27T01:02:03.123456789Z", "private/file.bin"), new('B',64), new('C',64), "ABSENT", 0);
        public Task<ContainerFileUploadTargetDto> InspectContainerUploadTargetAsync(ContainerFileLocationDto location, CancellationToken cancellationToken)
        { Inspections++; AfterInspection?.Invoke(); return Task.FromResult(Target); }
        public async Task<ContainerFileUploadReceiptDto> StageContainerFileAsync(ContainerFileStageDto request, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken)
        {
            Stages++; using var body = new MemoryStream();
            await foreach (var part in bytes.WithCancellation(cancellationToken)) await body.WriteAsync(part, cancellationToken);
            Received = body.ToArray(); Assert.Equal(request.Sha256, Convert.ToHexString(SHA256.HashData(Received)));
            AfterStage?.Invoke();
            return new(request.Location.ContainerId, request.Location.InstanceId, Output == "wrong-id" ? Guid.NewGuid() : request.OperationId, request.ExpectedTargetProof,
                new(Output is "Published" or "Unknown" ? Output : "Staged", request.SizeBytes, Output == "wrong-digest" ? new('F',64) : request.Sha256, "", false, false, false), false);
        }
        public Task<ContainerFileUploadReceiptDto> PublishContainerFileAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken)
        {
            Publications++;
            if (PublishHandler is not null) return PublishHandler(cancellationToken);
            var current = Receipt ?? throw new InvalidOperationException("missing-fixture-receipt");
            Receipt = current with { Upload = current.Upload with { State = "Published", Version = new('F',64), BindingCurrent = true } };
            return Task.FromResult(Receipt);
        }
        public Task<ContainerFileUploadReceiptDto?> InspectContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken)
        { UploadInspections++; return Task.FromResult(Receipt); }
        public Task<ContainerFileUploadReceiptDto> DiscardContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerFileUploadReceiptDto> PreventContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken)
        {
            Preventions++;
            if (PreventUnavailable) throw new IOException("fence-unavailable");
            BeforePrevent?.Invoke();
            if (RejectFenceOutput) throw new InvalidOperationException("CONTAINER_UPLOAD_OUTPUT_INVALID");
            if (Receipt?.Upload.State == "Published") return Task.FromResult(Receipt);
            Receipt = new(request.ContainerId, request.InstanceId, request.OperationId, request.ExpectedTargetProof,
                new(Receipt is null ? "Prevented" : "Discarded", Receipt?.Upload.SizeBytes ?? 0,
                    Receipt?.Upload.Sha256 ?? Convert.ToHexString(SHA256.HashData([])), "", false, false, false), false);
            return Task.FromResult(Receipt);
        }
    }
    private sealed class Repository : IContainerFileUploadRepository
    {
        public ContainerFileUploadOperation? Row { get; set; }
        public int Enqueued { get; private set; }
        public int ReadyCalls { get; private set; }
        public bool CancelRequested { get; set; }
        public bool LeaseAvailable { get; set; } = true;
        public bool RejectPublicationProof { get; set; }
        public int Deferred { get; private set; }
        public bool Claimed { get; private set; }
        public Action? AfterStarted { get; set; }
        public ContainerFileUploadStartResult StartResult { get; set; } = ContainerFileUploadStartResult.Started;
        public List<(string State, string Code)> Results { get; } = [];
        public Task<ContainerFileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken) => Task.FromResult(Row is { } row && row.Id == id && row.ActorSubject == actor.Subject && row.LoginBinding == actor.LoginBinding ? row : null);
        public Task<ContainerFileUploadOperation> EnqueueAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) { Enqueued++; Row = row; return Task.FromResult(row); }
        public Task<ContainerFileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
        { Row!.State = "Receiving"; Row.ReceiveToken = Guid.NewGuid(); return Task.FromResult(Row); }
        public Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Row!.State == "Receiving");
        public Task<bool> ReadyAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) { ReadyCalls++; Row!.State = "Ready"; return Task.FromResult(true); }
        public Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken)
        { Assert.Equal(Row!.Id, id); Assert.Equal(Row.ActorSubject, actor.Subject); Assert.Equal(Row.LoginBinding, actor.LoginBinding); CancelRequested = true; return Task.CompletedTask; }
        public Task<ContainerFileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (Claimed || Row is null || Row.CompletedAtUtc is not null || Row.State == "Unknown") return Task.FromResult<ContainerFileUploadOperation?>(null);
            Claimed = true; Row.LeaseToken = Guid.NewGuid(); Row.LeaseExpiresAtUtc = now.AddSeconds(90); return Task.FromResult<ContainerFileUploadOperation?>(Row);
        }
        public Task<bool> RenewAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(LeaseAvailable);
        public Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(CancelRequested);
        public Task<ContainerFileUploadStartResult> StartedAsync(ContainerFileUploadOperation row, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (StartResult != ContainerFileUploadStartResult.Started) return Task.FromResult(StartResult);
            Row!.State = "Publishing"; Row.PublishBarrier = true; Row.RootStartedAtUtc = now; AfterStarted?.Invoke(); return Task.FromResult(StartResult);
        }
        public Task<bool> FinishAsync(ContainerFileUploadOperation row, string state, string code, ContainerFileUploadReceiptDto? receipt, CancellationToken cancellationToken)
        {
            if (RejectPublicationProof && state is "Succeeded" or "Superseded") throw new ArgumentException("invalid-proof");
            Results.Add((state, code)); Row!.State = state; Claimed = false;
            if (state != "Unknown") { Row.CompletedAtUtc = DateTimeOffset.UtcNow; Row.PublishBarrier = false; }
            return Task.FromResult(true);
        }
        public Task DeferAsync(ContainerFileUploadOperation row, CancellationToken cancellationToken) { Deferred++; Claimed = false; return Task.CompletedTask; }
    }
    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
