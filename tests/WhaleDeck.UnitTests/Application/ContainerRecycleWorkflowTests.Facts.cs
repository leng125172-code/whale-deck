using System.Text.Json;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecycleWorkflowTests
{
    [Theory][InlineData("null")][InlineData("attrs")][InlineData("links")]
    public async Task MalformedPlanOutputFailsPromptlyWithoutRetryOrMutation(string field)
    {
        using var f = new Fixture(); await f.Queue();
        f.Gateway.PlanValue = field switch {
            "null" => null!, "attrs" => f.Gateway.PlanValue with { Attributes = null! },
            _ => f.Gateway.PlanValue with { SourceIdentity = f.Gateway.PlanValue.SourceIdentity with { LinkCount = 2 } }
        };
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Failed", f.Repository.Row!.State); Assert.Equal("CONTAINER_RECYCLE_OUTPUT_INVALID", f.Repository.Row.ResultCode);
        Assert.Equal(0, f.Repository.Deferrals); Assert.Equal(0, f.Gateway.Applies); Assert.Null(f.Repository.Row.PlanJson);
    }

    [Fact]
    public async Task RevocationDuringAcknowledgementLookupPreventsRootFenceCall()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        f.Repository.AfterFind = () => f.Identity.LiveAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default));
        Assert.Equal(0, f.Gateway.Discards); Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier);
    }

    [Fact]
    public async Task MutableAgentAttributeBuffersAreFrozenBeforePostScanAuthorityAwait()
    {
        using var f = new Fixture(); await f.Queue();
        f.Gateway.AfterPlan = () => f.Identity.OnLiveRead = () => {
            f.Gateway.PlanValue.Attributes[0].Value[0] = 123; f.Identity.OnLiveRead = null;
        };
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Ready", f.Repository.Row!.State);
        Assert.Equal(0, ContainerRecycleWorkflowValidation.ReadPlan(f.Repository.Row).Attributes[0].Value[0]);
        Assert.Equal(123, f.Gateway.PlanValue.Attributes[0].Value[0]);
        Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(f.Repository.Row, f.Protector));
    }

    [Fact]
    public async Task AlreadyRestoredRootIsNotFreshTrashSuccessOrAutomaticRestoration()
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.Receipt = Gateway.ReceiptFor(ContainerRecycleWorkflowValidation.Reference(f.Repository.Row!), "Restored");
        await f.Dispatcher.TickAsync(default);
        var result = await f.Get(); Assert.Equal("Superseded", result.State); Assert.Equal("CONTAINER_RECYCLE_ALREADY_RESTORED", result.ResultCode);
        Assert.True(result.Receipt!.SourceRestored); Assert.False(result.Receipt.SourceRetained);
        Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards);
    }

    [Theory][InlineData("Trashed")][InlineData("Aborted")][InlineData("Unknown")]
    public async Task IdentityOutageDoesNotEraseAlreadyObservedRootFact(string state)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.Receipt = Gateway.ReceiptFor(ContainerRecycleWorkflowValidation.Reference(f.Repository.Row!), state);
        f.Identity.LiveUnavailable = true;
        await f.Dispatcher.TickAsync(default);
        Assert.Equal(state, f.Repository.Row!.RootState);
        Assert.NotNull(f.Repository.Row.ReceiptHash); Assert.Equal(0, f.Gateway.Applies);
        Assert.Equal(state == "Unknown", f.Repository.Row.CompletedAtUtc is null);
        Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(f.Repository.Row, f.Protector));
    }

    [Theory][InlineData("ReceiptJson")][InlineData("ReceiptHash")][InlineData("SourceRetained")]
    [InlineData("SourceRestored")][InlineData("RootState")][InlineData("HistoryAvailable")][InlineData("HistoryDigest")]
    public async Task SavedReceiptAndProjectedFactsCannotBeSilentlyRewritten(string field)
    {
        using var f = new Fixture(); await f.Create(); await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        var property = typeof(ContainerRecycleIntent).GetProperty(field)!; var prior = property.GetValue(row)!;
        property.SetValue(row, prior is bool b ? !b : prior is string text ? text + "x" : throw new InvalidOperationException());
        Assert.False(ContainerRecycleWorkflowValidation.HasValidBinding(row, f.Protector));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get());
    }

    [Fact]
    public async Task CoordinatedValidReceiptRewriteStillRequiresIndependentReceiptMac()
    {
        using var f = new Fixture(); await f.Create(); await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        var receipt = ContainerRecycleWorkflowValidation.ReadReceipt(row)! with { HistoryAvailable = false, BindingCurrent = false };
        row.ReceiptJson = ContainerRecycleWorkflowValidation.Serialize(receipt); row.HistoryAvailable = false; row.BindingCurrent = false; row.State = "Superseded";
        Assert.False(ContainerRecycleWorkflowValidation.HasValidBinding(row, f.Protector));
    }

    [Fact]
    public async Task UnknownAcknowledgementCannotTurnUnprovedRemovalIntoAbortedOrSuccess()
    {
        using var f = new Fixture(); await f.Create(); f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown");
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!; var original = row.ReceiptJson;
        f.Gateway.Receipt = Gateway.ReceiptFor(ContainerRecycleWorkflowValidation.Reference(row), "Aborted");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default));
        Assert.Equal("Unknown", row.State); Assert.True(row.PublishBarrier); Assert.Equal(original, row.ReceiptJson);
        Assert.Null(row.AcknowledgedBy); Assert.Null(row.SourceRetained); Assert.Null(row.SourceRestored);
    }

    [Fact]
    public async Task HistoryUnavailableObservationKeepsPriorVerifiedHistoryOnAcknowledgement()
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => Gateway.ReceiptFor(op, "Unknown") with { HistoryVerified = true, HistoryDigest = new('F',64), HistoryAvailable = true };
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        f.Gateway.Receipt = f.Gateway.Receipt! with { HistoryAvailable = false, Replayed = true };
        await f.Service.AcknowledgeAsync(f.Actor, row.Id, new(row.Version, true), default);
        Assert.Equal("Acknowledged", row.State); Assert.Equal("Unknown", row.RootState); Assert.True(row.HistoryVerified);
        Assert.False(row.HistoryAvailable); Assert.Equal(new string('F',64), row.HistoryDigest);
        Assert.True(ContainerRecycleWorkflowValidation.HasValidBinding(row, f.Protector));
    }
}
