using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class NotificationPrivateWorkflowTests
{
    [Theory]
    [InlineData(NotificationPrivateStartResult.CancellationRequested, false, "Canceled")]
    [InlineData(NotificationPrivateStartResult.CancellationRequested, true, "Unknown")]
    [InlineData(NotificationPrivateStartResult.InputExpired, false, "Failed")]
    [InlineData(NotificationPrivateStartResult.InputExpired, true, "Unknown")]
    [InlineData(NotificationPrivateStartResult.LeaseLost, false, null)]
    public async Task FinalStartGateNeverWritesWhenCanceledExpiredOrFenced(NotificationPrivateStartResult start, bool previouslyStarted, string? expected)
    {
        var operation = Operation(previouslyStarted); _repo.Operation = operation; _repo.StartResult = start;
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        await Dispatcher().TickAsync(default);
        Assert.Equal(0, _agent.Applies); Assert.Equal(expected, _repo.Finished);
        Assert.Equal(expected is "Canceled" or "Failed", await _inputs.GetAsync(operation.ActorSubject, operation.Id, default) is null);
    }
    [Theory]
    [InlineData(false, "Canceled")]
    [InlineData(true, "Unknown")]
    public async Task CancellationRetainsUncertaintyIfRootMayAlreadyHaveStarted(bool started, string expected)
    {
        var operation = Operation(started); _repo.Operation = operation; _repo.Canceled = true;
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        await Dispatcher().TickAsync(default);
        Assert.Equal(expected, _repo.Finished); Assert.Equal(0, _agent.Applies);
        Assert.Equal(!started, await _inputs.GetAsync(operation.ActorSubject, operation.Id, default) is null);
    }
    [Fact]
    public async Task FencedResultDoesNotDeleteTheCurrentOwnersInput()
    {
        var operation = Operation(); _repo.Operation = operation; _repo.FinishAccepted = false;
        _agent.Receipt = Metadata(operation.Id);
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        await Dispatcher().TickAsync(default);
        Assert.Null(_repo.Finished); Assert.Equal(0, _agent.Applies);
        Assert.NotNull(await _inputs.GetAsync(operation.ActorSubject, operation.Id, default));
    }
    [Theory]
    [InlineData(false, "Failed")]
    [InlineData(true, "Unknown")]
    public async Task RejectionOfARetryDoesNotRewriteAnEarlierUnknownAttemptAsDefinitelyFailed(bool started, string expected)
    {
        var operation = Operation(started); _repo.Operation = operation; _agent.ApplyFailure = new NotificationPrivateRejectedException();
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        await Dispatcher().TickAsync(default);
        Assert.Equal(expected, _repo.Finished); Assert.Equal(1, _agent.Applies);
        Assert.Equal(!started, await _inputs.GetAsync(operation.ActorSubject, operation.Id, default) is null);
    }
    [Theory]
    [InlineData(false, "Failed")]
    [InlineData(true, "Unknown")]
    public async Task ChangedInputCannotBeSentAndOnlyPriorAttemptsRemainUnknown(bool started, string expected)
    {
        var operation = Operation(started, changedInput: true); _repo.Operation = operation;
        await _inputs.PutAsync(operation.ActorSubject, operation.Id, Input, _clock.GetUtcNow().AddMinutes(5), default);
        await Dispatcher().TickAsync(default);
        Assert.Equal(expected, _repo.Finished); Assert.Equal(0, _agent.Applies);
    }
    [Fact]
    public async Task AcceptedAcknowledgmentReplaysWithoutAgentEvenWhenAgentIsUnavailable()
    {
        _repo.AckReplay = true; _agent.ReadFailure = new IOException("fixture unavailable");
        await _workflow.AcknowledgeAsync(Context(), _repo.Channel.Id, new(Guid.NewGuid(), 7, true, true), default);
        Assert.Equal(0, _agent.ReceiptReads); Assert.Equal(0, _repo.Acknowledged);
    }
    [Fact]
    public async Task AvailableRootReceiptPreventsNewUnknownAcknowledgment()
    {
        var id = Guid.NewGuid(); _agent.Receipt = Metadata(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _workflow.AcknowledgeAsync(Context(), _repo.Channel.Id, new(id, 7, true, true), default));
        Assert.Equal(1, _agent.ReceiptReads); Assert.Equal(0, _repo.Acknowledged);
    }
    [Fact]
    public async Task ReplayDoesNotBypassCurrentPermissionOrConfirmation()
    {
        _repo.AckReplay = true;
        var command = new AcknowledgeNotificationPrivateCommand(Guid.NewGuid(), 7, true, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _workflow.AcknowledgeAsync(Context(false), _repo.Channel.Id, command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _workflow.AcknowledgeAsync(Context(), _repo.Channel.Id, command with { Confirmed = false }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _workflow.AcknowledgeAsync(Context(), _repo.Channel.Id, command with { AcceptUnknownOutcome = false }, default));
        Assert.Equal(0, _agent.ReceiptReads); Assert.Equal(0, _repo.Acknowledged);
    }
    private NotificationPrivateDispatcher Dispatcher() => new(_repo, _agent, _inputs, new Authorizer(true), _protector, _clock);
}
