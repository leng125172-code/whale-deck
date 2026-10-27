using System.Runtime.CompilerServices;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerUploadServiceTests
{
    private static ContainerHistoryEntryDto Source(Fixture fixture) => new(Guid.NewGuid().ToString("N"), fixture.Command.Location.ContainerId,
        "666:2026-10-26T01:02:03.123456789Z", fixture.Command.Location.Path, new('C',64), Fixture.Bytes.Length,
        fixture.Command.Sha256, fixture.Clock.Now.AddDays(-1).ToUnixTimeMilliseconds(), false, "Present", new('D',64));
    private static ContainerFileRestoreService Restores(Fixture fixture, Catalog catalog) =>
        new(fixture.Service, fixture.Gateway, fixture.History, catalog, fixture.Protector, fixture.Repository, fixture.Clock);
    private static ContainerFileRestoreCommand RestoreCommand(Fixture fixture, ContainerHistoryEntryDto source) =>
        new(fixture.Command.Location, source.CaptureId, source.CaptureHash, "ABSENT");
    private static ContainerFileUploadDispatcher RestoreDispatcher(Fixture fixture) =>
        new(fixture.Repository, fixture.Gateway, fixture.Service, fixture.Protector, fixture.Clock, fixture.History);
    private static async Task<ContainerFileUploadIntentDto> CreateRestore(Fixture fixture)
    {
        fixture.Gateway.RecordStageReceipt = true;
        var source = Source(fixture); var service = Restores(fixture, new() { Entry = source });
        var plan = await service.PlanAsync(fixture.Actor, RestoreCommand(fixture, source), default);
        return await service.SubmitAsync(fixture.Actor, source.ContainerId, new(plan.PlanHash, true, Guid.NewGuid().ToString("D")), default);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("logout")]
    [InlineData("directory-unavailable")]
    public async Task RestoreCannotReadCatalogOrSourceWithoutCurrentAuthority(string failure)
    {
        using var fixture = new Fixture(); var source = Source(fixture); var catalog = new Catalog { Entry = source };
        fixture.Identity.Admin = failure != "ordinary"; fixture.Logins.Active = failure != "logout"; fixture.Identity.Unavailable = failure == "directory-unavailable";
        await Assert.ThrowsAnyAsync<Exception>(() => Restores(fixture, catalog).PlanAsync(fixture.Actor, RestoreCommand(fixture, source), default));
        Assert.Equal(0,catalog.Finds); Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.History.Reads);
    }

    [Theory]
    [InlineData("Incomplete")]
    [InlineData("Missing")]
    [InlineData("Invalid")]
    public async Task UnavailableSourceCannotCreateAPlanOrInspectTarget(string availability)
    {
        using var fixture = new Fixture(); var source = Source(fixture) with { Availability = availability };
        await Assert.ThrowsAnyAsync<Exception>(() => Restores(fixture,new() { Entry = source }).PlanAsync(fixture.Actor,RestoreCommand(fixture,source),default));
        Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task CrossPathCaptureAndChangedCaptureHashAreRejectedBeforeTargetInspection()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var command = RestoreCommand(fixture,source);
        var catalog = new Catalog { Entry = source with { Path = "another/file.bin" } }; var service = Restores(fixture,catalog);
        await Assert.ThrowsAnyAsync<Exception>(()=>service.PlanAsync(fixture.Actor,command,default));
        catalog.Entry = source with { CaptureHash = new('E',64) };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.PlanAsync(fixture.Actor,command,default));
        Assert.Equal(0,fixture.Gateway.Inspections);
    }
    [Fact]
    public async Task MissingHistoryCapabilityCannotReserveAnIntent()
    {
        using var fixture = new Fixture(); var source = Source(fixture); fixture.History.Unavailable = true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Restores(fixture,new(){Entry=source}).PlanAsync(fixture.Actor,RestoreCommand(fixture,source),default));
        Assert.Equal(0,fixture.Gateway.Inspections); Assert.Equal(0,fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task RestorationAndUploadPlansCannotBeSubstitutedAndPriorLoginCannotBeChanged()
    {
        using var fixture = new Fixture(); var source = Source(fixture); var service = Restores(fixture,new(){Entry=source});
        var plan = await service.PlanAsync(fixture.Actor,RestoreCommand(fixture,source),default); var upload = await fixture.Plan();
        Assert.Throws<ArgumentException>(()=>fixture.Protector.VerifyContainerUpload(plan.PlanHash));
        Assert.Throws<ArgumentException>(()=>fixture.Protector.VerifyContainerRestore(upload.PlanHash));
        var submit = new SubmitFileUploadCommand(plan.PlanHash,true,Guid.NewGuid().ToString("D"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SubmitAsync(fixture.Actor with {LoginBinding=new('F',64)},source.ContainerId,submit,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SubmitAsync(fixture.Actor,new('b',64),submit,default));
        Assert.Equal(0,fixture.Repository.Enqueued);
    }
    [Fact]
    public async Task ExactRestoreRetryDoesNotExtendItsLifetimeAndBrowserCannotSupplyItsBytes()
    {
        using var fixture = new Fixture(); var source=Source(fixture); var service=Restores(fixture,new(){Entry=source});
        var plan=await service.PlanAsync(fixture.Actor,RestoreCommand(fixture,source),default);
        var submit=new SubmitFileUploadCommand(plan.PlanHash,true,Guid.NewGuid().ToString("D"));
        var first=await service.SubmitAsync(fixture.Actor,source.ContainerId,submit,default); fixture.Clock.Now=fixture.Clock.Now.AddMinutes(6);
        Assert.Equal(first,await service.SubmitAsync(fixture.Actor,source.ContainerId,submit,default)); Assert.Equal(1,fixture.Repository.Enqueued);
        using var body=new MemoryStream(Fixture.Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.ReceiveAsync(fixture.Actor,source.ContainerId,first.Id,body,Fixture.Bytes.Length,_=>Task.CompletedTask,default));
        Assert.Equal(0,body.Position); Assert.Equal(0,fixture.Gateway.Stages); Assert.True(ContainerFileUploadService.HasValidBinding(fixture.Repository.Row!,fixture.Protector));
    }
    [Fact]
    public async Task CompleteSourcePublishesOnceThroughExistingProtectionAndNeverBrowserBytes()
    {
        using var fixture=new Fixture(); await CreateRestore(fixture);
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.Equal("Succeeded",Assert.Single(fixture.Repository.Results).State); Assert.Equal(Fixture.Bytes,fixture.Gateway.Received);
        Assert.Equal(1,fixture.History.Reads); Assert.Equal(1,fixture.Gateway.Stages); Assert.Equal(1,fixture.Gateway.Publications);
        Assert.True(fixture.History.Complete); Assert.NotNull(fixture.Repository.Row!.RootStartedAtUtc); Assert.False(fixture.Repository.Row.PublishBarrier);
        Assert.False(await RestoreDispatcher(fixture).TickAsync(default)); Assert.Equal(1,fixture.Gateway.Publications);
    }
    [Fact]
    public async Task SourceFailureAfterLastChunkNeverCommitsStageAndIsFencedWithoutRereading()
    {
        using var fixture=new Fixture(); await CreateRestore(fixture); fixture.History.FailAfterBytes=true;
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.Equal("Receiving",fixture.Repository.Row!.State); Assert.Empty(fixture.Repository.Results); Assert.Equal(0,fixture.Gateway.Publications);
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.Equal("Failed",Assert.Single(fixture.Repository.Results).State); Assert.Equal(1,fixture.History.Reads);
        Assert.Equal(1,fixture.Gateway.Stages); Assert.Equal(0,fixture.Gateway.Publications); Assert.Equal(1,fixture.Gateway.Preventions);
    }
    [Fact]
    public async Task WorkerRestartAfterPositiveStageObservesReceiptWithoutReadingSourceAgain()
    {
        using var fixture=new Fixture(); await CreateRestore(fixture);
        fixture.Repository.Row!.State="Receiving"; fixture.Repository.Row.ReceiveToken=Guid.NewGuid(); fixture.Gateway.Receipt=Receipt(fixture,"Staged");
        Assert.True(await RestoreDispatcher(fixture).TickAsync(default));
        Assert.Equal("Succeeded",Assert.Single(fixture.Repository.Results).State); Assert.Equal(0,fixture.History.Reads); Assert.Equal(0,fixture.Gateway.Stages); Assert.Equal(1,fixture.Gateway.Publications);
    }
    [Fact]
    public async Task LogoutAfterSourceCannotMarkReadyAndCancellationWinsBeforeSource()
    {
        using(var fixture=new Fixture())
        {
            await CreateRestore(fixture); fixture.History.AfterBytes=()=>fixture.Logins.Active=false;
            Assert.True(await RestoreDispatcher(fixture).TickAsync(default)); Assert.Equal(0,fixture.Repository.ReadyCalls); Assert.Equal(0,fixture.Gateway.Publications);
            Assert.True(await RestoreDispatcher(fixture).TickAsync(default)); Assert.Equal("Canceled",Assert.Single(fixture.Repository.Results).State); Assert.Equal(1,fixture.History.Reads);
        }
        using(var fixture=new Fixture())
        {
            await CreateRestore(fixture); fixture.Repository.CancelRequested=true;
            Assert.True(await RestoreDispatcher(fixture).TickAsync(default)); Assert.Equal("Canceled",Assert.Single(fixture.Repository.Results).State);
            Assert.Equal(0,fixture.History.Reads); Assert.Equal(0,fixture.Gateway.Stages); Assert.Equal(0,fixture.Gateway.Publications);
        }
    }
    [Fact]
    public async Task LegacyBindingAndFrozenRecoverySnapshotOmitNullSourceExactly()
    {
        using var fixture=new Fixture();await fixture.Create();var row=fixture.Repository.Row!;var b=ContainerFileUploadIntentValidation.ReadBinding(row);
        var legacy=JsonSerializer.Serialize(new {b.Id,b.IdempotencyKey,b.Actor,b.LoginBinding,b.TraceId,b.Target,b.SizeBytes,b.Sha256,b.PlanHash,b.CreatedAtUtc,b.ExpiresAtUtc});
        Assert.Equal(legacy,JsonSerializer.Serialize(b));Assert.DoesNotContain("HistorySourceJson",JsonSerializer.Serialize(row),StringComparison.Ordinal);
    }
    private sealed class Catalog : IContainerFileHistoryRepository
    {
        public ContainerHistoryEntryDto? Entry {get;set;}
        public int Finds {get;private set;}
        public Task ObserveAsync(TerminalActor actor,ContainerHistoryPageDto page,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<ContainerHistoryEntryDto?> FindAsync(string containerId,string captureId,CancellationToken cancellationToken)
        {Finds++;return Task.FromResult(Entry);}
    }
    private sealed class HistoryGateway : IContainerFileHistoryGateway
    {
        public int Reads {get;private set;}
        public bool Complete {get;private set;}
        public bool FailAfterBytes {get;set;}
        public bool Unavailable {get;set;}
        public Action? AfterBytes {get;set;}
        public Task RequireContainerHistoryWorkflowAsync(CancellationToken cancellationToken)=>Unavailable?Task.FromException(new InvalidOperationException("CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE")):Task.CompletedTask;
        public Task<ContainerHistoryPageDto> ListContainerHistoryAsync(ContainerFileLocationDto location,string after,int take,string expectedVersion,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadContainerHistorySourceAsync(ContainerFileLocationDto location,ContainerHistoryEntryDto expected,[EnumeratorCancellation]CancellationToken cancellationToken)
        {
            Reads++;await Task.Yield();cancellationToken.ThrowIfCancellationRequested();yield return Fixture.Bytes;
            AfterBytes?.Invoke();if(FailAfterBytes)throw new IOException("owned-source-validation-failure-after-last-chunk");Complete=true;
        }
    }
}
