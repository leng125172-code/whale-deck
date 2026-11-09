using System.Text;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecycleArchiveRetirementProtector
{
    private const string RetirementBusinessDomain = "whaledeck-container-recycle-archive-retirement-workflow-v1:";

    public string FingerprintRetirementSource(ContainerRecycleArchiveRetirementSourceBinding binding)
    {
        if (binding.ArchiveIntentId == Guid.Empty || !FileManagementService.Hex(binding.RequestHash) || !FileManagementService.Hex(binding.PlanHash) ||
            !FileManagementService.Hex(binding.ExecutionHash) || !FileManagementService.Hex(binding.ReservationHash) ||
            !FileManagementService.Hex(binding.ReceiptHash) || !FileManagementService.Hex(binding.ArchiveSourceProofHash))
            throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_INVALID");
        ContainerRecycleArchiveRetirementValidation.Command(binding.Command);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-retirement-business-source-v1");
    }

    public string FingerprintRetirementRequest(ContainerRecycleArchiveRetirementIntent row)
    {
        Retirement.Request(row);
        return RetirementMac("request", new { row.Id, row.ArchiveIntentId, row.RetainDays, row.SourceProofHash, row.IdempotencyKey,
            row.ActorSubject, row.LoginBinding, row.TraceId, row.OwnerHash, row.CreatedAtUtc, row.PlanningExpiresAtUtc });
    }

    public string FingerprintRetirementPlan(ContainerRecycleArchiveRetirementIntent row)
    {
        var plan = Retirement.ReadPlan(row);
        return RetirementMac("plan", new { row.Id, row.RequestHash, row.ReadyExpiresAtUtc, plan });
    }

    public string FingerprintRetirementExecution(ContainerRecycleArchiveRetirementIntent row) =>
        RetirementMac("execution", new { row.Id, row.PlanHash, row.ExecutionJobId, row.ConfirmedAtUtc, row.ExecutionExpiresAtUtc });

    public string FingerprintRetirementReceipt(ContainerRecycleArchiveRetirementOperationDto operation, ContainerRecycleArchiveRetirementReceiptDto receipt)
    {
        ContainerRecycleArchiveRetirementValidation.Operation(operation);
        ContainerRecycleArchiveRetirementValidation.Receipt(operation, receipt);
        var planHash = FileManagementService.Hash(Retirement.Write(operation.Plan));
        return RetirementMac("receipt", new { operation.Id, operation.Owner, operation.Context, planHash, receipt });
    }

    public string SignRetirementConfirmation(RetirementConfirmation claims)
    {
        Retirement.Confirmation(claims);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Retirement.Write(claims)));
        return payload + "." + Mac(payload, RetirementBusinessDomain + "confirmation");
    }

    public RetirementConfirmation VerifyRetirementConfirmation(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) throw Retirement.Invalid();
        var fields = token.Split('.');
        if (fields.Length != 2 || !Retirement.EqualMac(fields[1], Mac(fields[0], RetirementBusinessDomain + "confirmation"))) throw Retirement.Invalid();
        try
        {
            var bytes = Convert.FromBase64String(fields[0]);
            if (Convert.ToBase64String(bytes) != fields[0]) throw Retirement.Invalid();
            var value = Retirement.Read<RetirementConfirmation>(Encoding.UTF8.GetString(bytes));
            Retirement.Confirmation(value);
            return value;
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException) { throw Retirement.Invalid(); }
    }

    private string RetirementMac<T>(string purpose, T value) => Mac(Retirement.Write(value), RetirementBusinessDomain + purpose);
}
