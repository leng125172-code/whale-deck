using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecursiveAclProtector
{
    private const string RecursiveAclConfirmationDomain = "whaledeck-container-recursive-acl-business-confirmation-v1";
    public string FingerprintRecursiveAclRequest(ContainerRecursiveAclRequestBinding binding)
    {
        ContainerRecursiveAclWorkflowValidation.Request(binding);
        return Mac(ContainerRecursiveAclWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-business-request-v1");
    }
    public string FingerprintRecursiveAclPlan(ContainerRecursiveAclPlanBinding binding)
    {
        ContainerRecursiveAclWorkflowValidation.Plan(binding);
        return Mac(ContainerRecursiveAclWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-business-plan-v1");
    }
    public string FingerprintRecursiveAclExecution(ContainerRecursiveAclExecutionBinding binding)
    {
        ContainerRecursiveAclWorkflowValidation.Execution(binding);
        return Mac(ContainerRecursiveAclWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-business-execution-v1");
    }
    public string FingerprintRecursiveAclReceipt(ContainerRecursiveAclReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecursiveAclWorkflowValidation.Invalid();
        ContainerRecursiveAclValidation.Operation(binding.Operation);
        ContainerRecursiveAclValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecursiveAclWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-business-receipt-v1");
    }
    public string SignRecursiveAclConfirmation(ContainerRecursiveAclConfirmation claims)
    {
        ContainerRecursiveAclWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecursiveAclWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecursiveAclConfirmationDomain);
    }
    public ContainerRecursiveAclConfirmation VerifyRecursiveAclConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecursiveAclConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecursiveAclConfirmation>(raw, ContainerRecursiveAclWorkflowValidation.Json) ?? throw ContainerRecursiveAclWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecursiveAclWorkflowValidation.Serialize(value))))
                throw ContainerRecursiveAclWorkflowValidation.Invalid();
            ContainerRecursiveAclWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECURSIVE_ACL_CONFIRMATION_INVALID"); }
    }
}
