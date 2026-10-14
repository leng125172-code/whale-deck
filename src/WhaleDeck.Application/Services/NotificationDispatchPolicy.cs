using System.Text.RegularExpressions;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Only a definite not-sent outcome is safe for automatic retry.</summary>
public enum NotificationTransportResult { Accepted, RetryableNotSent, Rejected, Unknown }

public static partial class NotificationDispatchPolicy
{
    public const int MaximumAttempts = 5;

    public static void Validate(NotificationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(channel.Name) || channel.Name.Length > 80 || channel.Name.Any(char.IsControl) ||
            channel.Provider is not ("Email" or "Webhook" or "DingTalk" or "WeCom" or "Feishu") ||
            string.IsNullOrWhiteSpace(channel.DestinationReference) || !ReferencePattern().IsMatch(channel.DestinationReference) ||
            channel.CredentialReference is { } credential && !ReferencePattern().IsMatch(credential) || channel.Version < 0)
            throw new ArgumentException("Notification channel metadata is invalid.");
    }

    public static void Validate(NotificationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.ChannelId == Guid.Empty || route.SourceKind is not ("all" or "Alert" or "Job") ||
            SeverityRank(route.MinimumSeverity) < 0 || route.Version < 0)
            throw new ArgumentException("Notification route metadata is invalid.");
    }

    public static bool Matches(NotificationEvent item, NotificationRoute route, NotificationChannel channel)
    {
        // Administrator-configured routes must not broadcast a person's private job result.
        return item.Audience == "Administrator" && item.RecipientSubject is null && route.IsEnabled && channel.IsEnabled &&
            item.PublishedAtUtc >= route.CreatedAtUtc && item.PublishedAtUtc >= channel.CreatedAtUtc &&
            route.ChannelId == channel.Id && SeverityRank(route.MinimumSeverity) >= 0 &&
            (route.SourceKind == "all" || route.SourceKind == item.SourceKind) && SeverityRank(item.Severity) >= 0 &&
            (SeverityRank(item.Severity) >= SeverityRank(route.MinimumSeverity) ||
                route.IncludeRecovery && item.SourceKind == "Alert" && item.MessageCode == "ALERT_RECOVERED");
    }

    public static bool BeginSend(NotificationDelivery item, Guid token, DateTimeOffset now)
    {
        if (token == Guid.Empty || item.State != "Leased" || item.LeaseToken != token || item.LeaseExpiresAtUtc <= now ||
            item.LeaseExpiresAtUtc is null || item.AttemptCount >= MaximumAttempts) return false;
        item.State = "Sending";
        item.AttemptCount++;
        item.LastAttemptAtUtc = now;
        item.LeaseExpiresAtUtc = now.AddSeconds(90);
        return true;
    }

    public static bool Finish(NotificationDelivery item, Guid token, DateTimeOffset now, NotificationTransportResult result)
    {
        if (!Enum.IsDefined(result)) throw new ArgumentOutOfRangeException(nameof(result));
        if (token == Guid.Empty || item.State != "Sending" || item.LeaseToken != token ||
            item.LeaseExpiresAtUtc is null || item.LeaseExpiresAtUtc <= now) return false;
        item.State = result switch
        {
            NotificationTransportResult.Accepted => "Succeeded",
            NotificationTransportResult.Rejected => "Failed",
            NotificationTransportResult.Unknown => "Unknown",
            _ => item.AttemptCount < MaximumAttempts ? "RetryWait" : "Failed"
        };
        item.LastResultCode = result.ToString();
        ClearLease(item);
        if (item.State == "RetryWait") item.NextAttemptAtUtc = now.AddSeconds(RetryDelaySeconds(item.AttemptCount));
        else item.CompletedAtUtc = now;
        return true;
    }

    public static void Recover(NotificationDelivery item, DateTimeOffset now, bool channelCurrent, bool eventCurrent)
    {
        if (item.State == "Sending")
        {
            if (item.LeaseExpiresAtUtc is null || item.LeaseExpiresAtUtc <= now)
            {
                item.State = "Unknown"; item.LastResultCode = "LeaseExpiredAfterSend";
                item.CompletedAtUtc = now; ClearLease(item);
            }
            return; // Disabling a channel cannot prove an already-started send was canceled.
        }
        if (item.State is not ("Pending" or "RetryWait" or "Leased")) return;
        if (!channelCurrent || !eventCurrent)
        {
            item.State = "Canceled"; item.LastResultCode = !eventCurrent ? "EventExpired" : "ChannelChanged";
            item.CompletedAtUtc = now; ClearLease(item);
        }
        else if (item.State == "Leased" && (item.LeaseExpiresAtUtc is null || item.LeaseExpiresAtUtc <= now))
        {
            item.State = "Pending"; item.LastResultCode = "LeaseExpiredBeforeSend";
            item.NextAttemptAtUtc = now; ClearLease(item);
        }
    }

    private static int RetryDelaySeconds(int attempt) => attempt switch { 1 => 30, 2 => 120, 3 => 600, _ => 1800 };
    private static void ClearLease(NotificationDelivery item) { item.LeaseToken = null; item.LeaseExpiresAtUtc = null; }
    private static int SeverityRank(string severity) => severity switch { "Info" => 0, "Warning" => 1, "Error" => 2, "Critical" => 3, _ => -1 };

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
