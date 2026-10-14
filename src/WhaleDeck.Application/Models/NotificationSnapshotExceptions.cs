namespace WhaleDeck.Application.Models;

public sealed class NotificationSnapshotUnavailableException() : KeyNotFoundException("Notification read snapshot is unavailable.");
