using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator")]
[Route("api/v1/development/workspaces/{workspaceId:guid}/report-access/{groupId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(4096)]
public sealed class WorkspaceReportGrantsController(WorkspaceReportGrantManagement grants, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WorkspaceReportGrant>> Get(Guid workspaceId, Guid groupId, CancellationToken token) =>
        Ok(await grants.GetAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, groupId, token));

    [HttpPut, Consumes("application/json")]
    public async Task<ActionResult<WorkspaceReportGrant>> Set(Guid workspaceId, Guid groupId, WorkspaceReportGrantBody body,
        CancellationToken token) =>
        Ok(await grants.SetAsync((await identity.ReadAsync(HttpContext)).Actor, workspaceId, groupId,
            body.Enabled, body.ExpectedVersion, token));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceReportGrantBody(
    [property: JsonRequired] bool Enabled, [property: JsonRequired] long ExpectedVersion);
