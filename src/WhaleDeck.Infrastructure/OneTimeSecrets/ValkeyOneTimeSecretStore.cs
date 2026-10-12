using System.Security.Cryptography;
using StackExchange.Redis;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Secrets;

public sealed class ValkeyOneTimeSecretStore(IConnectionMultiplexer connection) : IOneTimeSecretStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<OneTimeSecretTicketDto> StoreAsync(string ownerSubject, string secret, CancellationToken cancellationToken)
    {
        MemoryOneTimeSecretStore.Validate(ownerSubject, secret);
        var database = connection.GetDatabase();
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            if (await database.StringSetAsync(MemoryOneTimeSecretStore.Key(ownerSubject, token), secret, Lifetime, When.NotExists))
            {
                return new OneTimeSecretTicketDto(token, expiresAt);
            }
        }
    }

    public async Task<string?> PeekAsync(string ownerSubject, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MemoryOneTimeSecretStore.IsTokenValid(token)) return null;
        var value = await connection.GetDatabase().StringGetAsync(MemoryOneTimeSecretStore.Key(ownerSubject, token));
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<string?> ConsumeAsync(string ownerSubject, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MemoryOneTimeSecretStore.IsTokenValid(token)) return null;
        var value = await connection.GetDatabase().StringGetDeleteAsync(MemoryOneTimeSecretStore.Key(ownerSubject, token));
        return value.HasValue ? value.ToString() : null;
    }
}
