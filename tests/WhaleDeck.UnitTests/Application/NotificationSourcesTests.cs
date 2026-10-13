using System.Text.Json;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class NotificationSourcesTests
{
    [Theory]
    [InlineData("Active")]
    [InlineData("SeverityChanged")]
    [InlineData("Recovered")]
    [InlineData("SilenceExpired")]
    public void AlertFactsHaveStableSourceAndAdministratorAudience(string transition)
    {
        var alert = new AlertEvent { RuleId = Guid.NewGuid(), Fingerprint = "fingerprint", Severity = "Critical", SummaryCode = "ALERT_TEST", DetailJson = "secret-value" };
        var history = new AlertEventHistory { AlertEventId = alert.Id, State = transition, ActorSubject = "system:alert-evaluator" };
        var first = NotificationSources.Alert(alert, history, false)!;
        var second = NotificationSources.Alert(alert, history, false)!;
        Assert.Equal(first.SourceKey, second.SourceKey);
        Assert.Equal("Administrator", first.Audience); Assert.Null(first.RecipientSubject);
        Assert.Equal(alert.Id, first.SourceId); Assert.Equal(history.OccurredAtUtc, first.OccurredAtUtc);
        Assert.Null(NotificationSources.Alert(alert, history, true));
        Assert.DoesNotContain("secret-value", JsonSerializer.Serialize(first));
        Assert.Equal(transition == "Recovered" ? "Info" : "Critical", first.Severity);
    }

    [Theory]
    [InlineData("Acknowledged")]
    [InlineData("Silenced")]
    public void DispositionAloneDoesNotSendNewRiskMessages(string state)
    {
        var alert = new AlertEvent { RuleId = Guid.NewGuid(), Fingerprint = "f", Severity = "Warning", SummaryCode = "test" };
        Assert.Null(NotificationSources.Alert(alert, new() { AlertEventId = alert.Id, State = state, ActorSubject = "actor" }, false));
    }

    [Theory]
    [InlineData("Succeeded", "Info")]
    [InlineData("Failed", "Warning")]
    [InlineData("Canceled", "Info")]
    [InlineData("RolledBack", "Warning")]
    public void HumanJobTargetsOnlyItsOwnerAndNeverCopiesPayload(string state, string severity)
    {
        var job = new OperationJob { JobType = "containers.create", ActorSubject = "subject-a", IdempotencyKey = "owned", State = state,
            CompletedAtUtc = DateTimeOffset.UtcNow, RequestJson = "secret-input", ResultJson = "secret-result", ErrorCode = "secret-error" };
        var notification = NotificationSources.Job(job)!;
        Assert.Equal("Subject", notification.Audience); Assert.Equal("subject-a", notification.RecipientSubject); Assert.Equal(severity, notification.Severity);
        Assert.DoesNotContain("secret-", JsonSerializer.Serialize(notification));
        Assert.Equal(notification.SourceKey, NotificationSources.Job(job)!.SourceKey);
        job.State = "Running"; Assert.Null(NotificationSources.Job(job));
    }

    [Fact]
    public void SystemJobBroadcastsAndUnfinishedJobIsNotCompleted()
    {
        var job = new OperationJob { JobType = "backups.create", ActorSubject = "system:scheduler", IdempotencyKey = "key", State = "Succeeded" };
        Assert.Null(NotificationSources.Job(job));
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        var notification = NotificationSources.Job(job)!;
        Assert.Equal("Administrator", notification.Audience); Assert.Null(notification.RecipientSubject);
    }
}
