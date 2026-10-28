using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerCopyProtector
{
    private const string CopyConfirmationDomain = "whaledeck-container-copy-business-confirmation-v1";
    public string FingerprintCopyRequest(ContainerCopyRequestBinding binding)
    {
        ContainerCopyWorkflowValidation.Request(binding);
        return Mac(ContainerCopyWorkflowValidation.Serialize(binding), "whaledeck-container-copy-business-request-v1");
    }
    public string FingerprintCopyPlan(ContainerCopyPlanBinding binding)
    {
        ContainerCopyWorkflowValidation.Plan(binding);
        return Mac(ContainerCopyWorkflowValidation.Serialize(binding), "whaledeck-container-copy-business-plan-v1");
    }
    public string FingerprintCopyExecution(ContainerCopyExecutionBinding binding)
    {
        ContainerCopyWorkflowValidation.Execution(binding);
        return Mac(ContainerCopyWorkflowValidation.Serialize(binding), "whaledeck-container-copy-business-execution-v1");
    }
    public string SignCopyConfirmation(ContainerCopyConfirmation claims)
    {
        ContainerCopyWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerCopyWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, CopyConfirmationDomain);
    }
    public ContainerCopyConfirmation VerifyCopyConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_COPY_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], CopyConfirmationDomain)))) throw new ArgumentException("CONTAINER_COPY_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerCopyConfirmation>(raw, ContainerCopyWorkflowValidation.Json) ?? throw ContainerCopyWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerCopyWorkflowValidation.Serialize(value))))
                throw ContainerCopyWorkflowValidation.Invalid();
            ContainerCopyWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_COPY_CONFIRMATION_INVALID"); }
    }
}
