using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Middleware;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Development;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/development/workspaces/{workspaceId:guid}/runs/{runId:guid}/report")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceRunReportsController(WorkspaceRunReportDownloads downloads, TerminalCookieIdentity identity) : ControllerBase
{
    [HttpGet("{format}")]
    public async Task<IActionResult> Download(Guid workspaceId, Guid runId, string format, CancellationToken cancellationToken)
    {
        var fetchSite = Request.Headers["Sec-Fetch-Site"];
        if ((fetchSite.Count != 0 && (fetchSite.Count != 1 || fetchSite[0] is not ("same-origin" or "none"))) ||
            (Request.Headers.ContainsKey("Origin") && !SameOriginWriteProtection.IsSameOrigin(Request)))
            return StatusCode(StatusCodes.Status403Forbidden);
        if (Request.Query.Count != 0 || workspaceId == Guid.Empty || runId == Guid.Empty)
            return BadRequest();
        var reportFormat = format switch
        {
            "json" => WorkspaceReportFormat.Json,
            "markdown" => WorkspaceReportFormat.Markdown,
            _ => (WorkspaceReportFormat?)null
        };
        if (reportFormat is not { } selected) return NotFound();

        var actor = (await identity.ReadAsync(HttpContext)).Actor;
        var result = await downloads.ReadAsync(actor, workspaceId, runId, selected, cancellationToken);
        var json = selected == WorkspaceReportFormat.Json;
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["X-Content-SHA256"] = result.Sha256;
        return File(result.Content, json ? "application/json; charset=utf-8" : "text/markdown; charset=utf-8",
            $"workspace-run-{runId:D}.{(json ? "json" : "md")}", enableRangeProcessing: false);
    }
}
