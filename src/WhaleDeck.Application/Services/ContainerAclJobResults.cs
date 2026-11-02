using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Bounded task-center facts. Never serialize a command, plan, identity, path or capability.</summary>
public static class ContainerAclJobResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4, PropertyNameCaseInsensitive = false };

    public sealed record Summary(int SchemaVersion, Guid IntentId, string Stage, string State, string Code,
        string AclType, string? Kind, uint? TargetMode, bool? NoChange, bool? AclConfirmed,
        string? ReceiptState, bool? BindingCurrent, bool HistoryMayExist, bool HistoryVerified, bool HasPublishedVersion);

    public static string Snapshot(ContainerAclIntent row, bool execution)
    {
        var command = ContainerAclWorkflowValidation.ReadCommand(row);
        var plan = row.PlanJson is null ? null : ContainerAclWorkflowValidation.ReadPlan(row);
        var receipt = ContainerAclWorkflowValidation.ReadReceipt(row);
        return JsonSerializer.Serialize(new Summary(1, row.Id, execution ? "execution" : "planning", row.State,
            row.ResultCode!, command.AclType, plan?.Kind, plan?.TargetMode, plan?.NoChange, row.AclConfirmed,
            receipt?.State, receipt?.BindingCurrent, row.HistoryMayExist, row.HistoryVerified, row.PublishedVersion is not null), Json);
    }

    // Fail closed for missing/old/corrupt results; do not expose arbitrary JSON under an ACL job type.
    public static string? ForJob(OperationJob job)
    {
        if (job.ResultJson is null || job.ResultJson.Length > 4096) return null;
        try
        {
            using var document = JsonDocument.Parse(job.ResultJson, new() { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != 15 || names.Distinct(StringComparer.Ordinal).Count() != 15) return null;
            var value = JsonSerializer.Deserialize<Summary>(job.ResultJson, Json);
            if (value is null || value.SchemaVersion != 1 || value.IntentId == Guid.Empty ||
                value.Stage != (job.JobType == "containers.files.acl" ? "execution" : "planning") ||
                job.Id != (value.Stage == "execution" ? ContainerAclWorkflowValidation.ExecutionId(value.IntentId) : value.IntentId) ||
                value.State is not ("Ready" or "Expired" or "Succeeded" or "Superseded" or "Canceled" or "Failed" or "Unknown" or "Acknowledged") ||
                job.State != (value.State == "Ready" || value.State == "Succeeded" ? "Succeeded" : value.State == "Unknown" ? "Running" : value.State == "Canceled" ? "Canceled" : "Failed") ||
                value.Code is null || value.Code.Length is < 1 or > 64 || !value.Code.StartsWith("CONTAINER_ACL_", StringComparison.Ordinal) ||
                value.Code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')) || value.AclType is not ("access" or "default") ||
                value.Kind is not (null or "File" or "Directory") || value.TargetMode > 511 ||
                value.ReceiptState is not (null or "Completed" or "Aborted" or "Unknown")) return null;
            return JsonSerializer.Serialize(value, Json);
        }
        catch (JsonException) { return null; }
    }
}
