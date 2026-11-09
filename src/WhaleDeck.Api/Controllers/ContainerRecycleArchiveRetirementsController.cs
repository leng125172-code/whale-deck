using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecycleArchiveRetirementInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recycle-archive-retirements")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(8192)]
public sealed class ContainerRecycleArchiveRetirementsController(ContainerRecycleArchiveRetirementWorkflowService workflow, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";

    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecycleArchiveRetirementIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleArchiveRetirementIntentDto>> Queue(string containerId, QueueArchiveRetirementBody body, CancellationToken token)
    {
        if (!TryKey(out var key)) return BadKey();
        var actor = (await identity.ReadAsync(HttpContext)).Actor;
        var result = await workflow.QueueAsync(actor, containerId, body.ArchiveIntentId, body.RetainDays, key, token);
        return AcceptedAtAction(nameof(Status), new { containerId, id = result.Id }, result);
    }

    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerRecycleArchiveRetirementIntentDto>> Status(string containerId, Guid id, CancellationToken token) =>
        Ok(await workflow.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, token));

    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerRecycleArchiveRetirementIntentDto>> ByKey(string containerId, string key, CancellationToken token) =>
        Ok(await workflow.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, token));

    [HttpPost(Intent + "/confirm"), Consumes("application/json"), ProducesResponseType<ContainerRecycleArchiveRetirementIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecycleArchiveRetirementIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecycleArchiveBody body, CancellationToken token)
    {
        if (!Matches(id)) return BadKey();
        var actor = (await identity.ReadAsync(HttpContext)).Actor;
        var result = await workflow.ConfirmAsync(actor, containerId, id, body.PlanHash, body.Version, body.Confirmed, token);
        return AcceptedAtAction(nameof(Status), new { containerId, id }, result);
    }

    [HttpPost(Intent + "/cancel"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileCancelDto>> Cancel(string containerId, Guid id, CancellationToken token)
    {
        if (!Matches(id)) return BadKey();
        await workflow.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, token);
        return AcceptedAtAction(nameof(Status), new { containerId, id }, new FileCancelDto(id, "CancellationRequested"));
    }

    [HttpGet(Intent + "/recovery")]
    public async Task<ActionResult<ContainerRecycleArchiveRetirementIntentDto>> Recovery(string containerId, Guid id, CancellationToken token) =>
        Ok(await workflow.GetForRecoveryAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, token));

    [HttpPost(Intent + "/recovery/acknowledge"), Consumes("application/json"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FileCancelDto>> Acknowledge(string containerId, Guid id, AcknowledgeContainerRecycleArchiveBody body, CancellationToken token)
    {
        if (!Matches(id)) return BadKey();
        await workflow.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, body.Version, body.Confirmed, token);
        return AcceptedAtAction(nameof(Recovery), new { containerId, id }, new FileCancelDto(id, "Acknowledged"));
    }

    private bool TryKey(out string key)
    {
        var values = Request.Headers["Idempotency-Key"];
        key = values.ToString();
        return values.Count == 1 && Guid.TryParseExact(key, "D", out var id) && id != Guid.Empty && id.ToString("D") == key;
    }

    private bool Matches(Guid id) => TryKey(out var key) && key == id.ToString("D");
    private BadRequestObjectResult BadKey() => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前退役操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = HttpContext.TraceIdentifier }
    });
}

[JsonConverter(typeof(QueueArchiveRetirementBodyConverter))]
public sealed record QueueArchiveRetirementBody(Guid ArchiveIntentId, uint RetainDays);

public sealed class QueueArchiveRetirementBodyConverter : JsonConverter<QueueArchiveRetirementBody>
{
    private static readonly string[] Fields = ["archiveIntentId", "retainDays"];
    public override bool HandleNull => true;

    public override QueueArchiveRetirementBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        ContainerAclInput.Fields(root, Fields);
        var text = ContainerAclInput.Text(root, "archiveIntentId");
        var days = root.GetProperty("retainDays");
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text ||
            days.ValueKind != JsonValueKind.Number || !days.TryGetUInt32(out var retainDays) || retainDays is < 1 or > 3650)
            throw new JsonException("Canonical archive intent and explicit retirement retention required.");
        return new(id, retainDays);
    }

    public override void Write(Utf8JsonWriter writer, QueueArchiveRetirementBody value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("archiveIntentId", value.ArchiveIntentId);
        writer.WriteNumber("retainDays", value.RetainDays);
        writer.WriteEndObject();
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ContainerRecycleArchiveRetirementInputAttribute : ActionFilterAttribute
{
    public ContainerRecycleArchiveRetirementInputAttribute() => Order = -3001;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid && context.HttpContext.Request.Query.Count == 0) return;
        context.Result = new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "归档退役请求字段或查询参数无效。",
            Extensions = { ["code"] = "CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INPUT_INVALID", ["traceId"] = context.HttpContext.TraceIdentifier }
        });
    }
}
