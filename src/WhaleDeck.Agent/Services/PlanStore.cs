using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed class PlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly object _gate = new();

    public PlanStore(IConfiguration configuration)
    {
        _directory = configuration["Agent:PlanDirectory"] ?? "/var/lib/whaledeck-agent/plans";
        Directory.CreateDirectory(_directory);
    }

    public PlanResponse Create(string resourceId, string action, IReadOnlyDictionary<string, string> parameters, string? resourceFingerprint = null)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var canonical = Canonical(resourceId, action, parameters, expiresAt, nonce, resourceFingerprint);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var record = new StoredPlan(resourceId, action, new Dictionary<string, string>(parameters, StringComparer.Ordinal), expiresAt, nonce, resourceFingerprint);
        Save(hash, record);

        var response = new PlanResponse
        {
            PlanHash = hash,
            ExpiresAtUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(expiresAt)
        };
        response.Changes.Add($"{action}:{resourceId}");
        return response;
    }

    public void VerifyAndConsume(string planHash, string resourceId, string action, IReadOnlyDictionary<string, string> parameters, string? resourceFingerprint = null)
    {
        if (string.IsNullOrWhiteSpace(planHash)) throw new InvalidOperationException("A current plan hash is required.");
        var path = Resolve(planHash);
        lock (_gate)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("The plan is unknown or has already been used.");
            var record = JsonSerializer.Deserialize<StoredPlan>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException("The stored plan is invalid.");
            if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                File.Delete(path);
                throw new InvalidOperationException("The plan has expired.");
            }

            if (!string.Equals(record.ResourceFingerprint, resourceFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The resource changed after this plan was confirmed.");
            var canonical = Canonical(resourceId, action, parameters, record.ExpiresAtUtc, record.Nonce, resourceFingerprint);
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(planHash.ToLowerInvariant())))
            {
                throw new InvalidOperationException("The execution request differs from the confirmed plan.");
            }
            File.Delete(path);
        }
    }

    private void Save(string hash, StoredPlan plan)
    {
        var path = Resolve(hash);
        var temporary = path + ".tmp";
        lock (_gate)
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(plan, JsonOptions));
            File.Move(temporary, path, true);
        }
    }

    private string Resolve(string hash)
    {
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The plan hash is invalid.");
        return Path.Combine(_directory, hash.ToLowerInvariant() + ".json");
    }

    private static string Canonical(string resourceId, string action, IReadOnlyDictionary<string, string> parameters, DateTimeOffset expiresAt, string nonce, string? resourceFingerprint) =>
        string.Join('\n', new[] { resourceId, action, expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), nonce }
            .Concat(parameters.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))
            .Concat(resourceFingerprint is null ? [] : new[] { "resource-fingerprint:" + resourceFingerprint }));

    private sealed record StoredPlan(string ResourceId, string Action, Dictionary<string, string> Parameters, DateTimeOffset ExpiresAtUtc, string Nonce, string? ResourceFingerprint = null);
}
