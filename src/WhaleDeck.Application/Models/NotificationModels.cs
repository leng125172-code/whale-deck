namespace WhaleDeck.Application.Models;

public sealed record NotificationContext(string Subject, bool IsAdministrator, string TraceId, string? SourceIp = null);
public sealed record NotificationQuery(int Take = 50, long? Before = null, string Status = "all", string Kind = "all");
public sealed record NotificationDto(Guid Id, long Sequence, string SourceKind, Guid SourceId, string MessageCode, string Severity,
    string Title, string Summary, DateTimeOffset OccurredAtUtc, DateTimeOffset PublishedAtUtc, DateTimeOffset? ReadAtUtc, string? TargetPath);
public sealed record NotificationSummaryDto(long UnreadCount, long TotalCount, long SnapshotSequence);
public sealed record NotificationPageDto(IReadOnlyCollection<NotificationDto> Items, long? NextBefore, long SnapshotSequence);
public sealed record SetNotificationReadCommand(bool IsRead = true);
public sealed record PrepareNotificationsReadDto(Guid SnapshotId, long ItemCount, DateTimeOffset ExpiresAtUtc);
public sealed record MarkNotificationsReadCommand(Guid SnapshotId);
public sealed record NotificationReadDto(Guid Id, bool IsRead, DateTimeOffset? ReadAtUtc);
public sealed record MarkNotificationsReadDto(int ChangedCount, bool HasMore = false, int ProcessedCount = 0);
