using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Historical, bounded facts only; never expose the command, path, actors, hashes or authority.</summary>
public static class ContainerRecycleJobResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 4 };
    public sealed record Summary(int SchemaVersion, Guid IntentId, Guid JobId, string Stage, string State, string Code,
        string? ReceiptState, bool? BindingCurrent, bool? SourceRetained, bool? SourceRestored,
        bool HistoryMayExist, bool HistoryVerified, bool? HistoryAvailable, bool PublishBarrier);
    public static string Snapshot(ContainerRecycleIntent row, bool execution) => JsonSerializer.Serialize(new Summary(1,row.Id,
        execution ? row.ExecutionJobId!.Value : row.Id,execution ? "execution" : "planning",row.State,row.ResultCode!,
        row.RootState,row.BindingCurrent,row.SourceRetained,row.SourceRestored,row.HistoryMayExist,row.HistoryVerified,row.HistoryAvailable,row.PublishBarrier),Json);
    public static string? ForJob(OperationJob job)
    {
        if (job.JobType is not ("containers.files.recycle" or "containers.files.recycle-plan") || job.ResultJson is not { Length: > 0 and <= 4096 }) return null;
        try
        {
            using var doc = JsonDocument.Parse(job.ResultJson,new(){MaxDepth=4});
            if (doc.RootElement.ValueKind!=JsonValueKind.Object) return null;
            var names=doc.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
            if(names.Length!=14 || names.Distinct(StringComparer.Ordinal).Count()!=14) return null;
            var s=JsonSerializer.Deserialize<Summary>(job.ResultJson,Json);
            if(s is null || !Valid(s,job)) return null;
            return JsonSerializer.Serialize(s,Json);
        }
        catch(JsonException){return null;}
    }
    private static bool Valid(Summary s,OperationJob job)
    {
        var execution=job.JobType=="containers.files.recycle";
        if(s.SchemaVersion!=1 || s.IntentId==Guid.Empty || s.JobId!=job.Id ||
            job.Id!=(execution?ContainerRecycleWorkflowValidation.ExecutionId(s.IntentId):s.IntentId) || s.Stage!=(execution?"execution":"planning") ||
            job.State!=(s.State is "Ready" or "Succeeded"?"Succeeded":s.State=="Unknown"?"Running":s.State=="Canceled"?"Canceled":"Failed") ||
            s.Code is null || s.Code.Length>64 || !s.Code.StartsWith("CONTAINER_RECYCLE_",StringComparison.Ordinal) || s.Code.Length<=18 || s.Code.Any(c=>!(char.IsAsciiLetterUpper(c)||c=='_')) ||
            s.HistoryVerified && !s.HistoryMayExist || s.HistoryAvailable==true && !s.HistoryVerified) return false;
        if(!execution) return (s.State is "Ready" or "Expired" or "Canceled" or "Failed") &&
            s.ReceiptState is null && s.BindingCurrent is null && s.SourceRetained is null && s.SourceRestored is null && s.HistoryAvailable is null &&
            !s.HistoryMayExist && !s.HistoryVerified && !s.PublishBarrier && (s.State switch{
                "Ready"=>s.Code=="CONTAINER_RECYCLE_READY","Expired"=>s.Code=="CONTAINER_RECYCLE_PLAN_EXPIRED","Canceled"=>s.Code=="CONTAINER_RECYCLE_CANCELED",_=>true});
        if(s.State is not ("Succeeded" or "Superseded" or "Canceled" or "Failed" or "Unknown" or "Acknowledged") || s.State!="Unknown" && s.PublishBarrier) return false;
        if(s.ReceiptState is null) return s.State=="Unknown" && s.SourceRetained is null && s.SourceRestored is null && s.BindingCurrent is null && s.HistoryAvailable is null && !s.HistoryVerified && UnknownCode(s.Code);
        if(s.BindingCurrent is null || s.HistoryAvailable is null || s.ReceiptState is not ("Trashed" or "Restored" or "Aborted" or "Unknown") ||
            s.BindingCurrent==true && (s.ReceiptState is not ("Trashed" or "Restored") || s.HistoryAvailable!=true) ||
            s.ReceiptState is "Trashed" or "Restored" && !s.HistoryVerified ||
            (s.ReceiptState=="Unknown" ? s.SourceRetained is not null || s.SourceRestored is not null : s.SourceRetained!=(s.ReceiptState=="Trashed") || s.SourceRestored!=(s.ReceiptState=="Restored"))) return false;
        return s.State switch{
            "Succeeded"=>s.ReceiptState=="Trashed" && s.BindingCurrent==true && s.Code=="CONTAINER_RECYCLE_TRASHED",
            "Superseded"=>s.ReceiptState=="Restored"?s.Code=="CONTAINER_RECYCLE_ALREADY_RESTORED":s.ReceiptState=="Trashed" && s.BindingCurrent==false && s.Code=="CONTAINER_RECYCLE_TARGET_CHANGED",
            "Unknown"=>s.ReceiptState=="Unknown" && UnknownCode(s.Code),
            "Acknowledged"=>s.Code=="CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED",
            "Canceled"=>s.ReceiptState=="Aborted" && s.Code=="CONTAINER_RECYCLE_CANCELED",
            _=>s.ReceiptState=="Aborted" && s.Code=="CONTAINER_RECYCLE_INTERRUPTED"};
    }
    private static bool UnknownCode(string code)=>code is "CONTAINER_RECYCLE_RESULT_UNKNOWN" or "CONTAINER_RECYCLE_BINDING_CHANGED" or "CONTAINER_RECYCLE_OUTPUT_INVALID";
}
