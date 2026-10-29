using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerMoveProtector
{
    private const string MoveConfirmationDomain = "whaledeck-container-move-business-confirmation-v1";
    public string FingerprintContainerMoveRequest(ContainerMoveRequestBinding binding)
    {
        ContainerMoveWorkflowValidation.Request(binding);
        return Mac(ContainerMoveWorkflowValidation.Serialize(binding), "whaledeck-container-move-business-request-v1");
    }
    public string FingerprintContainerMovePlan(ContainerMovePlanBinding binding)
    {
        ContainerMoveWorkflowValidation.Plan(binding);
        return Mac(ContainerMoveWorkflowValidation.Serialize(binding), "whaledeck-container-move-business-plan-v1");
    }
    public string FingerprintContainerMoveExecution(ContainerMoveExecutionBinding binding)
    {
        ContainerMoveWorkflowValidation.Execution(binding);
        return Mac(ContainerMoveWorkflowValidation.Serialize(binding), "whaledeck-container-move-business-execution-v1");
    }
    public string SignContainerMoveConfirmation(ContainerMoveConfirmation claims)
    {
        ContainerMoveWorkflowValidation.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ContainerMoveWorkflowValidation.Serialize(claims)));
        return payload + "." + Mac(payload, MoveConfirmationDomain);
    }
    public ContainerMoveConfirmation VerifyContainerMoveConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw new ArgumentException("CONTAINER_MOVE_CONFIRMATION_INVALID");
        var parts = token.Split('.');
        if (parts.Length != 2 || !FileManagementService.Hex(parts[1]) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(Mac(parts[0], MoveConfirmationDomain)))) throw new ArgumentException("CONTAINER_MOVE_SIGNATURE_INVALID");
        try
        {
            var raw = Convert.FromBase64String(parts[0]);
            var value = JsonSerializer.Deserialize<ContainerMoveConfirmation>(raw, ContainerMoveWorkflowValidation.Json) ?? throw ContainerMoveWorkflowValidation.Invalid();
            // Reject duplicate properties, ignored fields, alternate spelling/encodings and noncanonical timestamps.
            if (Convert.ToBase64String(raw) != parts[0] || !raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ContainerMoveWorkflowValidation.Serialize(value))))
                throw ContainerMoveWorkflowValidation.Invalid();
            ContainerMoveWorkflowValidation.Confirmation(value); return value;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { throw new ArgumentException("CONTAINER_MOVE_CONFIRMATION_INVALID"); }
    }
}
