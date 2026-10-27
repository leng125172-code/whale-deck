using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed partial class ContainerUploadServiceTests
{
    private static ContainerFileUploadDispatcher Dispatcher(Fixture fixture) => new(fixture.Repository, fixture.Gateway, fixture.Service, fixture.Protector, fixture.Clock);
    private static ContainerFileUploadReceiptDto Receipt(Fixture fixture, string state)
    {
        var row = fixture.Repository.Row!;
        return new(row.ContainerId, row.InstanceId, row.Id, row.TargetProof,
            new(state, row.SizeBytes, row.Sha256, state == "Published" ? new('F',64) : "", false, state == "Published", false), false);
    }
    private static void PreviouslyStarted(Fixture fixture)
    { fixture.Repository.Row!.State = "Publishing"; fixture.Repository.Row.RootStartedAtUtc = fixture.Clock.Now; fixture.Repository.Row.PublishBarrier = true; }

    [Theory]
    [InlineData("logout")]
    [InlineData("demotion")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    public async Task UnreceivedIntentOnlyClosesAfterDurablePreReceiveFence(string condition)
    {
        using var fixture = new Fixture(); await fixture.Create();
        if (condition == "logout") fixture.Logins.Active = false;
        if (condition == "demotion") fixture.Identity.Admin = false;
        if (condition == "cancel") fixture.Repository.CancelRequested = true;
        if (condition == "expiry") fixture.Clock.Now = fixture.Clock.Now.AddMinutes(16);
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(1, fixture.Gateway.Preventions); Assert.Equal(0, fixture.Gateway.Publications);
        Assert.Equal(condition == "expiry" ? "Failed" : "Canceled", Assert.Single(fixture.Repository.Results).State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableAuthorityOrCacheDoesNotPublishOrPretendCancellation(bool cache)
    {
        using var fixture = new Fixture(); await fixture.Create();
        fixture.Repository.Row!.State = "Ready"; fixture.Gateway.Receipt = Receipt(fixture, "Staged");
        fixture.Logins.Unavailable = cache; fixture.Identity.Unavailable = !cache;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(0, fixture.Gateway.Preventions);
        Assert.Empty(fixture.Repository.Results); Assert.Equal(1, fixture.Repository.Deferred);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefinitiveTargetLossBecomesRecoverableUnknownWithoutReleasingProtection(bool started)
    {
        using var fixture = new Fixture(); await fixture.Create(); if (started) PreviouslyStarted(fixture);
        fixture.Gateway.InspectionFailure = new ContainerFileUploadTargetUnavailableException();
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(("Unknown", "CONTAINER_UPLOAD_TARGET_UNAVAILABLE"),Assert.Single(fixture.Repository.Results));
        Assert.Equal(started,fixture.Repository.Row!.PublishBarrier); Assert.Equal(0,fixture.Gateway.Publications); Assert.Equal(0,fixture.Gateway.Preventions);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredUnobservableTaskBecomesUnknownWhileTemporaryFailureCanDefer(bool expired)
    {
        using var fixture = new Fixture(); await fixture.Create(); PreviouslyStarted(fixture);
        if (expired) fixture.Clock.Now=fixture.Clock.Now.AddMinutes(16);
        fixture.Gateway.InspectionFailure = new IOException("owned unavailable transport");
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.True(fixture.Repository.Row!.PublishBarrier); Assert.Equal(0,fixture.Gateway.Publications); Assert.Equal(0,fixture.Gateway.Preventions);
        if (expired) Assert.Equal(("Unknown","CONTAINER_UPLOAD_RESULT_UNKNOWN"),Assert.Single(fixture.Repository.Results));
        else { Assert.Empty(fixture.Repository.Results); Assert.Equal(1,fixture.Repository.Deferred); }
    }

    [Fact]
    public async Task DowngradedAgentCannotAcquirePublicationBarrier()
    {
        using var fixture = new Fixture(); await fixture.Create();
        fixture.Repository.Row!.State = "Ready"; fixture.Gateway.Receipt = Receipt(fixture, "Staged");
        fixture.Gateway.WorkflowUnavailable = true;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Null(fixture.Repository.Row.RootStartedAtUtc); Assert.False(fixture.Repository.Row.PublishBarrier);
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(1, fixture.Repository.Deferred); Assert.Empty(fixture.Repository.Results);
    }

    [Fact]
    public async Task FailedFenceDoesNotBecomeSuccessfulCancellation()
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Logins.Active = false; fixture.Gateway.PreventUnavailable = true;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Empty(fixture.Repository.Results); Assert.Equal(1, fixture.Repository.Deferred); Assert.Null(fixture.Repository.Row!.CompletedAtUtc);
    }
    [Fact]
    public async Task KnownPublishedResultReconcilesAfterLogoutWithoutAnotherPublication()
    {
        using var fixture = new Fixture(); await fixture.Create(); PreviouslyStarted(fixture);
        fixture.Gateway.Receipt = Receipt(fixture, "Published"); fixture.Logins.Active = false;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal("Succeeded", Assert.Single(fixture.Repository.Results).State);
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(0, fixture.Gateway.Preventions);
    }
    [Fact]
    public async Task PublicationWinningTheCancellationFenceIsObservedAsSuccess()
    {
        using var fixture = new Fixture(); await fixture.Create(); PreviouslyStarted(fixture);
        fixture.Gateway.Receipt = Receipt(fixture, "Staged"); fixture.Logins.Active = false;
        fixture.Gateway.BeforePrevent = () => fixture.Gateway.Receipt = Receipt(fixture, "Published");
        fixture.Gateway.RejectFenceOutput = true;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal("Succeeded", Assert.Single(fixture.Repository.Results).State);
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(1, fixture.Gateway.Preventions);
        Assert.Equal(2, fixture.Gateway.UploadInspections);
    }
    [Fact]
    public async Task BodyStagingCannotBypassTheForegroundFinalLoginCheck()
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Repository.Row!.State = "Receiving";
        fixture.Gateway.Receipt = Receipt(fixture, "Staged");
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(0, fixture.Repository.ReadyCalls);
        Assert.Equal("Receiving", fixture.Repository.Row.State); Assert.Empty(fixture.Repository.Results);
    }
    [Fact]
    public async Task ReadyPublicationIsGuardedAndHostBusyDefersWithoutTakingBarrier()
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Repository.Row!.State = "Ready";
        fixture.Gateway.Receipt = Receipt(fixture, "Staged"); fixture.Repository.StartResult = ContainerFileUploadStartResult.Busy;
        Assert.True(await Dispatcher(fixture).TickAsync(default)); Assert.Equal(0, fixture.Gateway.Publications);
        Assert.False(fixture.Repository.Row.PublishBarrier); Assert.Empty(fixture.Repository.Results);
        fixture.Repository.StartResult = ContainerFileUploadStartResult.Started;
        Assert.True(await Dispatcher(fixture).TickAsync(default)); Assert.Equal(1, fixture.Gateway.Publications);
        Assert.Equal("Succeeded", Assert.Single(fixture.Repository.Results).State);
    }
    [Fact]
    public async Task LostPublishResponseIsObservedAfterRestartAndNeverRepublished()
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Repository.Row!.State = "Ready";
        fixture.Gateway.Receipt = Receipt(fixture, "Staged");
        fixture.Gateway.PublishHandler = _ => { fixture.Gateway.Receipt = Receipt(fixture, "Published"); return Task.FromException<ContainerFileUploadReceiptDto>(new IOException("lost-response")); };
        Assert.True(await Dispatcher(fixture).TickAsync(default)); Assert.Empty(fixture.Repository.Results);
        Assert.True(fixture.Repository.Row.PublishBarrier); fixture.Logins.Active = false;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(1, fixture.Gateway.Publications); Assert.Equal("Succeeded", Assert.Single(fixture.Repository.Results).State);
    }
    [Fact]
    public async Task StartedButUnsentPublicationUsesFenceInsteadOfResendingBytes()
    {
        using var fixture = new Fixture(); await fixture.Create(); PreviouslyStarted(fixture);
        fixture.Gateway.Receipt = Receipt(fixture, "Staged");
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(1, fixture.Gateway.Preventions);
        Assert.Equal(("Failed", "CONTAINER_UPLOAD_INTERRUPTED"), Assert.Single(fixture.Repository.Results));
    }
    [Theory]
    [InlineData("Unknown")]
    [InlineData("PublishInterrupted")]
    public async Task UnknownPublicationKeepsBarrierAndDoesNotAutoClaimAgain(string state)
    {
        using var fixture = new Fixture(); await fixture.Create(); PreviouslyStarted(fixture);
        fixture.Gateway.Receipt = Receipt(fixture, state);
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal("Unknown", Assert.Single(fixture.Repository.Results).State); Assert.True(fixture.Repository.Row!.PublishBarrier);
        Assert.Null(fixture.Repository.Row.CompletedAtUtc); Assert.False(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(0, fixture.Gateway.Publications); Assert.Equal(0, fixture.Gateway.Preventions);
    }
    [Fact]
    public async Task InvalidPersistedBindingAndBadPublicationProofNeverBecomeSuccess()
    {
        using var fixture = new Fixture(); await fixture.Create();
        var row = fixture.Repository.Row!;
        fixture.Repository.Row = JsonSerializer.Deserialize<ContainerFileUploadOperation>(JsonSerializer.Serialize(row).Replace(
            "\"Sha256\":\"" + row.Sha256 + "\"", "\"Sha256\":\"" + new string('F',64) + "\"", StringComparison.Ordinal))!;
        Assert.True(await Dispatcher(fixture).TickAsync(default));
        Assert.Equal(("Unknown", "CONTAINER_UPLOAD_BINDING_CHANGED"), Assert.Single(fixture.Repository.Results));
        Assert.Equal(0, fixture.Gateway.UploadInspections); Assert.Equal(0, fixture.Gateway.Publications);
        using var other = new Fixture(); await other.Create(); PreviouslyStarted(other);
        other.Gateway.Receipt = Receipt(other, "Published"); other.Repository.RejectPublicationProof = true;
        Assert.True(await Dispatcher(other).TickAsync(default)); Assert.Equal("Unknown", Assert.Single(other.Repository.Results).State);
        Assert.True(other.Repository.Row!.PublishBarrier);
    }
    [Theory]
    [InlineData("logout")]
    [InlineData("cache")]
    [InlineData("cancel")]
    [InlineData("lease")]
    public async Task InFlightRevocationCancelsAndObservesOneInvocationWithoutFalseTerminal(string condition)
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Repository.Row!.State = "Ready";
        fixture.Gateway.Receipt = Receipt(fixture, "Staged"); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = 0;
        fixture.Gateway.PublishHandler = async token =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException("unexpected-completion"); }
            catch (OperationCanceledException) { stopped++; throw; }
        };
        var tick = Dispatcher(fixture).TickAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (condition == "logout") fixture.Logins.Active = false;
        if (condition == "cache") fixture.Logins.Unavailable = true;
        if (condition == "cancel") fixture.Repository.CancelRequested = true;
        if (condition == "lease") fixture.Repository.LeaseAvailable = false;
        await fixture.Clock.AdvanceAsync(); Assert.True(await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fixture.Gateway.Publications); Assert.Equal(1, stopped); Assert.Empty(fixture.Repository.Results);
        Assert.Equal(1, fixture.Repository.Deferred); Assert.True(fixture.Repository.Row.PublishBarrier); Assert.Null(fixture.Repository.Row.CompletedAtUtc);
    }
    [Fact]
    public async Task AuthorityIsRecheckedAfterTakingTheBarrierBeforeSigningPublication()
    {
        using var fixture = new Fixture(); await fixture.Create(); fixture.Repository.Row!.State = "Ready";
        fixture.Gateway.Receipt = Receipt(fixture, "Staged"); fixture.Repository.AfterStarted = () => fixture.Logins.Active = false;
        Assert.True(await Dispatcher(fixture).TickAsync(default)); Assert.Equal(0, fixture.Gateway.Publications);
        Assert.True(fixture.Repository.Row.PublishBarrier); Assert.Empty(fixture.Repository.Results);
        Assert.True(await Dispatcher(fixture).TickAsync(default)); Assert.Equal("Canceled", Assert.Single(fixture.Repository.Results).State);
        Assert.Equal(1, fixture.Gateway.Preventions); Assert.Equal(0, fixture.Gateway.Publications);
    }
}
