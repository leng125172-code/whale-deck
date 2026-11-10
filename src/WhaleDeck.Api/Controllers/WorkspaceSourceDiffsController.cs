using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{workspaceId:guid}/source-imports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceSourceDiffsController(WorkspaceSourceDiffs diffs, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("{importId:guid}/diff")]
    public async Task<ActionResult<WorkspaceSourceDiffPageView>> Read(Guid workspaceId, Guid importId,
        [FromQuery] string planSha256, CancellationToken token, [FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        var login = await identity.ReadAsync(HttpContext);
        try
        {
            return await diffs.ReadAsync(login.Actor, workspaceId, importId, planSha256, offset, limit,
                async () => (await identity.ReadAsync(HttpContext, login.Key)).Actor, token);
        }
        catch (WorkspaceSourceException error)
        {
            var (status, code) = error.Failure switch
            {
                WorkspaceSourceFailure.Conflict or WorkspaceSourceFailure.Missing => (409, "WORKSPACE_SOURCE_DIFF_CHANGED"),
                WorkspaceSourceFailure.Rejected => (400, "WORKSPACE_SOURCE_DIFF_QUERY_INVALID"),
                WorkspaceSourceFailure.Integrity => (502, "WORKSPACE_SOURCE_DIFF_INCOMPLETE"),
                _ => (503, "WORKSPACE_SOURCE_DIFF_UNAVAILABLE")
            };
            return Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
        }
    }
}
