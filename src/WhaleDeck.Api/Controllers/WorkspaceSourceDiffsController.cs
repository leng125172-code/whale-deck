using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize, Route("api/v1/development/workspaces/{workspaceId:guid}/source-imports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceSourceDiffsController(WorkspaceSourceDiffs diffs, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("{importId:guid}/diff")]
    public Task<ActionResult<WorkspaceSourceDiffPageView>> Read(Guid workspaceId, Guid importId,
        [FromQuery] string planSha256, CancellationToken token, [FromQuery] int offset = 0, [FromQuery] int limit = 50) =>
        ReadAsync((login, refresh) => diffs.ReadAsync(login.Actor, workspaceId, importId, planSha256, offset, limit, refresh, token));

    [HttpPost("{importId:guid}/diff/text"), RequestSizeLimit(12 * 1024)]
    public Task<ActionResult<WorkspaceSourceTextDiffView>> ReadText(Guid workspaceId, Guid importId,
        [FromBody] TextDiffQuery query, CancellationToken token) =>
        ReadAsync((login, refresh) => diffs.ReadTextAsync(login.Actor, workspaceId, importId, query.PlanSha256, query.Path, refresh, token));

    public sealed record TextDiffQuery(string PlanSha256, string Path);

    private async Task<ActionResult<T>> ReadAsync<T>(Func<TerminalLogin, Func<Task<TerminalActor>>, Task<T>> read)
    {
        var login = await identity.ReadAsync(HttpContext);
        try
        {
            return await read(login, async () => (await identity.ReadAsync(HttpContext, login.Key)).Actor);
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
