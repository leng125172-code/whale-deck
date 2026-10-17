using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector
{
    public string FingerprintRecoveryRequest(FileRecoveryRequestBinding binding) => Mac(JsonSerializer.Serialize(binding), "whaledeck-administrator-recovery-request-v1");
    public string FingerprintRecoveryReady(FileRecoveryPlanClaims claims) => Mac(JsonSerializer.Serialize(claims), "whaledeck-administrator-recovery-ready-v1");
    private static readonly HashSet<string> RecoveryClaimFields = ["Id", "Actor", "LoginBinding", "Target", "ObservationHash", "RootState", "Disposition", "ReasonCode", "IssuedAtUtc", "ExpiresAtUtc"];
    private static readonly HashSet<string> RecoveryTargetFields = ["Kind", "IntentId", "ExecutionJobId", "OriginalVersion", "OriginalBindingHash"];
    public string SignRecovery(FileRecoveryPlanClaims claims)
    {
        FileRecoveryValidation.Claims(claims);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return payload + "." + Mac(payload, "whaledeck-current-administrator-file-recovery-v1");
    }
    public FileRecoveryPlanClaims VerifyRecovery(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-current-administrator-file-recovery-v1"))))
            throw new ArgumentException("FILES_RECOVERY_SIGNATURE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(parts[0]), new() { MaxDepth = 4 });
            RecoveryFields(document.RootElement, RecoveryClaimFields);
            RecoveryFields(document.RootElement.GetProperty("Target"), RecoveryTargetFields);
            var claims = document.RootElement.Deserialize<FileRecoveryPlanClaims>(FileTreeCopyValidation.JsonOptions)
                ?? throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
            FileRecoveryValidation.Claims(claims); return claims;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        { throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID"); }
    }
    private static void RecoveryFields(JsonElement value, HashSet<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != expected.Count || names.Distinct(StringComparer.Ordinal).Count() != expected.Count || !names.All(expected.Contains))
            throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
    }
}
