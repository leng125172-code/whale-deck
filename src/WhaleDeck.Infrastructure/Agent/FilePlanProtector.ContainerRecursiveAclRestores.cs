using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecursiveAclRestoreProtector
{
    private const string RecursiveAclRestoreConfirmationDomain = "whaledeck-container-recursive-acl-restore-business-confirmation-v1";
    public string FingerprintRecursiveAclRestoreRequest(ContainerRecursiveAclRestoreRequestBinding binding)
    {
        ContainerRecursiveAclRestoreWorkflowValidation.Request(binding);
        return Mac(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-restore-business-request-v1");
    }
    public string FingerprintRecursiveAclRestorePlan(ContainerRecursiveAclRestorePlanBinding binding)
    {
        ContainerRecursiveAclRestoreWorkflowValidation.Plan(binding);
        return Mac(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-restore-business-plan-v1");
    }
    public string FingerprintRecursiveAclRestoreExecution(ContainerRecursiveAclRestoreExecutionBinding binding)
    {
        ContainerRecursiveAclRestoreWorkflowValidation.Execution(binding);
        return Mac(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-restore-business-execution-v1");
    }
    public string FingerprintRecursiveAclRestoreReceipt(ContainerRecursiveAclRestoreReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecursiveAclRestoreWorkflowValidation.Invalid();
        ContainerRecursiveAclRestoreValidation.Operation(binding.Operation);
        ContainerRecursiveAclRestoreValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recursive-acl-restore-business-receipt-v1");
    }
    public string SignRecursiveAclRestoreConfirmation(ContainerRecursiveAclRestoreConfirmation claims)
    {
        ContainerRecursiveAclRestoreWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecursiveAclRestoreConfirmationDomain);
    }
    public ContainerRecursiveAclRestoreConfirmation VerifyRecursiveAclRestoreConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecursiveAclRestoreConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecursiveAclRestoreConfirmation>(raw, ContainerRecursiveAclRestoreWorkflowValidation.Json) ?? throw ContainerRecursiveAclRestoreWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecursiveAclRestoreWorkflowValidation.Serialize(value))))
                throw ContainerRecursiveAclRestoreWorkflowValidation.Invalid();
            ContainerRecursiveAclRestoreWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONFIRMATION_INVALID"); }
    }
}
