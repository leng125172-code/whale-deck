using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationDispatchPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
    private static NotificationChannel Channel() => new() { Name = "Test", Provider = "Webhook", DestinationReference = "approved-test", IsEnabled = true, CreatedAtUtc = Now.AddDays(-1) };
    private static NotificationEvent Event(string audience = "Administrator", string code = "ALERT_ACTIVE", string severity = "Critical") => new()
    {
        SourceKey = "unit", SourceKind = "Alert", Audience = audience, RecipientSubject = audience == "Subject" ? "user" : null,
        MessageCode = code, Severity = severity, PublishedAtUtc = Now
    };
    private static NotificationDelivery Leased(Guid token) => new()
    {
        Provider = "Webhook", DestinationReference = "test", State = "Leased", LeaseToken = token, LeaseExpiresAtUtc = Now.AddSeconds(30)
    };

    [Theory]
    [InlineData("Administrator", "ALERT_ACTIVE", "Critical", true)]
    [InlineData("Subject", "ALERT_ACTIVE", "Critical", false)]
    [InlineData("Administrator", "ALERT_ACTIVE", "Info", false)]
    [InlineData("Administrator", "ALERT_RECOVERED", "Info", true)]
    [InlineData("Administrator", "ALERT_ACTIVE", "Unknown", false)]
    public void RoutesPreserveAudienceAndSeverity(string audience, string code, string severity, bool match)
    {
        var channel = Channel();
        var route = new NotificationRoute { ChannelId = channel.Id, IsEnabled = true, CreatedAtUtc = Now.AddDays(-1) };
        Assert.Equal(match, NotificationDispatchPolicy.Matches(Event(audience, code, severity), route, channel));
        route.IsEnabled = false; Assert.False(NotificationDispatchPolicy.Matches(Event(audience, code, severity), route, channel));
    }

    [Fact]
    public void NewOrForeignRoutesDoNotReplayOldEvents()
    {
        var channel = Channel();
        var route = new NotificationRoute { ChannelId = channel.Id, IsEnabled = true, CreatedAtUtc = Now.AddSeconds(1) };
        Assert.False(NotificationDispatchPolicy.Matches(Event(), route, channel));
        var invalid = new NotificationRoute { ChannelId = channel.Id, IsEnabled = true, CreatedAtUtc = Now.AddDays(-1), MinimumSeverity = "unknown" };
        Assert.False(NotificationDispatchPolicy.Matches(Event(), invalid, channel));
    }

    [Theory]
    [InlineData("https://private.invalid/hook")]
    [InlineData("bad\nname")]
    [InlineData("ABC")]
    [InlineData("")]
    public void DestinationIsAReferenceNotAnEndpointOrSecret(string reference)
    {
        var channel = Channel(); channel.DestinationReference = reference;
        Assert.Throws<ArgumentException>(() => NotificationDispatchPolicy.Validate(channel));
    }

    [Fact]
    public void OnlyKnownNotSentFailuresAutomaticallyRetryAndStopAtFive()
    {
        var token = Guid.NewGuid(); var item = Leased(token); var now = Now;
        for (var attempt = 1; attempt <= NotificationDispatchPolicy.MaximumAttempts; attempt++)
        {
            Assert.True(NotificationDispatchPolicy.BeginSend(item, token, now));
            Assert.Equal(attempt, item.AttemptCount);
            Assert.True(NotificationDispatchPolicy.Finish(item, token, now.AddSeconds(1), NotificationTransportResult.RetryableNotSent));
            Assert.Null(item.LeaseToken);
            if (attempt < NotificationDispatchPolicy.MaximumAttempts)
            {
                Assert.Equal("RetryWait", item.State); Assert.True(item.NextAttemptAtUtc > now);
                now = item.NextAttemptAtUtc; item.State = "Leased"; item.LeaseToken = token; item.LeaseExpiresAtUtc = now.AddSeconds(30);
            }
            else Assert.Equal("Failed", item.State);
        }
    }

    [Theory]
    [InlineData(NotificationTransportResult.Accepted, "Succeeded")]
    [InlineData(NotificationTransportResult.Unknown, "Unknown")]
    [InlineData(NotificationTransportResult.Rejected, "Failed")]
    public void TerminalOutcomesAreNeverAutomaticallyResent(NotificationTransportResult result, string state)
    {
        var token = Guid.NewGuid(); var item = Leased(token);
        Assert.True(NotificationDispatchPolicy.BeginSend(item, token, Now));
        Assert.True(NotificationDispatchPolicy.Finish(item, token, Now, result));
        NotificationDispatchPolicy.Recover(item, Now.AddHours(1), true, true);
        Assert.Equal(state, item.State); Assert.NotNull(item.CompletedAtUtc);
        Assert.False(NotificationDispatchPolicy.BeginSend(item, token, Now.AddHours(1)));
    }

    [Fact]
    public void RestartBeforeSendRetriesButRestartAfterSendBecomesUnknown()
    {
        var token = Guid.NewGuid(); var before = Leased(token); var after = Leased(token);
        Assert.True(NotificationDispatchPolicy.BeginSend(after, token, Now));
        NotificationDispatchPolicy.Recover(before, Now.AddMinutes(2), true, true);
        NotificationDispatchPolicy.Recover(after, Now.AddMinutes(2), true, true);
        Assert.Equal("Pending", before.State); Assert.Equal(0, before.AttemptCount);
        Assert.Equal("Unknown", after.State); Assert.Equal(1, after.AttemptCount);
        Assert.False(NotificationDispatchPolicy.Finish(after, token, Now.AddMinutes(2), NotificationTransportResult.Accepted));
    }

    [Fact]
    public void ChangedChannelCancelsPreparationButCannotProveInFlightCancellation()
    {
        var token = Guid.NewGuid(); var before = Leased(token); var after = Leased(token);
        Assert.True(NotificationDispatchPolicy.BeginSend(after, token, Now));
        NotificationDispatchPolicy.Recover(before, Now, false, true);
        NotificationDispatchPolicy.Recover(after, Now, false, true);
        Assert.Equal("Canceled", before.State); Assert.Equal("Sending", after.State);
        Assert.False(NotificationDispatchPolicy.BeginSend(Leased(token), Guid.NewGuid(), Now));
    }
}
