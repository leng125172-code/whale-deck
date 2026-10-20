using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IFileTreePurgingProtector
{
    public string FingerprintRequest(FileTreePurgingRequestBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-tree-purge-request-v1");
    public string FingerprintPlan(FileTreePurgingPlanBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-tree-purge-ready-v1");
    public string FingerprintReceipt(FileTreePurgingReceiptBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-tree-purge-receipt-v1");
    public string SignConfirmation(FileTreePurgingConfirmation claims)
    {
        ValidatePurgingClaims(claims);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload, "whaledeck-tree-purge-confirmation-v1");
    }
    public FileTreePurgingConfirmation VerifyPurgingConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("FILES_PLAN_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-tree-purge-confirmation-v1")))) throw new ArgumentException("FILES_PLAN_SIGNATURE_INVALID");
        try
        {
            var value = JsonSerializer.Deserialize<FileTreePurgingConfirmation>(Convert.FromBase64String(parts[0]), FileTreePurgingValidation.JsonOptions)
                ?? throw new ArgumentException("FILES_PLAN_INVALID");
            ValidatePurgingClaims(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException) { throw new ArgumentException("FILES_PLAN_INVALID"); }
    }
    private static void ValidatePurgingClaims(FileTreePurgingConfirmation claims)
    {
        if (claims is null || claims.Id == Guid.Empty || claims.CaptureId == Guid.Empty || claims.Id == claims.CaptureId ||
            string.IsNullOrWhiteSpace(claims.Actor) || claims.Actor.Length > 256 || claims.Actor.Any(char.IsControl) ||
            !FileManagementService.Hex(claims.LoginBinding) || !FileManagementService.Hex(claims.Fingerprint) || !FileTreePurgingValidation.Scope(claims.Scope))
            throw new ArgumentException("FILES_PLAN_INVALID");
    }
}
