using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using Retirement = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementWorkflowValidation;
using Results = WhaleDeck.Application.Services.ContainerRecycleArchiveRetirementJobResults;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerRecycleArchiveRetirementJobResultsTests
{
    [Theory]
    [InlineData("containerId", "other")]
    [InlineData("code", "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_UNRECOGNIZED")]
    [InlineData("intentId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("confirmation", "private-token")]
    [InlineData("rawReceipt", "private-path")]
    public void InvalidOrPrivateFieldsCannotReachJobResult(string key, string value)
    {
        var job = UnknownJob();
        Assert.NotNull(Results.ForJob(job));
        var json = JsonNode.Parse(job.ResultJson!)!.AsObject();
        json[key] = value;
        job.ResultJson = json.ToJsonString();
        Assert.Null(Results.ForJob(job));
    }

    [Fact]
    public void UnknownReceiptAndOwnershipAreNotPromotedToSuccess()
    {
        var job = UnknownJob();
        Assert.NotNull(Results.ForJob(job));
        var json = JsonNode.Parse(job.ResultJson!)!.AsObject();
        json["retirementApplied"] = true;
        job.ResultJson = json.ToJsonString();
        Assert.Null(Results.ForJob(job));
        job = UnknownJob();
        job.ResultJson = job.ResultJson![..^1] + ",\"state\":\"Unknown\"}";
        Assert.Null(Results.ForJob(job));
    }

    private static OperationJob UnknownJob()
    {
        var id = Guid.NewGuid();
        var job = new OperationJob { Id = Retirement.ExecutionId(id), JobType = Results.ExecutionJob, State = "Running", ActorSubject = "fixture", IdempotencyKey = "fixture" };
        job.ResultJson = JsonSerializer.Serialize(new Results.Summary(1, id, job.Id, Guid.NewGuid(), new string('a', 64), 30,
            "Unknown", Retirement.Prefix + "RESULT_UNKNOWN", 1000000000, true, true, true, true, "Unknown", null), JsonSerializerOptions.Web);
        return job;
    }
}
