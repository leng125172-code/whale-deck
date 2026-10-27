using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerFileRestoreProtector
{
    public string SignContainerRestore(ContainerFileRestorePlanClaims claims)
    {
        ContainerFileRestoreValidation.Claims(claims);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims, ContainerFileRestoreValidation.Json));
        if (payload.Length > 65000) throw ContainerFileRestoreValidation.Invalid();
        return payload + "." + Mac(payload, "whaledeck-container-restore-plan-v1");
    }
    public ContainerFileRestorePlanClaims VerifyContainerRestore(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 65536) throw ContainerFileRestoreValidation.Invalid();
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-container-restore-plan-v1"))))
            throw new ArgumentException("CONTAINER_HISTORY_SIGNATURE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(parts[0]), new() { MaxDepth = 8 });
            ContainerFileRestoreValidation.Exact(document.RootElement, new HashSet<string> { "Actor", "LoginBinding", "Target", "Source", "CreatedAtUtc", "ExpiresAtUtc" });
            var target = document.RootElement.GetProperty("Target");
            ContainerUploadFields(target, ContainerUploadTargetFields); ContainerUploadFields(target.GetProperty("Location"), ContainerUploadLocationFields);
            var source = document.RootElement.GetProperty("Source");
            // Read() requires canonical source representation and rejects duplicates.
            var canonicalSource = JsonSerializer.Serialize(source, ContainerFileRestoreValidation.Json);
            _ = ContainerFileRestoreValidation.Read(canonicalSource);
            var claims = document.RootElement.Deserialize<ContainerFileRestorePlanClaims>(ContainerFileRestoreValidation.Json) ?? throw ContainerFileRestoreValidation.Invalid();
            ContainerFileRestoreValidation.Claims(claims); return claims;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw ContainerFileRestoreValidation.Invalid(); }
    }
}
