using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecycleArchiveProtector
{
    private const string RecycleArchiveConfirmationDomain = "whaledeck-container-recycle-archive-business-confirmation-v1";
    public string FingerprintRecycleArchiveSource(ContainerRecycleArchiveSourceBinding binding)
    {
        if (binding is null || binding.IntentId == Guid.Empty || !FileManagementService.Hex(binding.RequestHash) || !FileManagementService.Hex(binding.PlanHash) ||
            !FileManagementService.Hex(binding.ExecutionHash) || !FileManagementService.Hex(binding.ReceiptHash) || !FileManagementService.Hex(binding.OriginalProofHash)) throw ContainerRecycleArchiveWorkflowValidation.Invalid();
        ContainerRecycleArchiveValidation.Command(binding.Command);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-business-source-v1");
    }
    public string FingerprintRecycleArchiveRequest(ContainerRecycleArchiveRequestBinding binding)
    {
        ContainerRecycleArchiveWorkflowValidation.Request(binding);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-business-request-v1");
    }
    public string FingerprintRecycleArchivePlan(ContainerRecycleArchivePlanBinding binding)
    {
        ContainerRecycleArchiveWorkflowValidation.Plan(binding);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-business-plan-v1");
    }
    public string FingerprintRecycleArchiveExecution(ContainerRecycleArchiveExecutionBinding binding)
    {
        ContainerRecycleArchiveWorkflowValidation.Execution(binding);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-business-execution-v1");
    }
    public string FingerprintRecycleArchiveReceipt(ContainerRecycleArchiveReceiptBinding binding)
    {
        if (binding is null) throw ContainerRecycleArchiveWorkflowValidation.Invalid();
        ContainerRecycleArchiveValidation.Operation(binding.Operation);
        ContainerRecycleArchiveValidation.Receipt(binding.Operation, binding.Receipt);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-business-receipt-v1");
    }
    public string SignRecycleArchiveConfirmation(ContainerRecycleArchiveConfirmation claims)
    {
        ContainerRecycleArchiveWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerRecycleArchiveWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, RecycleArchiveConfirmationDomain);
    }
    public ContainerRecycleArchiveConfirmation VerifyRecycleArchiveConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], RecycleArchiveConfirmationDomain)))) throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerRecycleArchiveConfirmation>(raw, ContainerRecycleArchiveWorkflowValidation.Json) ?? throw ContainerRecycleArchiveWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerRecycleArchiveWorkflowValidation.Serialize(value))))
                throw ContainerRecycleArchiveWorkflowValidation.Invalid();
            ContainerRecycleArchiveWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_CONFIRMATION_INVALID"); }
    }
}
