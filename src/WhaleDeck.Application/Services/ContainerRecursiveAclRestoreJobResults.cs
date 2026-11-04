using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Historical task facts only. No commands, paths, identities, hashes, ACL entries or capabilities.</summary>
public static class ContainerRecursiveAclRestoreJobResults
{
    private const string Prefix = "CONTAINER_RECURSIVE_ACL_RESTORE_";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4, PropertyNameCaseInsensitive = false };
    public sealed record Summary(int SchemaVersion, Guid IntentId, Guid JobId, string Stage, string State, string Code,
        string? AclType, uint? EntryCount, uint? FileCount, uint? DirectoryCount, uint? ChangeCount,
        int? CompletedCount, int? ChangedCount, int? AbortedCount, int? NotStartedCount, int? UnknownCount,
        string? ReceiptState, bool? BindingCurrent, bool HistoryMayExist, bool HistoryVerified, bool? HistoryAvailable,
        bool PublishBarrier, uint? SourceEntryCount, uint? SourceFileCount, uint? SourceDirectoryCount, uint? SourceChangedCount);

    public static string Snapshot(ContainerRecursiveAclRestoreIntent row, bool execution)
    {
        var plan = row.PlanJson is null ? null : ContainerRecursiveAclRestoreWorkflowValidation.ReadPlan(row);
        var source = plan?.RestoreSource;
        return JsonSerializer.Serialize(new Summary(1, row.Id, execution ? row.ExecutionJobId!.Value : row.Id,
            execution ? "execution" : "planning", row.State, row.ResultCode!, source?.ChangedAclType,
            plan?.EntryCount, plan?.FileCount, plan?.DirectoryCount, plan?.ChangeCount,
            row.CompletedCount, row.ChangedCount, row.AbortedCount, row.NotStartedCount, row.UnknownCount,
            row.RootState, row.BindingCurrent, row.HistoryMayExist, row.HistoryVerified, row.HistoryAvailable,
            row.PublishBarrier, source?.EntryCount, source?.FileCount, source?.DirectoryCount, source?.ChangeCount), Json);
    }

    public static string? ForJob(OperationJob job)
    {
        if (job.JobType is not ("containers.files.recursive-acl-restore" or "containers.files.recursive-acl-restore-plan") || job.ResultJson is null || job.ResultJson.Length > 4096) return null;
        try
        {
            using var document = JsonDocument.Parse(job.ResultJson, new() { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != 26 || names.Distinct(StringComparer.Ordinal).Count() != 26) return null;
            var value = JsonSerializer.Deserialize<Summary>(job.ResultJson, Json);
            if (value is null || !Valid(value, job)) return null;
            return JsonSerializer.Serialize(value, Json);
        }
        catch (JsonException) { return null; }
    }

    private static bool Valid(Summary s, OperationJob job)
    {
        var execution = job.JobType == "containers.files.recursive-acl-restore";
        if (s.SchemaVersion != 1 || s.IntentId == Guid.Empty || s.JobId != job.Id ||
            job.Id != (execution ? ContainerRecursiveAclRestoreWorkflowValidation.ExecutionId(s.IntentId) : s.IntentId) ||
            s.Stage != (execution ? "execution" : "planning") ||
            (execution ? s.State is not ("Succeeded" or "Superseded" or "Canceled" or "Failed" or "Partial" or "Unknown" or "Acknowledged") : s.State is not ("Ready" or "Expired" or "Canceled" or "Failed")) ||
            job.State != (s.State is "Ready" or "Succeeded" ? "Succeeded" : s.State is "Partial" or "Unknown" ? "Running" : s.State == "Canceled" ? "Canceled" : "Failed") ||
            s.Code is null || s.Code.Length <= Prefix.Length || s.Code.Length > 64 || !s.Code.StartsWith(Prefix, StringComparison.Ordinal) || s.Code.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')) || !CodeMatches(s) ||
            s.AclType is not (null or "access" or "default") || s.ReceiptState is not (null or "Completed" or "Aborted" or "Partial" or "Unknown") ||
            s.HistoryVerified && !s.HistoryMayExist || s.HistoryAvailable == true && !s.HistoryVerified) return false;
        if (!Counts(s.EntryCount, s.FileCount, s.DirectoryCount, s.ChangeCount) || !Counts(s.SourceEntryCount, s.SourceFileCount, s.SourceDirectoryCount, s.SourceChangedCount)) return false;
        if ((s.EntryCount is null) != (s.AclType is null) || s.EntryCount != s.SourceEntryCount || s.FileCount != s.SourceFileCount || s.DirectoryCount != s.SourceDirectoryCount) return false;
        int?[] actual = [s.CompletedCount, s.ChangedCount, s.AbortedCount, s.NotStartedCount, s.UnknownCount];
        if (s.ReceiptState is null) return actual.All(n => n is null) && s.BindingCurrent is null && s.HistoryAvailable is null &&
            (execution ? s.State == "Unknown" : !s.PublishBarrier && !s.HistoryVerified && !s.HistoryMayExist && (s.State != "Ready" || s.EntryCount is not null));
        if (!execution || s.EntryCount is null || actual.Any(n => n is null or < 0 or > 10000) || s.BindingCurrent is null || s.HistoryAvailable is null ||
            s.ChangedCount > s.CompletedCount || s.ChangedCount > s.ChangeCount || (long)s.CompletedCount!.Value + s.AbortedCount!.Value + s.NotStartedCount!.Value + s.UnknownCount!.Value != s.EntryCount) return false;
        return s.ReceiptState == (s.State switch { "Succeeded" or "Superseded" => "Completed", "Canceled" or "Failed" => "Aborted", "Partial" => "Partial", "Unknown" => "Unknown", _ => s.ReceiptState }) &&
            (s.State != "Acknowledged" || s.ReceiptState is "Partial" or "Unknown") &&
            (s.ReceiptState != "Completed" || s.CompletedCount == s.EntryCount && s.ChangedCount == s.ChangeCount) &&
            (s.ReceiptState != "Aborted" || s.ChangedCount == 0 && s.UnknownCount == 0 && s.BindingCurrent == false) &&
            (s.ReceiptState != "Partial" || s.ChangedCount > 0 && s.CompletedCount < s.EntryCount && s.UnknownCount == 0 && s.HistoryVerified) &&
            (s.ReceiptState != "Unknown" || s.UnknownCount > 0) &&
            (s.BindingCurrent != true || s.ReceiptState == "Completed" && s.HistoryAvailable == true) &&
            (s.ReceiptState != "Completed" || s.HistoryVerified) &&
            (!s.PublishBarrier || s.State is "Partial" or "Unknown") &&
            (s.State != "Succeeded" || s.BindingCurrent == true) && (s.State != "Superseded" || s.BindingCurrent == false);
    }
    private static bool Counts(uint? count, uint? files, uint? directories, uint? changed) =>
        count is null ? files is null && directories is null && changed is null : count is >= 1 and <= 10000 && files <= count && directories >= 1 && directories <= count && files + directories == count && changed <= count;
    private static bool CodeMatches(Summary s)
    {
        var suffix = s.Code[Prefix.Length..];
        return s.State switch
        {
            "Ready" => suffix == "READY", "Expired" => suffix == "PLAN_EXPIRED", "Succeeded" => suffix == "COMPLETED",
            "Superseded" => suffix == "TARGET_CHANGED", "Canceled" => suffix == "CANCELED",
            "Partial" => suffix == "PARTIAL_REQUIRES_ACKNOWLEDGEMENT",
            "Unknown" => suffix is "RESULT_UNKNOWN" or "OUTPUT_INVALID" or "BINDING_CHANGED",
            "Acknowledged" => suffix == (s.ReceiptState == "Partial" ? "PARTIAL_ACKNOWLEDGED" : "UNKNOWN_ACKNOWLEDGED"),
            "Failed" => suffix is not ("READY" or "PLAN_EXPIRED" or "COMPLETED" or "TARGET_CHANGED" or "CANCELED" or "PARTIAL_REQUIRES_ACKNOWLEDGEMENT" or "RESULT_UNKNOWN" or "PARTIAL_ACKNOWLEDGED" or "UNKNOWN_ACKNOWLEDGED"),
            _ => false
        };
    }
}
