namespace WhaleDeck.Application.Models;

/// <summary>Only a definitive root input/version rejection; I/O failures remain unknown.</summary>
public sealed class NotificationPrivateRejectedException() : Exception("Private configuration was rejected before mutation.");
