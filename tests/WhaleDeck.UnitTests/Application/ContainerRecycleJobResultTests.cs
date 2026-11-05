using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
namespace WhaleDeck.UnitTests.Application;
public sealed class ContainerRecycleJobResultTests
{
    private static readonly Guid Id=Guid.Parse("291a9ea0-e736-4f2c-a090-1cce1138fbb3");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static ContainerRecycleJobResults.Summary Facts()=>new(1,Id,ContainerRecycleWorkflowValidation.ExecutionId(Id),"execution","Succeeded","CONTAINER_RECYCLE_TRASHED","Trashed",true,true,false,true,true,true,false);
    private static OperationJob Job(ContainerRecycleJobResults.Summary s,Guid? id=null,string? type=null)=>new(){Id=id??s.JobId,ActorSubject="private-actor",IdempotencyKey="private-key",JobType=type??(s.Stage=="planning"?"containers.files.recycle-plan":"containers.files.recycle"),State=s.State is "Ready" or "Succeeded"?"Succeeded":s.State=="Unknown"?"Running":s.State=="Canceled"?"Canceled":"Failed",ResultJson=JsonSerializer.Serialize(s,Json)};
    [Theory][InlineData("Succeeded")][InlineData("Superseded")][InlineData("Canceled")][InlineData("Failed")][InlineData("Unknown")][InlineData("Acknowledged")]
    public void HistoricalOutcomesRemainDistinct(string state)
    {
        var s=Facts() with{State=state};
        s=state switch{"Superseded"=>s with{BindingCurrent=false,Code="CONTAINER_RECYCLE_TARGET_CHANGED"},"Canceled" or "Failed"=>s with{ReceiptState="Aborted",BindingCurrent=false,SourceRetained=false,Code=state=="Canceled"?"CONTAINER_RECYCLE_CANCELED":"CONTAINER_RECYCLE_INTERRUPTED"},"Unknown" or "Acknowledged"=>s with{ReceiptState="Unknown",BindingCurrent=false,SourceRetained=null,SourceRestored=null,PublishBarrier=state=="Unknown",Code=state=="Unknown"?"CONTAINER_RECYCLE_RESULT_UNKNOWN":"CONTAINER_RECYCLE_UNKNOWN_ACKNOWLEDGED"},_=>s};
        Assert.NotNull(ContainerRecycleJobResults.ForJob(Job(s)));
        Assert.Null(ContainerRecycleJobResults.ForJob(Job(s with{SourceRestored=true})));
    }
    [Fact] public void RestoredHistoryCannotBeFreshTrashSuccess()
    {var s=Facts() with{State="Superseded",ReceiptState="Restored",SourceRetained=false,SourceRestored=true,Code="CONTAINER_RECYCLE_ALREADY_RESTORED"};Assert.NotNull(ContainerRecycleJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleJobResults.ForJob(Job(s with{State="Succeeded",Code="CONTAINER_RECYCLE_TRASHED"})));}
    [Fact] public void MissingReplyIsNotFalseAndCannotBeSuccess()
    {var s=Facts() with{State="Unknown",Code="CONTAINER_RECYCLE_RESULT_UNKNOWN",ReceiptState=null,BindingCurrent=null,SourceRetained=null,SourceRestored=null,HistoryVerified=false,HistoryAvailable=null,PublishBarrier=true};Assert.NotNull(ContainerRecycleJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleJobResults.ForJob(Job(s with{SourceRetained=false})));}
    [Theory][InlineData("Ready","READY")][InlineData("Expired","PLAN_EXPIRED")][InlineData("Canceled","CANCELED")][InlineData("Failed","SOURCE_CHANGED")]
    public void PlanningDoesNotClaimExecution(string state,string code)
    {var s=Facts() with{JobId=Id,Stage="planning",State=state,Code="CONTAINER_RECYCLE_"+code,ReceiptState=null,BindingCurrent=null,SourceRetained=null,SourceRestored=null,HistoryMayExist=false,HistoryVerified=false,HistoryAvailable=null};Assert.NotNull(ContainerRecycleJobResults.ForJob(Job(s)));Assert.Null(ContainerRecycleJobResults.ForJob(Job(s with{SourceRetained=false})));}
    [Theory][InlineData("extra")][InlineData("missing")][InlineData("duplicate")][InlineData("wrong-job")][InlineData("wrong-type")][InlineData("unsafe-history")][InlineData("barrier")][InlineData("oversize")]
    public void MalformedOrContradictoryJsonIsNotShown(string change)
    {
        var s=Facts();var job=Job(s);var node=JsonNode.Parse(job.ResultJson!)!.AsObject();
        switch(change){case "extra":node["privatePath"]="secret";break;case "missing":node.Remove("sourceRestored");break;case "wrong-job":job=Job(s,Guid.NewGuid());break;case "wrong-type":job=Job(s,type:"containers.files.acl");break;case "unsafe-history":node["historyMayExist"]=false;break;case "barrier":node["publishBarrier"]=true;break;}
        job.ResultJson=change=="duplicate"?job.ResultJson![..^1]+",\"state\":\"Succeeded\"}":change=="oversize"?new('x',4097):node.ToJsonString();Assert.Null(ContainerRecycleJobResults.ForJob(job));
    }
    [Fact] public void SnapshotHasOnlyTheFourteenSafeFields()
    {
        var p=ContainerRecycleWorkflowFixture.Plan();var row=new ContainerRecycleIntent{Id=Id,ActorSubject="secret-actor",LoginBinding=new('A',64),TraceId="secret-trace",OwnerHash=new('B',64),ContainerId=p.Command.Location.ContainerId,InstanceId=p.Command.Location.InstanceId,RelativePath=p.Command.Location.Path,CommandJson=ContainerRecycleWorkflowValidation.Serialize(p.Command),RequestHash=new('C',64),PlanJson=ContainerRecycleWorkflowValidation.Serialize(p),State="Ready",ResultCode="CONTAINER_RECYCLE_READY"};
        var value=ContainerRecycleJobResults.Snapshot(row,false);Assert.Equal(14,JsonDocument.Parse(value).RootElement.EnumerateObject().Count());foreach(var secret in new[]{row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.RequestHash,p.Sha256})Assert.DoesNotContain(secret,value,StringComparison.Ordinal);Assert.NotNull(ContainerRecycleJobResults.ForJob(new(){Id=Id,ActorSubject="admin",IdempotencyKey="key",JobType="containers.files.recycle-plan",State="Succeeded",ResultJson=value}));
    }
}
