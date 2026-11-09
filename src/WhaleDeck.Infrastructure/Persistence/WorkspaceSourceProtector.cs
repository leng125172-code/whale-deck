using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class WorkspaceSourceProtector(IConfiguration configuration) : IDisposable
{
    internal const int Overhead = 29;
    private readonly Lazy<byte[]> _key = new(() => ReadKey(configuration));

    public byte[] Seal(Guid importId, int offset, ReadOnlySpan<byte> bytes)
    {
        Span<byte> context = stackalloc byte[20];
        Bind(context, importId, offset);
        var sealedBytes = new byte[bytes.Length + Overhead];
        sealedBytes[0] = 1;
        var nonce = sealedBytes.AsSpan(1, 12);
        RandomNumberGenerator.Fill(nonce);
        using var cipher = new AesGcm(_key.Value, 16);
        cipher.Encrypt(nonce, bytes, sealedBytes.AsSpan(Overhead), sealedBytes.AsSpan(13, 16), context);
        return sealedBytes;
    }

    public byte[] Open(Guid importId, int offset, byte[] bytes)
    {
        if (bytes.Length <= Overhead || bytes[0] != 1) throw new CryptographicException();
        Span<byte> context = stackalloc byte[20];
        Bind(context, importId, offset);
        var opened = new byte[bytes.Length - Overhead];
        using var cipher = new AesGcm(_key.Value, 16);
        cipher.Decrypt(bytes.AsSpan(1, 12), bytes.AsSpan(Overhead), bytes.AsSpan(13, 16), opened, context);
        return opened;
    }

    public void Dispose()
    {
        if (_key.IsValueCreated) CryptographicOperations.ZeroMemory(_key.Value);
    }

    private static void Bind(Span<byte> context, Guid importId, int offset)
    {
        importId.TryWriteBytes(context);
        BinaryPrimitives.WriteInt32BigEndian(context[16..], offset);
    }

    private static byte[] ReadKey(IConfiguration configuration)
    {
        var path = configuration["Agent:CapabilityKeyFile"] ?? "/run/secrets/agent-capability.key";
        byte[] secret;
        try { secret = File.ReadAllBytes(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("WORKSPACE_SOURCE_PROTECTION_UNAVAILABLE"); }
        try
        {
            if (secret.Length < 32) throw new InvalidOperationException("WORKSPACE_SOURCE_PROTECTION_UNAVAILABLE");
            return HMACSHA256.HashData(secret, "whaledeck-workspace-source-chunk-v1"u8);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
