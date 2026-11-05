using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

/// <summary>Fixed historical task facts, never command/owner/path/authority or private plan metadata.</summary>
public static class ContainerRecycleRestoreJobResults
{
    private const string Prefix="CONTAINER_RECYCLE_RESTORE_";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web)
    {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,PropertyNameCaseInsensitive=false,MaxDepth=4};
    public sealed record Summary(int SchemaVersion,Guid IntentId,Guid JobId,Guid SourceIntentId,Guid SourceOperationId,string Stage,string State,string Code,
        string? ReceiptState,string? SourceState,bool? BindingCurrent,bool? SourceRetained,bool? SourceRestored,bool? RestoredByThisOperation,
        bool HistoryVerified,bool? HistoryAvailable,bool PublishBarrier);
    public static string Snapshot(ContainerRecycleRestoreIntent row,bool execution) => JsonSerializer.Serialize(new Summary(1,row.Id,
        execution?row.ExecutionJobId!.Value:row.Id,row.SourceIntentId,ContainerRecycleRestoreWorkflowValidation.ReadCommand(row).Source.OperationId,
        execution?"execution":"planning",row.State,row.ResultCode!,row.RootState,row.SourceState,row.BindingCurrent,row.SourceRetained,row.SourceRestored,row.RestoredByThisOperation,row.HistoryVerified,row.HistoryAvailable,row.PublishBarrier),Json);
    public static string? ForJob(OperationJob job)
    {
        if(job.JobType is not ("containers.files.recycle-restore" or "containers.files.recycle-restore-plan")||job.ResultJson is not {Length:>0 and <=4096})return null;
        try
        {
            using var doc=JsonDocument.Parse(job.ResultJson,new(){MaxDepth=4});if(doc.RootElement.ValueKind!=JsonValueKind.Object)return null;
            var names=doc.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();if(names.Length!=17||names.Distinct(StringComparer.Ordinal).Count()!=17)return null;
            var value=JsonSerializer.Deserialize<Summary>(job.ResultJson,Json);return value is not null&&Valid(value,job)?JsonSerializer.Serialize(value,Json):null;
        }
        catch(JsonException){return null;}
    }
    private static bool Valid(Summary s,OperationJob job)
    {
        var execution=job.JobType=="containers.files.recycle-restore";
        if(s.SchemaVersion!=1||s.IntentId==Guid.Empty||s.SourceIntentId==Guid.Empty||s.SourceIntentId==s.IntentId||
            s.SourceOperationId!=ContainerRecycleWorkflowValidation.ExecutionId(s.SourceIntentId)||s.JobId!=job.Id||
            s.JobId!=(execution?ContainerRecycleRestoreWorkflowValidation.ExecutionId(s.IntentId):s.IntentId)||
            execution&&s.JobId==s.SourceOperationId||s.Stage!=(execution?"execution":"planning")||
            job.State!=(s.State is "Ready" or "Succeeded"?"Succeeded":s.State=="Unknown"?"Running":s.State=="Canceled"?"Canceled":"Failed")||
            s.Code is null||s.Code.Length<=Prefix.Length||s.Code.Length>64||!s.Code.StartsWith(Prefix,StringComparison.Ordinal)||s.Code.Any(c=>!(char.IsAsciiLetterUpper(c)||c=='_'))||
            s.HistoryAvailable==true&&!s.HistoryVerified||s.SourceState is not (null or "Trashed" or "Restored" or "Aborted" or "Unknown"))return false;
        if(!execution)return (s.State is "Ready" or "Expired" or "Canceled" or "Failed") && s.ReceiptState is null&&s.SourceState is null&&s.BindingCurrent is null&&s.SourceRetained is null&&s.SourceRestored is null&&s.RestoredByThisOperation is null&&s.HistoryAvailable is null&&!s.HistoryVerified&&!s.PublishBarrier&&CodeMatches(s,false);
        if(s.State is not ("Succeeded" or "Superseded" or "Canceled" or "Failed" or "Unknown" or "Acknowledged")||s.State!="Unknown"&&s.PublishBarrier||!CodeMatches(s,true))return false;
        if(s.ReceiptState is null)return s.State=="Unknown"&&s.SourceState is null&&s.BindingCurrent is null&&s.SourceRetained is null&&s.SourceRestored is null&&s.RestoredByThisOperation is null&&s.HistoryAvailable is null&&!s.HistoryVerified;
        if(s.ReceiptState is not ("Completed" or "Aborted" or "Unknown")||s.BindingCurrent is null||s.HistoryAvailable is null||
            s.BindingCurrent==true&&!(s.ReceiptState=="Completed"&&s.HistoryAvailable==true&&s.HistoryVerified))return false;
        var facts=s.ReceiptState switch{
            "Completed"=>s.HistoryVerified&&s.SourceState=="Restored"&&s.SourceRetained==false&&s.SourceRestored==true&&s.RestoredByThisOperation==true,
            "Unknown"=>s.HistoryVerified&&s.SourceRetained is null&&s.SourceRestored is null&&s.RestoredByThisOperation is null,
            _=>s.BindingCurrent==false&&s.RestoredByThisOperation==false&&(s.HistoryVerified?s.SourceState=="Trashed"&&s.SourceRetained==true&&s.SourceRestored==false:s.SourceState is null&&s.SourceRetained is null&&s.SourceRestored is null)};
        return facts&&(s.State switch{"Succeeded"=>s.ReceiptState=="Completed"&&s.BindingCurrent==true,"Superseded"=>s.ReceiptState=="Completed"&&s.BindingCurrent==false,"Unknown"=>s.ReceiptState=="Unknown","Canceled" or "Failed"=>s.ReceiptState=="Aborted",_=>true});
    }
    private static bool CodeMatches(Summary s,bool execution)
    {
        var suffix=s.Code[Prefix.Length..];
        return s.State switch{
            "Ready"=>suffix=="READY","Expired"=>suffix=="PLAN_EXPIRED","Succeeded"=>suffix=="COMPLETED","Superseded"=>suffix=="TARGET_CHANGED","Canceled"=>suffix=="CANCELED",
            "Unknown"=>suffix is "RESULT_UNKNOWN" or "OUTPUT_INVALID" or "BINDING_CHANGED","Acknowledged"=>suffix=="UNKNOWN_ACKNOWLEDGED",
            "Failed"=>execution?suffix=="INTERRUPTED":suffix is not ("READY" or "PLAN_EXPIRED" or "COMPLETED" or "CANCELED" or "UNKNOWN_ACKNOWLEDGED"),_=>false};
    }
}
