using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursivePermissionInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recursive-permissions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursivePermissionsController(ContainerRecursivePermissionWorkflowService recursivePermissions, TerminalCookieIdentity identity) : ControllerBase
{
    private const string Intent = "{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}";
    /// <summary>Queue bounded, asynchronous source preflight. No recursive permission change occurs until the original login confirms its completed plan.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerRecursivePermissionIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> Queue(string containerId, ContainerRecursivePermissionPlanBody command, CancellationToken cancellationToken)
    {
        if (!Key(out var key)) return ContainerRecursivePermissionHttp.BadKey(HttpContext);
        if (command is null || !ContainerRecursivePermissionHttp.Mode(command.FileMode) || !ContainerRecursivePermissionHttp.Mode(command.DirectoryMode)) return ContainerRecursivePermissionHttp.BadModes(HttpContext);
        var result = await recursivePermissions.QueueAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.FileMode, command.DirectoryMode, command.ExpectedVersion), key, cancellationToken);
        return Accepted(Location(containerId, result.Id), result);
    }
    [HttpGet(Intent)]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await recursivePermissions.GetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    /// <summary>Read the original registration after a lost HTTP reply. A missing result never authorizes generating another key.</summary>
    [HttpGet("by-key/{key:guid}")]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await recursivePermissions.GetByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
    /// <summary>Confirm the five-minute immutable plan. Retries resolve the same execution job; no recursive mode mutation runs within this HTTP request.</summary>
    [HttpPost(Intent + "/confirm"), ProducesResponseType<ContainerRecursivePermissionIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> Confirm(string containerId, Guid id, ConfirmContainerRecursivePermissionBody command, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursivePermissionHttp.BadKey(HttpContext);
        var result = await recursivePermissions.ConfirmAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, new(command.PlanHash, command.Confirmed, command.Version), cancellationToken);
        return Accepted(Location(containerId, id), result);
    }
    [HttpPost(Intent + "/cancel"), ProducesResponseType<ContainerRecursivePermissionIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> Cancel(string containerId, Guid id, CancellationToken cancellationToken)
    {
        if (!Matches(id)) return ContainerRecursivePermissionHttp.BadKey(HttpContext);
        return Accepted(Location(containerId, id), await recursivePermissions.CancelAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));
    }
    private bool Key(out string key)
    {
        key = Request.Headers["Idempotency-Key"].ToString();
        return Request.Headers["Idempotency-Key"].Count == 1 && Guid.TryParseExact(key, "D", out var value) && value != Guid.Empty && value.ToString("D") == key;
    }
    private bool Matches(Guid id) => Key(out var key) && key == id.ToString("D");
    private static string Location(string containerId, Guid id) => $"/api/v1/containers/{containerId}/files/recursive-permissions/{id:D}";
}
[System.Text.Json.Serialization.JsonConverter(typeof(ContainerRecursivePermissionPlanBodyConverter))]
public sealed record ContainerRecursivePermissionPlanBody(string InstanceId, string Path, string FileMode, string DirectoryMode, string ExpectedVersion);

[ApiController, Authorize(Policy = "Administrator"), ContainerRecursivePermissionInput, Route("api/v1/files/container-recursive-permissions/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerRecursivePermissionRecoveryController(ContainerRecursivePermissionWorkflowService recursivePermissions, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Bounded metadata for Partial/Unknown recursive operations, accessible to current administrators without impersonating the original user.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerRecursivePermissionIntentDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await recursivePermissions.UnresolvedAsync((await identity.ReadAsync(HttpContext)).Actor, take, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContainerRecursivePermissionIntentDto>> Status(Guid id, CancellationToken cancellationToken) =>
        Ok(await recursivePermissions.RecoveryGetAsync((await identity.ReadAsync(HttpContext)).Actor, id, cancellationToken));
    /// <summary>Accept historical uncertainty only after the exact original Root operation is permanently fenced. Never deletes HDD metadata or reports them as successful.</summary>
    [HttpPost("{id:guid}/acknowledge"), ProducesResponseType<FileCancelDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Acknowledge(Guid id, AcknowledgeContainerRecursivePermissionBody command, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || Request.Headers["Idempotency-Key"].Count != 1 || Request.Headers["Idempotency-Key"].ToString() != id.ToString("D")) return ContainerRecursivePermissionHttp.BadKey(HttpContext);
        await recursivePermissions.AcknowledgeAsync((await identity.ReadAsync(HttpContext)).Actor, id, new(command.Version, command.Confirmed), cancellationToken);
        return Accepted(new FileCancelDto(id, "Acknowledged"));
    }
}

internal static class ContainerRecursivePermissionHttp
{
    internal static bool Mode(string? value) => value is { Length: 4 } && value[0] == '0' && value.Skip(1).All(c => c is >= '0' and <= '7');
    internal static BadRequestObjectResult BadModes(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须分别明确提供文件与目录的四位普通八进制权限模式。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_PERMISSION_MODES_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
    internal static BadRequestObjectResult BadKey(HttpContext context) => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "必须提供与当前操作匹配的单个规范幂等键。",
        Extensions = { ["code"] = "CONTAINER_RECURSIVE_PERMISSION_IDEMPOTENCY_KEY_REQUIRED", ["traceId"] = context.TraceIdentifier }
    });
}
