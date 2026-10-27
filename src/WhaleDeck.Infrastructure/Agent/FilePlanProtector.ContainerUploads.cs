using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector
{
    private static readonly JsonSerializerOptions ContainerUploadJson = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly HashSet<string> ContainerUploadClaimFields = ["Actor", "LoginBinding", "Target", "SizeBytes", "Sha256", "CreatedAtUtc", "ExpiresAtUtc"];
    private static readonly HashSet<string> ContainerUploadTargetFields = ["Location", "TargetProof", "ExpectedParentVersion", "ExpectedVersion", "CurrentSizeBytes"];
    private static readonly HashSet<string> ContainerUploadLocationFields = ["ContainerId", "InstanceId", "Path"];
    public string SignContainerUpload(ContainerFileUploadPlanClaims claims)
    {
        ContainerFileUploadIntentValidation.Claims(claims);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims, ContainerUploadJson));
        if (payload.Length > 65000) throw new ArgumentException("CONTAINER_UPLOAD_PLAN_TOO_LARGE");
        return payload + "." + Mac(payload, "whaledeck-container-upload-plan-v1");
    }
    public ContainerFileUploadPlanClaims VerifyContainerUpload(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 65536) throw new ArgumentException("CONTAINER_UPLOAD_PLAN_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(Mac(parts[0], "whaledeck-container-upload-plan-v1"))))
            throw new ArgumentException("CONTAINER_UPLOAD_SIGNATURE_INVALID");
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(parts[0]), new() { MaxDepth = 8 });
            ContainerUploadFields(document.RootElement, ContainerUploadClaimFields);
            var target = document.RootElement.GetProperty("Target"); ContainerUploadFields(target, ContainerUploadTargetFields);
            ContainerUploadFields(target.GetProperty("Location"), ContainerUploadLocationFields);
            var claims = document.RootElement.Deserialize<ContainerFileUploadPlanClaims>(ContainerUploadJson)
                ?? throw new ArgumentException("CONTAINER_UPLOAD_PLAN_INVALID");
            ContainerFileUploadIntentValidation.Claims(claims); return claims;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new ArgumentException("CONTAINER_UPLOAD_PLAN_INVALID"); }
    }
    public string FingerprintContainerUpload(ContainerFileUploadBinding binding)
    {
        ContainerFileUploadIntentValidation.Binding(binding);
        return Mac(JsonSerializer.Serialize(binding, ContainerUploadJson), "whaledeck-container-upload-intent-v1");
    }
    private static void ContainerUploadFields(JsonElement value, HashSet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("CONTAINER_UPLOAD_PLAN_INVALID");
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != fields.Count || names.Distinct(StringComparer.Ordinal).Count() != fields.Count || !names.All(fields.Contains))
            throw new ArgumentException("CONTAINER_UPLOAD_PLAN_INVALID");
    }
}
