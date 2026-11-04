using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveAclRestoreInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recursive-acl-restores")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveAclRestoresController(ContainerRecursiveAclRestoreWorkflowService restores, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No recursive ACL restoration occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecursiveAclRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> Queue(string containerId, ContainerRecursiveAclRestorePlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecursiveAclRestoreHttp.BadKey(HttpContext);
        if (command is null) return ContainerRecursiveAclRestoreHttp.BadSource(HttpContext);
        var result = await restores.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.CaptureId, command.ExpectedHistoryDigest, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent), ContainerRecursiveAclRestoreQuery]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await restores.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}"), ContainerRecursiveAclRestoreQuery]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await restores.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no recursive ACL restoration runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecursiveAclRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecursiveAclRestoreBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveAclRestoreHttp.BadKey(HttpContext);
        var result = await restores.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecursiveAclRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursiveAclRestoreHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await restores.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recursive-acl-restores/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecursiveAclRestorePlanBodyConverter))]
public sealed record ContainerRecursiveAclRestorePlanBody(string InstanceId, string Path, string CaptureId, string ExpectedHistoryDigest, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursiveAclRestoreInput, Route("api/v1/files/container-recursive-acl-restores/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursiveAclRestoreRecoveryController(ContainerRecursiveAclRestoreWorkflowService restores, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Partial/Unknown recursive restore operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet, ContainerRecursiveAclRestoreQuery(AllowTake = true)]
    public async Task<ActionResult<IReadOnlyList<ContainerRecursiveAclRestoreIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await restores.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}"), ContainerRecursiveAclRestoreQuery]
    public async Task<ActionResult<ContainerRecursiveAclRestoreIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await restores.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecursiveAclRestoreBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecursiveAclRestoreHttp.BadKey(HttpContext);
        await restores.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecursiveAclRestoreHttp
{
    internal static BadRequestObjectResult BadSource(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须明确提供合法的恢复来源、历史摘要和当前目录版本。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_RESTORE_SOURCE_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_RESTORE_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
