using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecyclePurgeJobResultTests
{
    private static readonly Guid Id=Guid.Parse("291a9ea0-e736-4f2c-a090-1cce1138fbb3");
    private static readonly Guid SourceId=Guid.Parse("491a9ea0-e736-4f2c-a090-1cce1138fbb4");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static ContainerRecyclePurgeJobResults.Summary Facts()=>new(1,Id,ContainerRecyclePurgeWorkflowValidation.ExecutionId(Id),SourceId,ContainerRecycleWorkflowValidation.ExecutionId(SourceId),"execution","Succeeded","CONTAINER_RECYCLE_PURGE_COMPLETED","Completed",true,true,true,true,true,true,false);
    private static OperationJob Job(ContainerRecyclePurgeJobResults.Summary s)=>new(){Id=s.JobId,ActorSubject="private-actor",IdempotencyKey="private-key",JobType=s.Stage=="planning"?"containers.files.recycle-purge-plan":"containers.files.recycle-purge",State=s.State is "Ready" or "Succeeded"?"Succeeded":s.State is "Unknown" or "Reserving" or "Applying"?"Running":s.State=="Canceled"?"Canceled":"Failed",ResultJson=JsonSerializer.Serialize(s,Json)};
    [Theory][InlineData("Succeeded")][InlineData("Canceled")][InlineData("Failed")][InlineData("Unknown")][InlineData("Acknowledged")]
    public void OwnHistoricalOutcomeRemainsDistinctFromPreparation(string state)
    {
        var s=Facts() with{State=state};s=state switch{
            "Canceled" or "Failed"=>s with{ReceiptState="Aborted",PurgeApplied=false,SourceFenced=false,OwnedFence=false,Code=state=="Canceled"?"CONTAINER_RECYCLE_PURGE_CANCELED":"CONTAINER_RECYCLE_PURGE_INTERRUPTED"},
            "Unknown" or "Acknowledged"=>s with{ReceiptState="Unknown",PurgeApplied=null,PublishBarrier=state=="Unknown",Code=state=="Unknown"?"CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN":"CONTAINER_RECYCLE_PURGE_UNKNOWN_ACKNOWLEDGED"},_=>s};
        Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(Job(s)));Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{SourceOperationId=s.JobId})));
        if(state is "Unknown" or "Acknowledged")Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{PurgeApplied=false})));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void ReservationIsNotProofOfDeletion(bool recorded)
    {
        var s=Facts() with{State="Reserving",Code="CONTAINER_RECYCLE_PURGE_"+(recorded?"RESERVED":"RESERVING"),ReceiptState=null,ReservationRecorded=recorded,ApplyStarted=false,PurgeApplied=null,SourceFenced=null,OwnedFence=null,PublishBarrier=true};
        Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(Job(s)));Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{PurgeApplied=false})));
        Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{ApplyStarted=true})));
    }
    [Fact] public void ApplyMarkerDoesNotProveOutcome()
    {var s=Facts() with{State="Applying",Code="CONTAINER_RECYCLE_PURGE_APPLYING",ReceiptState=null,PurgeApplied=null,SourceFenced=null,OwnedFence=null,PublishBarrier=true};Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(Job(s)));Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{ReservationRecorded=false})));}
    [Theory][InlineData("Ready","READY")][InlineData("Expired","PLAN_EXPIRED")][InlineData("Canceled","CANCELED")][InlineData("Failed","SOURCE_CHANGED")]
    public void PlanningCannotClaimSourceOrExecutionFacts(string state,string code)
    {
        var s=Facts() with{JobId=Id,Stage="planning",State=state,Code="CONTAINER_RECYCLE_PURGE_"+code,ReceiptState=null,ReservationStarted=false,ReservationRecorded=false,ApplyStarted=false,PurgeApplied=null,SourceFenced=null,OwnedFence=null};
        Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(Job(s)));Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{PurgeApplied=false})));
    }
    [Fact]
    public void MissingReceiptCannotBeFalseOrCompleted()
    {
        var s=Facts() with{State="Unknown",Code="CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN",ReceiptState=null,ReservationStarted=false,ReservationRecorded=false,ApplyStarted=false,PurgeApplied=null,SourceFenced=null,OwnedFence=null,PublishBarrier=true};
        Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(Job(s)));Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{SourceFenced=false})));
    }
    [Fact]
    public void AcknowledgedMissingReplyMayAcquireLateProofWithoutClaimingJobSucceeded()
    {
        var s=Facts() with{State="Acknowledged",Code="CONTAINER_RECYCLE_PURGE_UNKNOWN_ACKNOWLEDGED"};
        var job=Job(s);Assert.Equal("Failed",job.State);Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(job));
        Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s with{State="Succeeded"})));
    }
    [Theory][InlineData("job-id")][InlineData("stage")][InlineData("source")][InlineData("version")][InlineData("outcome")][InlineData("code")][InlineData("barrier")]
    public void MismatchedProjectionIsNeverSuccessful(string field)
    {
        var s=Facts();s=field switch{"job-id"=>s with{JobId=Guid.NewGuid()},"stage"=>s with{Stage="planning"},"source"=>s with{SourceIntentId=Guid.NewGuid()},"version"=>s with{SchemaVersion=2},"outcome"=>s with{PurgeApplied=false},"code"=>s with{Code="CONTAINER_RECYCLE_TRASHED"},_=>s with{PublishBarrier=true}};
        Assert.Null(ContainerRecyclePurgeJobResults.ForJob(Job(s)));
    }
    [Theory][InlineData("extra")][InlineData("duplicate")][InlineData("missing")][InlineData("type")][InlineData("case")]
    public async Task MalformedStoredJsonCannotFallBackToGenericRawOutput(string kind)
    {
        var job=Job(Facts());var node=JsonNode.Parse(job.ResultJson!)!.AsObject();
        if(kind=="extra")node["privatePath"]="/must-not-render";if(kind=="missing")node.Remove("sourceFenced");if(kind=="type")node["purgeApplied"]="true";if(kind=="case"){node["State"]=node["state"]!.DeepClone();node.Remove("state");}
        job.ResultJson=kind=="duplicate"?job.ResultJson![..^1]+",\"purgeApplied\":true}":node.ToJsonString();
        Assert.Null(ContainerRecyclePurgeJobResults.ForJob(job));Assert.Null((await new OperationService(new Jobs(job)).FindAsync(job.Id,default))!.ResultJson);
    }
    [Fact]
    public void SnapshotHasOnlyFixedHistoricalFieldsAndOpaqueReferences()
    {
        var p=ContainerRecycleWorkflowFixture.Plan();var command=new WhaleDeck.Application.Models.ContainerRecyclePurgeCommandDto(new(ContainerRecycleWorkflowValidation.ExecutionId(SourceId),new('D',64),p),new('F',64),"Originals");
        var row=new ContainerRecyclePurgeIntent{Id=Id,SourceIntentId=SourceId,SourceProofHash=new('A',64),ActorSubject="private-actor",LoginBinding="private-login",TraceId="private-trace",OwnerHash="private-owner",ContainerId=p.Command.Location.ContainerId,InstanceId=p.Command.Location.InstanceId,RelativePath=p.Command.Location.Path,CommandJson=ContainerRecyclePurgeWorkflowValidation.Serialize(command),RequestHash="private-request",State="Ready",ResultCode="CONTAINER_RECYCLE_PURGE_READY"};
        var text=ContainerRecyclePurgeJobResults.Snapshot(row,false);Assert.Equal(16,JsonDocument.Parse(text).RootElement.EnumerateObject().Count());
        foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.SourceProofHash,row.RequestHash,p.Sha256,command.HistoryDigest})Assert.DoesNotContain(secret,text,StringComparison.Ordinal);
        Assert.NotNull(ContainerRecyclePurgeJobResults.ForJob(new(){Id=Id,ActorSubject="admin",IdempotencyKey="key",JobType="containers.files.recycle-purge-plan",State="Succeeded",ResultJson=text}));
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
