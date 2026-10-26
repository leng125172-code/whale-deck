using System.Security.Cryptography;
using System.Text;

namespace WhaleDeck.Application.Services;

public static class BffSessionValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public const int MaximumTicketBytes = 131072;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(8);
    public static bool ValidBinding(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
    public static string Binding(string reference)
    {
        if (reference.Length != 64 || !reference.All(char.IsAsciiHexDigit)) throw new ArgumentException("SESSION_REFERENCE_INVALID");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference)));
    }
    public static string SubjectHash(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 256 || subject.Any(char.IsControl)) throw new ArgumentException("SESSION_SUBJECT_INVALID");
        try { return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes("whaledeck-bff-subject-v2:" + subject))); }
        catch (EncoderFallbackException) { throw new ArgumentException("SESSION_SUBJECT_INVALID"); }
    }
    public static void Payload(string binding, string subject, ReadOnlyMemory<byte> bytes, DateTimeOffset expires, DateTimeOffset now)
    {
        if (!ValidBinding(binding) || bytes.Length is < 1 or > MaximumTicketBytes || expires > now.Add(MaximumLifetime))
            throw new ArgumentException("SESSION_PAYLOAD_INVALID");
        _ = SubjectHash(subject);
    }
}
