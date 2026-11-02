using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerAclInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/acls")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerAclsController(ContainerAclWorkflowService acls, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No ACL change occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerAclIntentDto>> Queue(string containerId, ContainerAclPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerAclHttp.BadKey(HttpContext);
        if (command is null || command.Entries is null) return ContainerAclHttp.BadAcl(HttpContext);
        var result = await acls.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.AclType, command.Entries, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerAclIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await acls.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerAclIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await acls.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no ACL mutation runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerAclIntentDto>> Confirm(string containerId, Guid id, ContainerAclConfirmBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerAclHttp.BadKey(HttpContext);
        var result = await acls.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerAclIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerAclHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await acls.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/acls/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerAclPlanBodyConverter))]
public sealed record ContainerAclPlanBody(string InstanceId, string Path, string AclType, IReadOnlyList<ContainerAclEntryDto> Entries, string ExpectedVersion);
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerAclConfirmBodyConverter))]
public sealed record ContainerAclConfirmBody(string PlanHash, bool Confirmed, long Version);
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerAclAcknowledgeBodyConverter))]
public sealed record ContainerAclAcknowledgeBody(long Version, bool Confirmed);

[ApiController, Authorize(Policy = "Administrator"), ContainerAclInput, Route("api/v1/files/container-acls/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerAclRecoveryController(ContainerAclWorkflowService acls, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Unknown acl operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerAclIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await acls.UnknownAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerAclIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await acls.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, ContainerAclAcknowledgeBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerAclHttp.BadKey(HttpContext);
        await acls.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerAclHttp
{
    internal static BadRequestObjectResult BadAcl(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供access/default类型及规范ACL条目（0为合法显式值）。",
        Extensions = { ["code"] = "CONTAINER_ACL_ENTRIES_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "A single canonical idempotency key matching this operation is required.",
        Extensions = { ["code"] = "CONTAINER_ACL_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
