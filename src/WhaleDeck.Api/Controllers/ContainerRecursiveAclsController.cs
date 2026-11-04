using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveAclInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recursive-acls")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveAclsController(ContainerRecursiveAclWorkflowService recursiveAcls, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No recursive ACL change occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecursiveAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> Queue(string containerId, ContainerRecursiveAclPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecursiveAclHttp.BadKey(HttpContext);
        if (command is null) return ContainerRecursiveAclHttp.BadAcl(HttpContext);
        var result = await recursiveAcls.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.AclType, command.FileEntries, command.DirectoryEntries, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await recursiveAcls.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await recursiveAcls.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no recursive ACL mutation runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecursiveAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecursiveAclBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveAclHttp.BadKey(HttpContext);
        var result = await recursiveAcls.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecursiveAclIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveAclHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await recursiveAcls.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recursive-acls/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecursiveAclPlanBodyConverter))]
public sealed record ContainerRecursiveAclPlanBody(string InstanceId, string Path, string AclType, IReadOnlyList<ContainerAclEntryDto>? FileEntries, IReadOnlyList<ContainerAclEntryDto> DirectoryEntries, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveAclInput, Route("api/v1/files/container-recursive-acls/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveAclRecoveryController(ContainerRecursiveAclWorkflowService recursiveAcls, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Partial/Unknown recursive operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerRecursiveAclIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await recursiveAcls.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerRecursiveAclIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recursiveAcls.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecursiveAclBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecursiveAclHttp.BadKey(HttpContext);
        await recursiveAcls.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecursiveAclHttp
{
    internal static BadRequestObjectResult BadAcl(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供合法的ACL类型及文件、目录目标。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_TARGETS_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
