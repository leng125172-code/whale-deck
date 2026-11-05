using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecycleProtector
{
    private const string RecycleConfirmationDomain = "whaledeck-container-recycle-business-confirmation-v1";
    public string FingerprintRecycleRequest(ContainerRecycleRequestBinding binding)
    {
        ContainerRecycleWorkflowValidation.Request(binding);
        return Mac(ContainerRecycleWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-business-request-v1");
    }
    public string FingerprintRecyclePlan(ContainerRecyclePlanBinding binding)
    {
        ContainerRecycleWorkflowValidation.Plan(binding);
        return Mac(ContainerRecycleWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-business-plan-v1");
    }
    public string FingerprintRecycleExecution(ContainerRecycleExecutionBinding binding)
    {
        ContainerRecycleWorkflowValidation.Execution(binding);
        return Mac(ContainerRecycleWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-business-execution-v1");
    }
    public string FingerprintRecycleReceipt(ContainerRecycleReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecycleWorkflowValidation.Invalid();
        ContainerRecycleValidation.Operation(binding.Operation);
        ContainerRecycleValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecycleWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-business-receipt-v1");
    }
    public string SignRecycleConfirmation(ContainerRecycleConfirmation claims)
    {
        ContainerRecycleWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecycleWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecycleConfirmationDomain);
    }
    public ContainerRecycleConfirmation VerifyRecycleConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECYCLE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecycleConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECYCLE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecycleConfirmation>(raw, ContainerRecycleWorkflowValidation.Json) ?? throw ContainerRecycleWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecycleWorkflowValidation.Serialize(value))))
                throw ContainerRecycleWorkflowValidation.Invalid();
            ContainerRecycleWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECYCLE_CONFIRMATION_INVALID"); }
    }
}
