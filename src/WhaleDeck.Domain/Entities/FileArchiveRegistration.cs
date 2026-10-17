namespace WhaleDeck.Domain.Entities;

/// <summary>Session-bound five-minute plan reference. No confirmation or job is inferred from existence.</summary>
public sealed class FileArchiveRegistration
{
    public Guid Id { get; init; }
    public required string ActorSubject { get; init; }
    public required string LoginBinding { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string RequestHash { get; init; }
    public required string PlanJson { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
}
