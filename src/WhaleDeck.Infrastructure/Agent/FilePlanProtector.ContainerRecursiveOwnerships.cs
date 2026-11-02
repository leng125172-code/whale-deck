using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecursiveOwnershipProtector
{
    private const string RecursiveOwnershipConfirmationDomain = "whaledeck-container-recursive-ownership-business-confirmation-v1";
    public string FingerprintRecursiveOwnershipRequest(ContainerRecursiveOwnershipRequestBinding binding)
    {
        ContainerRecursiveOwnershipWorkflowValidation.Request(binding);
        return Mac(ContainerRecursiveOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-ownership-business-request-v1");
    }
    public string FingerprintRecursiveOwnershipPlan(ContainerRecursiveOwnershipPlanBinding binding)
    {
        ContainerRecursiveOwnershipWorkflowValidation.Plan(binding);
        return Mac(ContainerRecursiveOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-ownership-business-plan-v1");
    }
    public string FingerprintRecursiveOwnershipExecution(ContainerRecursiveOwnershipExecutionBinding binding)
    {
        ContainerRecursiveOwnershipWorkflowValidation.Execution(binding);
        return Mac(ContainerRecursiveOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-ownership-business-execution-v1");
    }
    public string SignRecursiveOwnershipConfirmation(ContainerRecursiveOwnershipConfirmation claims)
    {
        ContainerRecursiveOwnershipWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecursiveOwnershipWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecursiveOwnershipConfirmationDomain);
    }
    public ContainerRecursiveOwnershipConfirmation VerifyRecursiveOwnershipConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecursiveOwnershipConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecursiveOwnershipConfirmation>(raw, ContainerRecursiveOwnershipWorkflowValidation.Json) ?? throw ContainerRecursiveOwnershipWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecursiveOwnershipWorkflowValidation.Serialize(value))))
                throw ContainerRecursiveOwnershipWorkflowValidation.Invalid();
            ContainerRecursiveOwnershipWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECURSIVE_OWNERSHIP_CONFIRMATION_INVALID"); }
    }
}
