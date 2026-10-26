namespace WhaleDeck.Application.Abstractions;

/// <summary>Only session liveness, never credentials, ticket bytes or user profiles.</summary>
public interface ILoginSessionRegistry
{
    Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken);
}

/// <summary>API-owned encrypted ticket storage. Renew is conditional and cannot create a missing login.</summary>
public interface IBffSessionStorage : ILoginSessionRegistry
{
    Task<bool> CreateAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> RenewAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<BffSessionSnapshot?> ReadAsync(string binding, CancellationToken cancellationToken);
    Task RemoveAsync(string binding, long? expectedRevision, CancellationToken cancellationToken);
}

/// <summary>Internal only; never serialize into HTTP, jobs, audit or logs.</summary>
public sealed record BffSessionSnapshot(ReadOnlyMemory<byte> ProtectedTicket, string SubjectHash, DateTimeOffset ExpiresAt, long Revision)
{
    public override string ToString() => nameof(BffSessionSnapshot);
}
