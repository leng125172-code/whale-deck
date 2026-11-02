using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerAclProtector
{
    private const string AclConfirmationDomain = "whaledeck-container-acl-business-confirmation-v1";
    public string FingerprintAclRequest(ContainerAclRequestBinding binding)
    {
        ContainerAclWorkflowValidation.Request(binding);
        return Mac(ContainerAclWorkflowValidation.Serialize(binding), "whaledeck-container-acl-business-request-v1");
    }
    public string FingerprintAclPlan(ContainerAclPlanBinding binding)
    {
        ContainerAclWorkflowValidation.Plan(binding);
        return Mac(ContainerAclWorkflowValidation.Serialize(binding), "whaledeck-container-acl-business-plan-v1");
    }
    public string FingerprintAclExecution(ContainerAclExecutionBinding binding)
    {
        ContainerAclWorkflowValidation.Execution(binding);
        return Mac(ContainerAclWorkflowValidation.Serialize(binding), "whaledeck-container-acl-business-execution-v1");
    }
    public string FingerprintAclReceipt(ContainerAclReceiptBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ContainerAclValidation.Operation(binding.Operation); ContainerAclValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerAclWorkflowValidation.Serialize(binding), "whaledeck-container-acl-business-receipt-v1");
    }
    public string SignAclConfirmation(ContainerAclConfirmation claims)
    {
        ContainerAclWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerAclWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, AclConfirmationDomain);
    }
    public ContainerAclConfirmation VerifyAclConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_ACL_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], AclConfirmationDomain)))) throw new ArgumentException("CONTAINER_ACL_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerAclConfirmation>(raw, ContainerAclWorkflowValidation.Json) ?? throw ContainerAclWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerAclWorkflowValidation.Serialize(value))))
                throw ContainerAclWorkflowValidation.Invalid();
            ContainerAclWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_ACL_CONFIRMATION_INVALID"); }
    }
}
