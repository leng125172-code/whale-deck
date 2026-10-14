using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Infrastructure.Notifications;

public sealed class NotificationPrivateInputStore(IConnectionMultiplexer? connection = null, TimeProvider? clock = null) : INotificationPrivateInputStore
{
    private readonly ConcurrentDictionary<string, (string Input, DateTimeOffset Expires)> _memory = new();
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static string Key(string owner, Guid id) => "whaledeck:notification-private:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))) + ":" + id.ToString("N");
    public async Task PutAsync(string owner, Guid operationId, string input, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ttl = expiresAt - _clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(owner) || operationId == Guid.Empty || Encoding.UTF8.GetByteCount(input) > 64000 || ttl <= TimeSpan.Zero || ttl > TimeSpan.FromMinutes(5)) throw new ArgumentException("Invalid private input budget.");
        var key = Key(owner, operationId);
        if (connection is null)
        {
            lock (_gate)
            {
                foreach (var stale in _memory.Where(item => item.Value.Expires <= _clock.GetUtcNow())) _memory.TryRemove(stale.Key, out _);
                if (!_memory.TryAdd(key, (input, expiresAt)) && _memory.TryGetValue(key, out var existing) && existing.Input != input) throw new InvalidOperationException("Private input is already frozen for this operation.");
            }
        }
        else if (!await connection.GetDatabase().StringSetAsync(key, input, ttl, When.NotExists) &&
            (await connection.GetDatabase().StringGetAsync(key)).ToString() != input) throw new InvalidOperationException("Private input is already frozen for this operation.");
    }
    public async Task<string?> GetAsync(string owner, Guid operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var key = Key(owner, operationId);
        if (connection is null) return _memory.TryGetValue(key, out var value) && value.Expires > _clock.GetUtcNow() ? value.Input : null;
        var result = await connection.GetDatabase().StringGetAsync(key); return result.HasValue ? result.ToString() : null;
    }
    public async Task DeleteAsync(string owner, Guid operationId, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); if (connection is null) _memory.TryRemove(Key(owner, operationId), out _); else await connection.GetDatabase().KeyDeleteAsync(Key(owner, operationId)); }
}
