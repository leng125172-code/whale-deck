using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecursivePermissionProtector
{
    private const string RecursivePermissionConfirmationDomain = "whaledeck-container-recursive-permission-business-confirmation-v1";
    public string FingerprintRecursivePermissionRequest(ContainerRecursivePermissionRequestBinding binding)
    {
        ContainerRecursivePermissionWorkflowValidation.Request(binding);
        return Mac(ContainerRecursivePermissionWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-permission-business-request-v1");
    }
    public string FingerprintRecursivePermissionPlan(ContainerRecursivePermissionPlanBinding binding)
    {
        ContainerRecursivePermissionWorkflowValidation.Plan(binding);
        return Mac(ContainerRecursivePermissionWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-permission-business-plan-v1");
    }
    public string FingerprintRecursivePermissionExecution(ContainerRecursivePermissionExecutionBinding binding)
    {
        ContainerRecursivePermissionWorkflowValidation.Execution(binding);
        return Mac(ContainerRecursivePermissionWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-permission-business-execution-v1");
    }
    public string SignRecursivePermissionConfirmation(ContainerRecursivePermissionConfirmation claims)
    {
        ContainerRecursivePermissionWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecursivePermissionWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecursivePermissionConfirmationDomain);
    }
    public ContainerRecursivePermissionConfirmation VerifyRecursivePermissionConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecursivePermissionConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecursivePermissionConfirmation>(raw, ContainerRecursivePermissionWorkflowValidation.Json) ?? throw ContainerRecursivePermissionWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecursivePermissionWorkflowValidation.Serialize(value))))
                throw ContainerRecursivePermissionWorkflowValidation.Invalid();
            ContainerRecursivePermissionWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECURSIVE_PERMISSION_CONFIRMATION_INVALID"); }
    }
}
