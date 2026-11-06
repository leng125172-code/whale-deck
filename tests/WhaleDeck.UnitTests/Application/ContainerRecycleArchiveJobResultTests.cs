using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecycleArchiveJobResultTests
{
    private static readonly Guid Id=Guid.Parse("291a9ea0-e736-4f2c-a090-1cce1138fbb3");
    private static readonly Guid SourceId=Guid.Parse("491a9ea0-e736-4f2c-a090-1cce1138fbb4");
    private static readonly Guid OriginalId=Guid.Parse("591a9ea0-e736-4f2c-a090-1cce1138fbb5");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static ContainerRecycleArchiveJobResults.Summary Facts()=>new(1,Id,ContainerRecycleArchiveWorkflowValidation.ExecutionId(Id),SourceId,ContainerRecyclePurgeWorkflowValidation.ExecutionId(SourceId),OriginalId,ContainerRecycleWorkflowValidation.ExecutionId(OriginalId),"execution","Succeeded","CONTAINER_RECYCLE_ARCHIVE_COMPLETED","Completed",true,true,true,true,true,true,true,false);
    private static OperationJob Job(ContainerRecycleArchiveJobResults.Summary s)=>new(){Id=s.JobId,ActorSubject="private-actor",IdempotencyKey="private-key",JobType=s.Stage=="planning"?"containers.files.recycle-archive-plan":"containers.files.recycle-archive",State=s.State is "Ready" or "Succeeded"?"Succeeded":s.State is "Unknown" or "Reserving" or "Applying"?"Running":s.State=="Canceled"?"Canceled":"Failed",ResultJson=JsonSerializer.Serialize(s,Json)};
    [Theory][InlineData("Succeeded")][InlineData("Canceled")][InlineData("Failed")][InlineData("Unknown")][InlineData("Acknowledged")]
    public void OwnHistoricalOutcomeRemainsDistinctFromPreparation(string state)
    {
        var s=Facts() with{State=state};s=state switch{
            "Canceled" or "Failed"=>s with{ReceiptState="Aborted",ArchiveCopied=false,SourceFenced=false,OwnedFence=false,BindingCurrent=null,Code=state=="Canceled"?"CONTAINER_RECYCLE_ARCHIVE_CANCELED":"CONTAINER_RECYCLE_ARCHIVE_INTERRUPTED"},
            "Unknown" or "Acknowledged"=>s with{ReceiptState="Unknown",ArchiveCopied=null,BindingCurrent=null,PublishBarrier=state=="Unknown",Code=state=="Unknown"?"CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN":"CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED"},_=>s};
        Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{SourceOperationId=s.JobId})));
        if(state is "Unknown" or "Acknowledged")Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{ArchiveCopied=false})));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ReservationIsNotProofOfCopying(bool recorded)
    {
        var s=Facts() with{State="Reserving",Code="CONTAINER_RECYCLE_ARCHIVE_"+(recorded?"RESERVED":"RESERVING"),ReceiptState=null,ReservationRecorded=recorded,ApplyStarted=false,ArchiveCopied=null,SourceFenced=null,OwnedFence=null,BindingCurrent=null,PublishBarrier=true};
        Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{ArchiveCopied=false})));
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{ApplyStarted=true})));
    }
    [Fact] public void ApplyMarkerDoesNotProveOutcome()
    {var s=Facts() with{State="Applying",Code="CONTAINER_RECYCLE_ARCHIVE_APPLYING",ReceiptState=null,ArchiveCopied=null,SourceFenced=null,OwnedFence=null,BindingCurrent=null,PublishBarrier=true};Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{ReservationRecorded=false})));}
    [Theory][InlineData("Ready","READY")][InlineData("Expired","PLAN_EXPIRED")][InlineData("Canceled","CANCELED")][InlineData("Failed","SOURCE_CHANGED")]
    public void PlanningCannotClaimSourceOrExecutionFacts(string state,string code)
    {
        var s=Facts() with{JobId=Id,Stage="planning",State=state,Code="CONTAINER_RECYCLE_ARCHIVE_"+code,ReceiptState=null,ReservationStarted=false,ReservationRecorded=false,ApplyStarted=false,ArchiveCopied=null,SourceFenced=null,OwnedFence=null,BindingCurrent=null};
        Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{ArchiveCopied=false})));
    }
    [Fact]
    public void MissingReceiptCannotBeFalseOrCompleted()
    {
        var s=Facts() with{State="Unknown",Code="CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN",ReceiptState=null,ReservationStarted=false,ReservationRecorded=false,ApplyStarted=false,ArchiveCopied=null,SourceFenced=null,OwnedFence=null,BindingCurrent=null,PublishBarrier=true};
        Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{SourceFenced=false})));
    }
    [Fact]
    public void AcknowledgedMissingReplyMayAcquireLateProofWithoutClaimingJobSucceeded()
    {
        var s=Facts() with{State="Acknowledged",Code="CONTAINER_RECYCLE_ARCHIVE_UNKNOWN_ACKNOWLEDGED"};
        var job=Job(s);Assert.Equal("Failed",job.State);Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(job));
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{State="Succeeded"})));
    }
    [Theory][InlineData("job-id")][InlineData("stage")][InlineData("source")][InlineData("version")][InlineData("outcome")][InlineData("code")][InlineData("barrier")]
    public void MismatchedProjectionIsNeverSuccessful(string field)
    {
        var s=Facts();s=field switch{"job-id"=>s with{JobId=Guid.NewGuid()},"stage"=>s with{Stage="planning"},"source"=>s with{SourceIntentId=Guid.NewGuid()},"version"=>s with{SchemaVersion=2},"outcome"=>s with{ArchiveCopied=false},"code"=>s with{Code="CONTAINER_RECYCLE_TRASHED"},_=>s with{PublishBarrier=true}};
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s)));
    }
    [Theory][InlineData("extra")][InlineData("duplicate")][InlineData("missing")][InlineData("type")][InlineData("case")]
    public async Task MalformedStoredJsonCannotFallBackToGenericRawOutput(string kind)
    {
        var job=Job(Facts());var node=JsonNode.Parse(job.ResultJson!)!.AsObject();
        if(kind=="extra")node["privatePath"]="/must-not-render";if(kind=="missing")node.Remove("sourceFenced");if(kind=="type")node["archiveCopied"]="true";if(kind=="case"){node["State"]=node["state"]!.DeepClone();node.Remove("state");}
        job.ResultJson=kind=="duplicate"?job.ResultJson![..^1]+",\"archiveCopied\":true}":node.ToJsonString();
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(job));Assert.Null((await new OperationService(new Jobs(job)).FindAsync(job.Id,default))!.ResultJson);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void CompletedCopyBindingIsIndependentFromHistoricalSuccess(bool current)
    {
        var s=Facts() with{BindingCurrent=current,OwnedFence=current};Assert.NotNull(ContainerRecycleArchiveJobResults.ForJob(Job(s)));
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{PurgeOperationId=s.SourceOperationId})));
        Assert.Null(ContainerRecycleArchiveJobResults.ForJob(Job(s with{OriginalIntentId=s.SourceIntentId})));
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
