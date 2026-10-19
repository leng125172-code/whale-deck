using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector
{
    public string FingerprintRequest(FileTreeUnpackingRequestBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-tree-unpack-request-v1");
    public string FingerprintPlan(FileTreeUnpackingPlanBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-tree-unpack-ready-v1");
    public string SignConfirmation(FileTreeUnpackingConfirmation claims)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload, "whaledeck-tree-unpack-confirmation-v1");
    }
    public FileTreeUnpackingConfirmation VerifyUnpackingConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("FILES_PLAN_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || parts[1].Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-tree-unpack-confirmation-v1")))) throw new ArgumentException("FILES_PLAN_SIGNATURE_INVALID");
        try
        {
            var value = JsonSerializer.Deserialize<FileTreeUnpackingConfirmation>(Convert.FromBase64String(parts[0]), FileTreeUnpackingValidation.JsonOptions);
            if (value is null || value.Id == Guid.Empty || string.IsNullOrWhiteSpace(value.Actor) || value.Actor.Length > 256 || value.Actor.Any(char.IsControl) ||
                !FileManagementService.Hex(value.LoginBinding) || !FileManagementService.Hex(value.Fingerprint)) throw new ArgumentException("FILES_PLAN_INVALID");
            return value;
        }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("FILES_PLAN_INVALID"); }
    }
}
