using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/recursive-acls/history")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerRecursiveAclHistoryController(ContainerRecursiveAclHistoryService history, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Verified original ACL metadata for this current container instance and exact existing path. No mutation or restoration.</summary>
    [HttpGet, ContainerRecursiveAclHistoryQuery("instanceId", "path", "after", "take", "expectedVersion")]
    public async Task<ActionResult<ContainerRecursiveAclHistoryPageDto>> List(string containerId, [FromQuery] string instanceId, [FromQuery] string path,
        [FromQuery] string after = "", [FromQuery] int take = 25, [FromQuery] string expectedVersion = "", CancellationToken cancellationToken = default) =>
        Ok(await history.ListAsync((await identity.ReadAsync(HttpContext)).Actor, new(containerId, instanceId, path), after, take, expectedVersion, cancellationToken));

    /// <summary>Read the exact selected digest from HDD. Historical ACLs are not a claim about current permissions.</summary>
    [HttpGet("{captureId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(32)}"), ContainerRecursiveAclHistoryQuery("instanceId", "path", "expectedHistoryDigest", "offset", "take")]
    public async Task<ActionResult<ContainerRecursiveAclHistorySnapshotDto>> Read(string containerId, string captureId,
        [FromQuery] string instanceId, [FromQuery] string path, [FromQuery] string expectedHistoryDigest, [FromQuery] int offset, [FromQuery] int take, CancellationToken cancellationToken) =>
        Ok(await history.ReadAsync((await identity.ReadAsync(HttpContext)).Actor, new(containerId, instanceId, path), captureId, expectedHistoryDigest, offset, take, cancellationToken));
}

/// <summary>Reject ambiguous GET fields before ApiController can echo model-binding input.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ContainerRecursiveAclHistoryQueryAttribute : ActionFilterAttribute
{
    private readonly string[] _fields;
    public ContainerRecursiveAclHistoryQueryAttribute(params string[] fields) { _fields = fields; Order = -3001; }
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var query = context.HttpContext.Request.Query;
        if (context.ModelState.IsValid && query.ContainsKey("instanceId") && query.ContainsKey("path") &&
            (!_fields.Contains("offset",StringComparer.Ordinal) || query.ContainsKey("offset") && query.ContainsKey("take") && query.ContainsKey("expectedHistoryDigest")) && query.All(item => _fields.Contains(item.Key, StringComparer.Ordinal) && item.Value.Count == 1)) return;
        context.Result = new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "递归ACL历史查询字段缺失、重复或类型无效。",
            Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_HISTORY_QUERY_INVALID", ["traceId"] = context.HttpContext.TraceIdentifier }
        });
    }
}
