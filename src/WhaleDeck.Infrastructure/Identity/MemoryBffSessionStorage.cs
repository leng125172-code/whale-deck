using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Identity;

/// <summary>Single-process development only. Worker in another process fails closed.</summary>
public sealed class MemoryBffSessionStorage(TimeProvider clock) : IBffSessionStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BffSessionSnapshot> _sessions = new(StringComparer.Ordinal);
    public Task<bool> CreateAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Write(binding, subject, protectedTicket, expiresAt, false, cancellationToken);
    public Task<bool> RenewAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Write(binding, subject, protectedTicket, expiresAt, true, cancellationToken);
    private Task<bool> Write(string binding, string subject, ReadOnlyMemory<byte> bytes, DateTimeOffset expires, bool renew, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); BffSessionValidation.Payload(binding, subject, bytes, expires, clock.GetUtcNow());
        var hash = BffSessionValidation.SubjectHash(subject);
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in _sessions.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray()) _sessions.Remove(key);
            var found = _sessions.GetValueOrDefault(binding);
            if (expires <= now || renew && (found is null || found.SubjectHash != hash || expires.ToUnixTimeMilliseconds() < found.ExpiresAt.ToUnixTimeMilliseconds()) ||
                !renew && (found is not null || _sessions.Count >= 4096)) return Task.FromResult(false);
            _sessions[binding] = new(bytes.ToArray(), hash, DateTimeOffset.FromUnixTimeMilliseconds(expires.ToUnixTimeMilliseconds()), found is null ? 1 : checked(found.Revision + 1));
            return Task.FromResult(true);
        }
    }
    public Task<BffSessionSnapshot?> ReadAsync(string binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BffSessionValidation.ValidBinding(binding)) return Task.FromResult<BffSessionSnapshot?>(null);
        lock (_gate)
        {
            var value = _sessions.GetValueOrDefault(binding);
            if (value?.ExpiresAt <= clock.GetUtcNow()) { _sessions.Remove(binding); value = null; }
            return Task.FromResult(value is null ? null : value with { ProtectedTicket = value.ProtectedTicket.ToArray() });
        }
    }
    public Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BffSessionValidation.ValidBinding(loginBinding)) return Task.FromResult(false);
        var hash = BffSessionValidation.SubjectHash(subject);
        lock (_gate) return Task.FromResult(_sessions.TryGetValue(loginBinding, out var value) && value.ExpiresAt > clock.GetUtcNow() && value.SubjectHash == hash);
    }
    public Task RemoveAsync(string binding, long? expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BffSessionValidation.ValidBinding(binding)) return Task.CompletedTask;
        lock (_gate)
            if (_sessions.TryGetValue(binding, out var value) && (expectedRevision is null || expectedRevision == value.Revision)) _sessions.Remove(binding);
        return Task.CompletedTask;
    }
}
