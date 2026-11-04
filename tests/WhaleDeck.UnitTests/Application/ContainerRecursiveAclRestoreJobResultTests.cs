using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecursiveAclRestoreJobResultTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Intent = Guid.Parse("291a9ea0-e736-4f2c-a090-1cce1138fbb3");
    private static ContainerRecursiveAclRestoreJobResults.Summary Summary() => new(1, Intent,
        ContainerRecursiveAclRestoreWorkflowValidation.ExecutionId(Intent), "execution", "Succeeded", "CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED",
        "access", 4, 2, 2, 4, 4, 4, 0, 0, 0, "Completed", true, true, true, true, false, 4, 2, 2, 2);
    private static string Code(string state) => "CONTAINER_RECURSIVE_ACL_RESTORE_" + (state switch { "Ready"=>"READY", "Expired"=>"PLAN_EXPIRED", "Succeeded"=>"COMPLETED", "Superseded"=>"TARGET_CHANGED", "Canceled"=>"CANCELED", "Failed"=>"INTERRUPTED", "Partial"=>"PARTIAL_REQUIRES_ACKNOWLEDGEMENT", "Unknown"=>"RESULT_UNKNOWN", _=>"UNKNOWN_ACKNOWLEDGED" });
    private static OperationJob Job(ContainerRecursiveAclRestoreJobResults.Summary summary, string? state = null, string? jobType = null, Guid? id = null) => new()
    { Id = id ?? summary.JobId, ActorSubject = "admin", IdempotencyKey = summary.JobId.ToString(), JobType = jobType ?? (summary.Stage == "planning" ? "containers.files.recursive-acl-restore-plan" : "containers.files.recursive-acl-restore"), State = state ?? (summary.State is "Ready" or "Succeeded" ? "Succeeded" : summary.State is "Partial" or "Unknown" ? "Running" : summary.State == "Canceled" ? "Canceled" : "Failed"), ResultJson = JsonSerializer.Serialize(summary, Json) };

    [Fact]
    public void SnapshotExposesBoundedFactsWithoutCommandLocationOrAuthority()
    {
        var plan = ContainerRecursiveAclRestoreWorkflowFixture.Plan();
        var row = new ContainerRecursiveAclRestoreIntent { Id = Intent, ActorSubject = "private-admin-subject", LoginBinding = new('A',64), TraceId = "private-trace", OwnerHash = new('B',64),
            ContainerId = plan.Command.Location.ContainerId, InstanceId = plan.Command.Location.InstanceId, RelativePath = plan.Command.Location.Path,
            CommandJson = ContainerRecursiveAclRestoreWorkflowValidation.Serialize(plan.Command), RequestHash = new('C',64), PlanHash = new('D',64),
            PlanJson = ContainerRecursiveAclRestoreWorkflowValidation.Serialize(plan), State = "Ready", ResultCode = "CONTAINER_RECURSIVE_ACL_RESTORE_READY" };
        var json = ContainerRecursiveAclRestoreJobResults.Snapshot(row, false);
        using var parsed = JsonDocument.Parse(json); Assert.Equal(26, parsed.RootElement.EnumerateObject().Count());
        foreach (var secret in new[] { row.ActorSubject,row.LoginBinding,row.TraceId,row.OwnerHash,row.ContainerId,row.InstanceId,row.RelativePath,row.RequestHash,row.PlanHash,plan.Command.CaptureId,plan.Command.ExpectedHistoryDigest }) Assert.DoesNotContain(secret,json,StringComparison.Ordinal);
        var summary = JsonSerializer.Deserialize<ContainerRecursiveAclRestoreJobResults.Summary>(json,Json)!;
        Assert.Equal(2U,summary.SourceChangedCount);Assert.Equal(4U,summary.ChangeCount);Assert.Null(summary.CompletedCount);
        Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(Job(summary)));
    }

    [Fact]
    public void ContradictoryHistoryReceiptOrProtectionIsNotAResult()
    {
        var original = Summary();
        foreach(var invalid in new[] { original with {HistoryMayExist=false},original with {HistoryVerified=false},original with {HistoryAvailable=false},original with {PublishBarrier=true},original with {ReceiptState="Unknown"},original with {BindingCurrent=false},original with {AclType=null} }) Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(invalid)));
    }

    [Theory][InlineData("Succeeded")][InlineData("Superseded")][InlineData("Canceled")][InlineData("Failed")][InlineData("Partial")][InlineData("Unknown")][InlineData("Acknowledged")]
    public void HistoricalOutcomesKeepActualCountsAndSourceCountsSeparate(string state)
    {
        var s = Summary() with { State = state, Code = Code(state) };
        if (state == "Superseded") s = s with { BindingCurrent = false };
        if (state is "Canceled" or "Failed") s = s with { ReceiptState = "Aborted", CompletedCount = 1, ChangedCount = 0, AbortedCount = 1, NotStartedCount = 2, BindingCurrent = false };
        if (state == "Partial") s = s with { ReceiptState = "Partial", CompletedCount = 1, ChangedCount = 1, NotStartedCount = 3, BindingCurrent = false, PublishBarrier = true };
        if (state is "Unknown" or "Acknowledged") s = s with { ReceiptState = "Unknown", CompletedCount = 0, ChangedCount = 0, UnknownCount = 4, BindingCurrent = false, PublishBarrier = state == "Unknown" };
        var job = Job(s); Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        var parsed = JsonSerializer.Deserialize<ContainerRecursiveAclRestoreJobResults.Summary>(ContainerRecursiveAclRestoreJobResults.ForJob(job)!, Json)!;
        Assert.Equal(s, parsed); Assert.Equal(2U, parsed.SourceChangedCount); Assert.Equal(4U, parsed.ChangeCount);
    }

    [Fact]
    public void MissingRootReceiptRemainsUnprovedRatherThanZeroOrSuccess()
    {
        var s = Summary() with { State = "Unknown", Code = Code("Unknown"), ReceiptState = null, CompletedCount = null, ChangedCount = null, AbortedCount = null, NotStartedCount = null, UnknownCount = null, BindingCurrent = null, HistoryAvailable = null, HistoryVerified = false, HistoryMayExist = true, PublishBarrier = true };
        Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s)));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s with { State = "Succeeded", PublishBarrier = false })));
    }

    [Theory][InlineData("Completed")][InlineData("Partial")][InlineData("Aborted")]
    public void AcknowledgementCanObtainALateOriginalReceiptWithoutTurningTheJobIntoSuccess(string receipt)
    {
        var s=Summary() with { State="Acknowledged",Code="CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_ACKNOWLEDGED",ReceiptState=receipt };
        if(receipt=="Partial")s=s with{CompletedCount=1,ChangedCount=1,NotStartedCount=3,BindingCurrent=false};
        if(receipt=="Aborted")s=s with{CompletedCount=0,ChangedCount=0,NotStartedCount=4,BindingCurrent=false,HistoryVerified=false,HistoryAvailable=false};
        var job=Job(s);Assert.Equal("Failed",job.State);Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s,"Succeeded")));
    }

    [Fact]
    public void PlanningTargetRefusalIsNotMistakenForCompletedSupersededExecution()
    {
        var s = Summary() with { JobId = Intent, Stage = "planning", State = "Failed", Code = "CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_CHANGED", AclType = null,
            EntryCount=null,FileCount=null,DirectoryCount=null,ChangeCount=null,SourceEntryCount=null,SourceFileCount=null,SourceDirectoryCount=null,SourceChangedCount=null,
            ReceiptState=null,CompletedCount=null,ChangedCount=null,AbortedCount=null,NotStartedCount=null,UnknownCount=null,BindingCurrent=null,HistoryAvailable=null,HistoryVerified=false,HistoryMayExist=false };
        Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s)));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(Summary() with { State="Failed",Code=s.Code,ReceiptState="Aborted",CompletedCount=0,ChangedCount=0,NotStartedCount=4,BindingCurrent=false })));
    }

    [Theory][InlineData("Ready")][InlineData("Canceled")][InlineData("Expired")][InlineData("Failed")]
    public void PlanningFactsAreNotExecutionResults(string state)
    {
        var s = Summary() with { JobId = Intent, Stage = "planning", State = state, Code = Code(state), ReceiptState = null, CompletedCount = null, ChangedCount = null, AbortedCount = null, NotStartedCount = null, UnknownCount = null, BindingCurrent = null, HistoryAvailable = null, HistoryVerified = false, HistoryMayExist = false };
        Assert.NotNull(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s)));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(s with { CompletedCount = 0 })));
    }

    [Theory][InlineData("completedCount", -1)][InlineData("unknownCount", 1)][InlineData("changedCount", 5)][InlineData("entryCount", 10001)][InlineData("fileCount", 4)][InlineData("sourceEntryCount", 3)][InlineData("schemaVersion", 2)]
    public void InvalidNumbersCannotBePublished(string key, int value)
    {
        var job = Job(Summary()); var node = JsonNode.Parse(job.ResultJson!)!; node[key] = value; job.ResultJson = node.ToJsonString();
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
    }

    [Fact]
    public void ForeignJobTypesJobIdsAndStatesAreRejected()
    {
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(Summary(), jobType: "containers.files.acl")));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(Summary(), id: Guid.NewGuid())));
        Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(Job(Summary(), "Failed")));
    }

    [Fact]
    public void PrivateUnknownDuplicateMissingAndOversizedFieldsAreRejected()
    {
        var job = Job(Summary()); var node = JsonNode.Parse(job.ResultJson!)!; node["path"] = "/private-never-expose"; job.ResultJson = node.ToJsonString(); Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        job = Job(Summary()); job.ResultJson = job.ResultJson!.Insert(1, "\"schemaVersion\":1,"); Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        job = Job(Summary()); node = JsonNode.Parse(job.ResultJson!)!; node.AsObject().Remove("unknownCount"); job.ResultJson = node.ToJsonString(); Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        job.ResultJson = new(' ', 4097); Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
        job.ResultJson = "[]"; Assert.Null(ContainerRecursiveAclRestoreJobResults.ForJob(job));
    }
}
