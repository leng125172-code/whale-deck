using StackExchange.Redis;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Identity;

/// <summary>One TTL-bound hash; atomic renewal never creates a missing/revoked login.</summary>
public sealed class ValkeyBffSessionStorage(IConnectionMultiplexer connection, TimeProvider clock) : IBffSessionStorage
{
    public const string Prefix = "whaledeck:auth:session:v2:";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private const string WriteScript = """
        local t=redis.call('TIME');local now=tonumber(t[1])*1000+math.floor(tonumber(t[2])/1000)
        local expiry=tonumber(ARGV[3]);if not expiry or expiry<=now or expiry>now+28800000 then return 0 end
        local kind=redis.call('TYPE',KEYS[1]).ok
        if ARGV[4]=='create' then
          if kind~='none' then return 0 end
          redis.call('HSET',KEYS[1],'ticket',ARGV[1],'subject',ARGV[2],'expires',ARGV[3],'revision','1')
        else
          if kind~='hash' or redis.call('PTTL',KEYS[1])<=0 then return 0 end
          local old=redis.call('HMGET',KEYS[1],'subject','expires','revision')
          local n=redis.call('HSTRLEN',KEYS[1],'ticket');local rev=tonumber(old[3]);local untilAt=tonumber(old[2])
          if n<1 or n>131072 or old[1]~=ARGV[2] or not untilAt or untilAt~=math.floor(untilAt) or untilAt<=now or expiry<untilAt or not string.match(old[2],'^%d+$') or not rev or rev~=math.floor(rev) or rev<1 or rev>=9007199254740991 or not string.match(old[3],'^[1-9]%d*$') then return 0 end
          redis.call('HSET',KEYS[1],'ticket',ARGV[1],'expires',ARGV[3])
          redis.call('HINCRBY',KEYS[1],'revision',1)
        end
        redis.call('PEXPIREAT',KEYS[1],ARGV[3]);return 1
        """;
    private const string ReadScript = """
        if redis.call('TYPE',KEYS[1]).ok~='hash' or redis.call('PTTL',KEYS[1])<=0 then return nil end
        local v=redis.call('HMGET',KEYS[1],'ticket','subject','expires','revision')
        local t=redis.call('TIME');local now=tonumber(t[1])*1000+math.floor(tonumber(t[2])/1000)
        local expiry=tonumber(v[3]);local rev=tonumber(v[4])
        if not v[1] or #v[1]<1 or #v[1]>131072 or not v[2] or not expiry or expiry~=math.floor(expiry) or expiry<=now or expiry>now+28800000 or not string.match(v[3],'^%d+$') or not rev or rev~=math.floor(rev) or rev<1 or rev>9007199254740991 or not string.match(v[4],'^[1-9]%d*$') then return nil end
        return v
        """;
    private const string ActiveScript = """
        if redis.call('TYPE',KEYS[1]).ok~='hash' or redis.call('PTTL',KEYS[1])<=0 then return 0 end
        local v=redis.call('HMGET',KEYS[1],'subject','expires','revision')
        local t=redis.call('TIME');local now=tonumber(t[1])*1000+math.floor(tonumber(t[2])/1000)
        local n=redis.call('HSTRLEN',KEYS[1],'ticket');local expiry=tonumber(v[2]);local rev=tonumber(v[3])
        if n<1 or n>131072 or v[1]~=ARGV[1] or not expiry or expiry~=math.floor(expiry) or expiry<=now or expiry>now+28800000 or not string.match(v[2],'^%d+$') or not rev or rev~=math.floor(rev) or rev<1 or rev>9007199254740991 or not string.match(v[3],'^[1-9]%d*$') then return 0 end
        return 1
        """;
    private const string RemoveScript = """
        if ARGV[1]~='' then
          if redis.call('TYPE',KEYS[1]).ok~='hash' or redis.call('HGET',KEYS[1],'revision')~=ARGV[1] then return 0 end
        end
        return redis.call('DEL',KEYS[1])
        """;

    public Task<bool> CreateAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Write(binding, subject, protectedTicket, expiresAt, "create", cancellationToken);
    public Task<bool> RenewAsync(string binding, string subject, ReadOnlyMemory<byte> protectedTicket, DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        Write(binding, subject, protectedTicket, expiresAt, "renew", cancellationToken);
    private async Task<bool> Write(string binding, string subject, ReadOnlyMemory<byte> bytes, DateTimeOffset expires, string mode, CancellationToken cancellationToken)
    {
        BffSessionValidation.Payload(binding, subject, bytes, expires, clock.GetUtcNow());
        return (long)await Evaluate(WriteScript, binding, [bytes.ToArray(), BffSessionValidation.SubjectHash(subject), expires.ToUnixTimeMilliseconds(), mode], cancellationToken) == 1;
    }
    public async Task<BffSessionSnapshot?> ReadAsync(string binding, CancellationToken cancellationToken)
    {
        if (!BffSessionValidation.ValidBinding(binding)) return null;
        var value = await Evaluate(ReadScript, binding, [], cancellationToken);
        if (value.IsNull) return null;
        var fields = (RedisResult[])value!;
        if (fields.Length != 4) throw new InvalidOperationException("SESSION_STORAGE_INVALID");
        var bytes = (byte[])fields[0]!; var subject = (string)fields[1]!; var expires = (long)fields[2]; var revision = (long)fields[3];
        if (bytes.Length is < 1 or > BffSessionValidation.MaximumTicketBytes || !BffSessionValidation.ValidBinding(subject) ||
            expires is < 0 or > 253402300799999 || revision < 1) throw new InvalidOperationException("SESSION_STORAGE_INVALID");
        return new(bytes, subject, DateTimeOffset.FromUnixTimeMilliseconds(expires), revision);
    }
    public async Task<bool> IsActiveAsync(string subject, string loginBinding, CancellationToken cancellationToken)
    {
        if (!BffSessionValidation.ValidBinding(loginBinding)) return false;
        return (long)await Evaluate(ActiveScript, loginBinding, [BffSessionValidation.SubjectHash(subject)], cancellationToken) == 1;
    }
    public async Task RemoveAsync(string binding, long? expectedRevision, CancellationToken cancellationToken)
    {
        if (!BffSessionValidation.ValidBinding(binding)) return;
        _ = await Evaluate(RemoveScript, binding, [expectedRevision.HasValue ? expectedRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""], cancellationToken);
    }
    private async Task<RedisResult> Evaluate(string script, string binding, RedisValue[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await connection.GetDatabase().ScriptEvaluateAsync(script, [Prefix + binding], values).WaitAsync(Budget, cancellationToken);
    }
}
