using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerDirectoryProtector
{
    public string SignDirectory(ContainerDirectoryPlanClaims claims)
    {
        ContainerDirectoryWorkflowValidation.Claims(claims);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims, ContainerDirectoryWorkflowValidation.Json));
        if (payload.Length > 65000) throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        return payload + "." + Mac(payload, "whaledeck-container-directory-business-plan-v1");
    }
    public ContainerDirectoryPlanClaims VerifyDirectory(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 65536) throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-container-directory-business-plan-v1")))) throw new ArgumentException("CONTAINER_DIRECTORY_SIGNATURE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(parts[0]), new() { MaxDepth = 8 });
            DirectoryFields(document.RootElement, "Actor", "LoginBinding", "Plan", "CreatedAtUtc", "ExpiresAtUtc");
            var plan = document.RootElement.GetProperty("Plan");
            DirectoryFields(plan, "SchemaVersion", "Command", "TargetProof", "ParentIdentity", "ParentVersion", "OwnerUid", "OwnerGid", "Fingerprint");
            DirectoryFields(plan.GetProperty("Command"), "Location", "Mode");
            DirectoryFields(plan.GetProperty("Command").GetProperty("Location"), "ContainerId", "InstanceId", "Path");
            DirectoryFields(plan.GetProperty("ParentIdentity"), "Device", "Inode");
            var claims = document.RootElement.Deserialize<ContainerDirectoryPlanClaims>(ContainerDirectoryWorkflowValidation.Json) ?? throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
            ContainerDirectoryWorkflowValidation.Claims(claims); return claims;
        }
        catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID"); }
    }
    public string FingerprintDirectory(ContainerDirectoryBindingDto binding)
    {
        ContainerDirectoryWorkflowValidation.Binding(binding);
        return Mac(JsonSerializer.Serialize(binding, ContainerDirectoryWorkflowValidation.Json), "whaledeck-container-directory-business-intent-v1");
    }
    private static void DirectoryFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != fields.Length || names.Any(n => !fields.Contains(n, StringComparer.Ordinal)))
            throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
    }
}
