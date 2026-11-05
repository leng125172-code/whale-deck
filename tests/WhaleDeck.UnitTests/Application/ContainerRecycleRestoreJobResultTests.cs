using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecycleRestoreJobResultTests
{
    private static readonly Guid Id=Guid.Parse("291a9ea0-e736-4f2c-a090-1cce1138fbb3");
    private static readonly Guid SourceId=Guid.Parse("491a9ea0-e736-4f2c-a090-1cce1138fbb4");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static ContainerRecycleRestoreJobResults.Summary Facts()=>new(1,Id,ContainerRecycleRestoreWorkflowValidation.ExecutionId(Id),SourceId,ContainerRecycleWorkflowValidation.ExecutionId(SourceId),"execution","Succeeded","CONTAINER_RECYCLE_RESTORE_COMPLETED","Completed","Restored",true,false,true,true,true,true,false);
    private static OperationJob Job(ContainerRecycleRestoreJobResults.Summary s)=>new(){Id=s.JobId,ActorSubject="private-actor",IdempotencyKey="private-key",JobType=s.Stage=="planning"?"containers.files.recycle-restore-plan":"containers.files.recycle-restore",State=s.State is "Ready" or "Succeeded"?"Succeeded":s.State=="Unknown"?"Running":s.State=="Canceled"?"Canceled":"Failed",ResultJson=JsonSerializer.Serialize(s,Json)};
    [Theory][InlineData("Succeeded")][InlineData("Superseded")][InlineData("Canceled")][InlineData("Failed")][InlineData("Unknown")][InlineData("Acknowledged")]
    public void OwnHistoricalOutcomeIsDistinctFromSourceState(string state)
    {
        var s=Facts() with{State=state};s=state switch{
            "Superseded"=>s with{BindingCurrent=false,Code="CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED"},
            "Canceled" or "Failed"=>s with{ReceiptState="Aborted",SourceState=null,BindingCurrent=false,SourceRetained=null,SourceRestored=null,RestoredByThisOperation=false,HistoryVerified=false,HistoryAvailable=false,Code=state=="Canceled"?"CONTAINER_RECYCLE_RESTORE_CANCELED":"CONTAINER_RECYCLE_RESTORE_INTERRUPTED"},
            "Unknown" or "Acknowledged"=>s with{ReceiptState="Unknown",SourceState="Restored",BindingCurrent=false,SourceRetained=null,SourceRestored=null,RestoredByThisOperation=null,PublishBarrier=state=="Unknown",Code=state=="Unknown"?"CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN":"CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED"},_=>s};
        Assert.NotNull(ContainerRecycleRestoreJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s with{SourceOperationId=s.JobId})));
        if(state is "Unknown" or "Acknowledged")Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s with{RestoredByThisOperation=false})));
    }
    [Theory][InlineData("Ready","READY")][InlineData("Expired","PLAN_EXPIRED")][InlineData("Canceled","CANCELED")][InlineData("Failed","SOURCE_CHANGED")]
    public void PlanningCannotClaimSourceOrExecutionFacts(string state,string code)
    {
        var s=Facts() with{JobId=Id,Stage="planning",State=state,Code="CONTAINER_RECYCLE_RESTORE_"+code,ReceiptState=null,SourceState=null,BindingCurrent=null,SourceRetained=null,SourceRestored=null,RestoredByThisOperation=null,HistoryVerified=false,HistoryAvailable=null};
        Assert.NotNull(ContainerRecycleRestoreJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s with{RestoredByThisOperation=false})));
    }
    [Fact]
    public void MissingReceiptCannotBeFalseOrCompleted()
    {
        var s=Facts() with{State="Unknown",Code="CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN",ReceiptState=null,SourceState=null,BindingCurrent=null,SourceRetained=null,SourceRestored=null,RestoredByThisOperation=null,HistoryVerified=false,HistoryAvailable=null,PublishBarrier=true};
        Assert.NotNull(ContainerRecycleRestoreJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s with{SourceRestored=false})));
    }
    [Fact]
    public void AcknowledgedMissingReplyMayAcquireLateProofWithoutClaimingJobSucceeded()
    {
        var s=Facts() with{State="Acknowledged",Code="CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED"};
        var job=Job(s);Assert.Equal("Failed",job.State);Assert.NotNull(ContainerRecycleRestoreJobResults.ForJob(job));
        Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s with{State="Succeeded"})));
    }
    [Theory][InlineData("job-id")][InlineData("stage")][InlineData("source")][InlineData("version")][InlineData("outcome")][InlineData("code")][InlineData("barrier")]
    public void MismatchedProjectionIsNeverSuccessful(string field)
    {
        var s=Facts();s=field switch{"job-id"=>s with{JobId=Guid.NewGuid()},"stage"=>s with{Stage="planning"},"source"=>s with{SourceIntentId=Guid.NewGuid()},"version"=>s with{SchemaVersion=2},"outcome"=>s with{RestoredByThisOperation=false},"code"=>s with{Code="CONTAINER_RECYCLE_TRASHED"},_=>s with{PublishBarrier=true}};
        Assert.Null(ContainerRecycleRestoreJobResults.ForJob(Job(s)));
    }
    [Theory][InlineData("extra")][InlineData("duplicate")][InlineData("missing")][InlineData("type")][InlineData("case")]
    public async Task MalformedStoredJsonCannotFallBackToGenericRawOutput(string kind)
    {
        var job=Job(Facts());var node=JsonNode.Parse(job.ResultJson!)!.AsObject();
        if(kind=="extra")node["privatePath"]="/must-not-render";if(kind=="missing")node.Remove("sourceRestored");if(kind=="type")node["restoredByThisOperation"]="true";if(kind=="case"){node["State"]=node["state"]!.DeepClone();node.Remove("state");}
        job.ResultJson=kind=="duplicate"?job.ResultJson![..^1]+",\"restoredByThisOperation\":true}":node.ToJsonString();
        Assert.Null(ContainerRecycleRestoreJobResults.ForJob(job));Assert.Null((await new OperationService(new Jobs(job)).FindAsync(job.Id,default))!.ResultJson);
    }
    [Fact]
    public void SnapshotHasOnlyFixedHistoricalFieldsAndOpaqueReferences()
    {
        var p=ContainerRecycleWorkflowFixture.Plan();var command=new WhaleDeck.Application.Models.ContainerRecycleRestoreCommandDto(new(ContainerRecycleWorkflowValidation.ExecutionId(SourceId),new('D',64),p),new('F',64));
        var row=new ContainerRecycleRestoreIntent{Id=Id,SourceIntentId=SourceId,SourceProofHash=new('A',64),ActorSubject="private-actor",LoginBinding="private-login",TraceId="private-trace",OwnerHash="private-owner",ContainerId=p.Command.Location.ContainerId,InstanceId=p.Command.Location.InstanceId,RelativePath=p.Command.Location.Path,CommandJson=ContainerRecycleRestoreWorkflowValidation.Serialize(command),RequestHash="private-request",State="Ready",ResultCode="CONTAINER_RECYCLE_RESTORE_READY"};
        var text=ContainerRecycleRestoreJobResults.Snapshot(row,false);Assert.Equal(17,JsonDocument.Parse(text).RootElement.EnumerateObject().Count());
        foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.SourceProofHash,row.RequestHash,p.Sha256,command.HistoryDigest})Assert.DoesNotContain(secret,text,StringComparison.Ordinal);
        Assert.NotNull(ContainerRecycleRestoreJobResults.ForJob(new(){Id=Id,ActorSubject="admin",IdempotencyKey="key",JobType="containers.files.recycle-restore-plan",State="Succeeded",ResultJson=text}));
    }
    private sealed class Jobs(OperationJob job):IJobRepository
    {
        public Task<OperationJob?> FindAsync(Guid id,CancellationToken token)=>Task.FromResult<OperationJob?>(job);
        public Task<OperationJob> EnqueueAsync(OperationJob j,OutboxMessage o,AuditEvent a,CancellationToken token)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<OperationJob>> ListAsync(int take,CancellationToken token)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id,long after,CancellationToken token)=>throw new NotSupportedException();
        public Task RequestCancellationAsync(Guid id,string actor,CancellationToken token)=>throw new NotSupportedException();
    }
}
