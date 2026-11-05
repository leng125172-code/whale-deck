using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecycleRestoreProtector
{
    private const string RecycleRestoreConfirmationDomain = "whaledeck-container-recycle-restore-business-confirmation-v1";
    public string FingerprintRecycleRestoreSource(ContainerRecycleRestoreSourceBinding binding)
    {
        if (binding is null || binding.IntentId == Guid.Empty || !FileManagementService.Hex(binding.RequestHash) || !FileManagementService.Hex(binding.PlanHash) ||
            !FileManagementService.Hex(binding.ExecutionHash) || !FileManagementService.Hex(binding.ReceiptHash)) throw ContainerRecycleRestoreWorkflowValidation.Invalid();
        ContainerRecycleRestoreValidation.Command(binding.Command);
        return Mac(ContainerRecycleRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-restore-business-source-v1");
    }
    public string FingerprintRecycleRestoreRequest(ContainerRecycleRestoreRequestBinding binding)
    {
        ContainerRecycleRestoreWorkflowValidation.Request(binding);
        return Mac(ContainerRecycleRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-restore-business-request-v1");
    }
    public string FingerprintRecycleRestorePlan(ContainerRecycleRestorePlanBinding binding)
    {
        ContainerRecycleRestoreWorkflowValidation.Plan(binding);
        return Mac(ContainerRecycleRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-restore-business-plan-v1");
    }
    public string FingerprintRecycleRestoreExecution(ContainerRecycleRestoreExecutionBinding binding)
    {
        ContainerRecycleRestoreWorkflowValidation.Execution(binding);
        return Mac(ContainerRecycleRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-restore-business-execution-v1");
    }
    public string FingerprintRecycleRestoreReceipt(ContainerRecycleRestoreReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecycleRestoreWorkflowValidation.Invalid();
        ContainerRecycleRestoreValidation.Operation(binding.Operation);
        ContainerRecycleRestoreValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecycleRestoreWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-restore-business-receipt-v1");
    }
    public string SignRecycleRestoreConfirmation(ContainerRecycleRestoreConfirmation claims)
    {
        ContainerRecycleRestoreWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecycleRestoreWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecycleRestoreConfirmationDomain);
    }
    public ContainerRecycleRestoreConfirmation VerifyRecycleRestoreConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecycleRestoreConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecycleRestoreConfirmation>(raw, ContainerRecycleRestoreWorkflowValidation.Json) ?? throw ContainerRecycleRestoreWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecycleRestoreWorkflowValidation.Serialize(value))))
                throw ContainerRecycleRestoreWorkflowValidation.Invalid();
            ContainerRecycleRestoreWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECYCLE_RESTORE_CONFIRMATION_INVALID"); }
    }
}
