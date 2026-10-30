using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerPermissionProtector
{
    private const string PermissionConfirmationDomain = "whaledeck-container-permission-business-confirmation-v1";
    public string FingerprintPermissionRequest(ContainerPermissionRequestBinding binding)
    {
        ContainerPermissionWorkflowValidation.Request(binding);
        return Mac(ContainerPermissionWorkflowValidation.Serialize(binding), "whaledeck-container-permission-business-request-v1");
    }
    public string FingerprintPermissionPlan(ContainerPermissionPlanBinding binding)
    {
        ContainerPermissionWorkflowValidation.Plan(binding);
        return Mac(ContainerPermissionWorkflowValidation.Serialize(binding), "whaledeck-container-permission-business-plan-v1");
    }
    public string FingerprintPermissionExecution(ContainerPermissionExecutionBinding binding)
    {
        ContainerPermissionWorkflowValidation.Execution(binding);
        return Mac(ContainerPermissionWorkflowValidation.Serialize(binding), "whaledeck-container-permission-business-execution-v1");
    }
    public string SignPermissionConfirmation(ContainerPermissionConfirmation claims)
    {
        ContainerPermissionWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerPermissionWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, PermissionConfirmationDomain);
    }
    public ContainerPermissionConfirmation VerifyPermissionConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_PERMISSION_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], PermissionConfirmationDomain)))) throw new ArgumentException("CONTAINER_PERMISSION_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerPermissionConfirmation>(raw, ContainerPermissionWorkflowValidation.Json) ?? throw ContainerPermissionWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerPermissionWorkflowValidation.Serialize(value))))
                throw ContainerPermissionWorkflowValidation.Invalid();
            ContainerPermissionWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_PERMISSION_CONFIRMATION_INVALID"); }
    }
}
