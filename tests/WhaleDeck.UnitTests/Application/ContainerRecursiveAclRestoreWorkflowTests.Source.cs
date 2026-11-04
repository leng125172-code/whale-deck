using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerRecursiveAclRestoreWorkflowTests
{
    [Fact]
    public async Task AgentSourceFrameIsCapturedBeforeThePostRpcLeaseAwait()
    {
        using var f = new Fixture(); await f.Queue(); var original = f.Gateway.PlanValue.RestoreSourceFrame; var renewals = 0;
        f.Repository.AfterRenew = () => { if (++renewals == 2) typeof(ContainerRecursiveAclRestoreFrameDto).GetProperty(nameof(original.OwnerUid))!.SetValue(original, 1U); };
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Ready", f.Repository.Row!.State); Assert.Equal(1U, original.OwnerUid);
        var plan = ContainerRecursiveAclRestoreWorkflowValidation.ReadPlan(f.Repository.Row);
        Assert.Equal(0U, plan.RestoreSourceFrame.OwnerUid); Assert.NotSame(original, plan.RestoreSourceFrame);
        Assert.True(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(f.Repository.Row, f.Protector));
        Assert.Equal(0, f.Gateway.Applies);
    }

    [Theory]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND")]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED")]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_INVALID")]
    [InlineData("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_TREE_MISMATCH")]
    public async Task KnownPlanningSourceRefusalFinishesWithoutInventingAReadyPlan(string code)
    {
        using var f = new Fixture(); await f.Queue(); f.Gateway.PlanFailure = new ContainerRecursiveAclRestorePreflightRejectedException(code);
        await f.Dispatcher.TickAsync(default);
        Assert.Equal("Failed", f.Repository.Row!.State); Assert.Equal(code, f.Repository.Row.ResultCode); Assert.NotNull(f.Repository.Row.CompletedAtUtc);
        Assert.Null(f.Repository.Row.PlanJson); Assert.Null(f.Repository.Row.ExecutionJobId); Assert.Equal(0, f.Gateway.Applies); Assert.Equal(0, f.Gateway.Discards);
    }

    [Theory]
    [InlineData(null, "Failed")][InlineData("Partial", "Partial")][InlineData("Unknown", "Unknown")][InlineData("Completed", "Succeeded")]
    public async Task SourceFenceFailureObservesOriginalFactsBeforeChoosingDiscard(string? actualRoot, string expected)
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = op => {
            if (actualRoot is not null) f.Gateway.Receipt = Gateway.ReceiptFor(op, actualRoot);
            throw new ContainerRecursiveAclRestorePreflightRejectedException("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED");
        };
        await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        Assert.Equal(1, f.Gateway.Applies); Assert.Equal(2, f.Gateway.Inspects); Assert.Equal(actualRoot is null ? 1 : 0, f.Gateway.Discards);
        Assert.Equal(expected, row.State); Assert.Equal(actualRoot is null ? "Aborted" : actualRoot, row.RootState);
        Assert.Equal(expected is "Partial" or "Unknown", row.PublishBarrier);
        Assert.Equal(expected == "Partial" ? 1 : expected == "Succeeded" ? 4 : 0, row.ChangedCount);
        if (actualRoot is null) Assert.Equal("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_CHANGED", row.ResultCode);
        await f.Dispatcher.TickAsync(default); Assert.Equal(1, f.Gateway.Applies);
        Assert.Equal(row.ExecutionJobId, f.Gateway.LastOperation!.Id);
    }

    [Fact]
    public async Task SourceRefusalCannotReleaseProtectionWhenOriginalRootFenceIsUnavailable()
    {
        using var f = new Fixture(); await f.Create();
        f.Gateway.ApplyHandler = _ => throw new ContainerRecursiveAclRestorePreflightRejectedException("CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_NOT_FOUND");
        f.Gateway.DiscardFailure = new IOException("synthetic fence unavailable");
        await f.Dispatcher.TickAsync(default); Assert.True(f.Repository.Row!.PublishBarrier); Assert.Null(f.Repository.Row.CompletedAtUtc);
        Assert.Equal(1, f.Repository.Deferrals); Assert.Equal(1, f.Gateway.Applies);
        f.Gateway.DiscardFailure = null; await f.Dispatcher.TickAsync(default);
        Assert.Equal("Failed", f.Repository.Row.State); Assert.False(f.Repository.Row.PublishBarrier); Assert.Equal(1, f.Gateway.Applies);
    }

    [Fact]
    public async Task DefaultRestoreIntentReferencesOriginalTypeWithoutCopyingAnyHistoricalAclEntries()
    {
        using var f = new Fixture(); f.Gateway.PlanValue = ContainerRecursiveAclRestoreWorkflowFixture.Plan(kind: "default");
        await f.Ready(); var ready = await f.Get();
        Assert.Equal("default", ready.RestoreSource!.ChangedAclType); Assert.Equal(2U, ready.ChangeCount);
        Assert.Contains("仅恢复该来源原修改的default", ready.Confirmation!.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries", JsonSerializer.Serialize(ready.Command), StringComparison.Ordinal);
        await f.Confirm(); await f.Dispatcher.TickAsync(default); Assert.Equal("default", (await f.Get()).Receipt!.AclType);
    }

    [Theory]
    [InlineData("CaptureId")][InlineData("ExpectedHistoryDigest")][InlineData("ExpectedVersion")]
    public async Task ADifferentSourceOrCurrentVersionCannotReuseAnIdempotencyKey(string field)
    {
        using var f = new Fixture(); await f.Queue();
        var command = field switch {
            "CaptureId" => f.Command with { CaptureId = new('e',32) },
            "ExpectedHistoryDigest" => f.Command with { ExpectedHistoryDigest = new('6',64) },
            _ => f.Command with { ExpectedVersion = new('6',64) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueAsync(f.Actor, command, f.Key, default));
        Assert.Equal(1, f.Repository.Enqueues); Assert.Equal(0, f.Gateway.Plans); Assert.Equal(0, f.Gateway.Applies);
    }

    [Theory]
    [InlineData("OutcomeDigest")][InlineData("BindingCurrent")][InlineData("HistoryAvailable")][InlineData("Replayed")]
    public async Task MatchingReceiptColumnsDoNotReplaceTheReceiptMac(string field)
    {
        using var f = new Fixture(); await f.Create(); await f.Dispatcher.TickAsync(default); var row = f.Repository.Row!;
        var receipt = ContainerRecursiveAclRestoreWorkflowValidation.ReadReceipt(row)!;
        receipt = field switch { "OutcomeDigest" => receipt with { OutcomeDigest = new('8',64) }, "BindingCurrent" => receipt with { BindingCurrent = false },
            "HistoryAvailable" => receipt with { HistoryAvailable = false, BindingCurrent = false }, _ => receipt with { Replayed = true } };
        row.ReceiptJson = ContainerRecursiveAclRestoreWorkflowValidation.Serialize(receipt); row.OutcomeDigest = receipt.OutcomeDigest;
        row.BindingCurrent = receipt.BindingCurrent; row.HistoryAvailable = receipt.HistoryAvailable;
        if (!receipt.BindingCurrent) row.State = "Superseded";
        Assert.NotNull(ContainerRecursiveAclRestoreWorkflowValidation.ReadReceipt(row));
        Assert.False(ContainerRecursiveAclRestoreWorkflowValidation.HasValidBinding(row, f.Protector));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get());
    }
}
