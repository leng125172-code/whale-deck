using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecyclePurgeProtector
{
    private const string RecyclePurgeConfirmationDomain = "whaledeck-container-recycle-purge-business-confirmation-v1";
    public string FingerprintRecyclePurgeSource(ContainerRecyclePurgeSourceBinding binding)
    {
        if (binding is null || binding.IntentId == Guid.Empty || !FileManagementService.Hex(binding.RequestHash) || !FileManagementService.Hex(binding.PlanHash) ||
            !FileManagementService.Hex(binding.ExecutionHash) || !FileManagementService.Hex(binding.ReceiptHash)) throw ContainerRecyclePurgeWorkflowValidation.Invalid();
        ContainerRecyclePurgeValidation.Command(binding.Command);
        return Mac(ContainerRecyclePurgeWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-purge-business-source-v1");
    }
    public string FingerprintRecyclePurgeRequest(ContainerRecyclePurgeRequestBinding binding)
    {
        ContainerRecyclePurgeWorkflowValidation.Request(binding);
        return Mac(ContainerRecyclePurgeWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-purge-business-request-v1");
    }
    public string FingerprintRecyclePurgePlan(ContainerRecyclePurgePlanBinding binding)
    {
        ContainerRecyclePurgeWorkflowValidation.Plan(binding);
        return Mac(ContainerRecyclePurgeWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-purge-business-plan-v1");
    }
    public string FingerprintRecyclePurgeExecution(ContainerRecyclePurgeExecutionBinding binding)
    {
        ContainerRecyclePurgeWorkflowValidation.Execution(binding);
        return Mac(ContainerRecyclePurgeWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-purge-business-execution-v1");
    }
    public string FingerprintRecyclePurgeReceipt(ContainerRecyclePurgeReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecyclePurgeWorkflowValidation.Invalid();
        ContainerRecyclePurgeValidation.Operation(binding.Operation);
        ContainerRecyclePurgeValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecyclePurgeWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-purge-business-receipt-v1");
    }
    public string SignRecyclePurgeConfirmation(ContainerRecyclePurgeConfirmation claims)
    {
        ContainerRecyclePurgeWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecyclePurgeWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecyclePurgeConfirmationDomain);
    }
    public ContainerRecyclePurgeConfirmation VerifyRecyclePurgeConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecyclePurgeConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECYCLE_PURGE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecyclePurgeConfirmation>(raw, ContainerRecyclePurgeWorkflowValidation.Json) ?? throw ContainerRecyclePurgeWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecyclePurgeWorkflowValidation.Serialize(value))))
                throw ContainerRecyclePurgeWorkflowValidation.Invalid();
            ContainerRecyclePurgeWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECYCLE_PURGE_CONFIRMATION_INVALID"); }
    }
}
