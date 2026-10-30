using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerOwnershipProtector
{
    private const string OwnershipConfirmationDomain = "whaledeck-container-ownership-business-confirmation-v1";
    public string FingerprintOwnershipRequest(ContainerOwnershipRequestBinding binding)
    {
        ContainerOwnershipWorkflowValidation.Request(binding);
        return Mac(ContainerOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-ownership-business-request-v1");
    }
    public string FingerprintOwnershipPlan(ContainerOwnershipPlanBinding binding)
    {
        ContainerOwnershipWorkflowValidation.Plan(binding);
        return Mac(ContainerOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-ownership-business-plan-v1");
    }
    public string FingerprintOwnershipExecution(ContainerOwnershipExecutionBinding binding)
    {
        ContainerOwnershipWorkflowValidation.Execution(binding);
        return Mac(ContainerOwnershipWorkflowValidation.Serialize(binding), "whaledeck-container-ownership-business-execution-v1");
    }
    public string SignOwnershipConfirmation(ContainerOwnershipConfirmation claims)
    {
        ContainerOwnershipWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerOwnershipWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, OwnershipConfirmationDomain);
    }
    public ContainerOwnershipConfirmation VerifyOwnershipConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_OWNERSHIP_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], OwnershipConfirmationDomain)))) throw new ArgumentException("CONTAINER_OWNERSHIP_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerOwnershipConfirmation>(raw, ContainerOwnershipWorkflowValidation.Json) ?? throw ContainerOwnershipWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerOwnershipWorkflowValidation.Serialize(value))))
                throw ContainerOwnershipWorkflowValidation.Invalid();
            ContainerOwnershipWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_OWNERSHIP_CONFIRMATION_INVALID"); }
    }
}
