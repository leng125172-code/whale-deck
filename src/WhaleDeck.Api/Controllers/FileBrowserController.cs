using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileBrowserController(FileBrowserService browser, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Lazy directory tree page. Protected directories, links and nested mounts are excluded. Version changes require refreshing the branch.</summary>
    [HttpGet("directories")]
    public async Task<ActionResult<FilePageDto>> Directories([FromQuery] string rootId, [FromQuery] string path = "", [FromQuery] string after = "", [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(await browser.DirectoriesAsync((await identity.ReadAsync(HttpContext)).Actor, new(rootId, path), after, take, cancellationToken));
    /// <summary>Read-only literal filename search, not content/regex/glob. POST bounds the opaque cursor without putting it in URLs. Requires Cookie/CSRF/Origin. Each page scans at most 10,000 entries/5 seconds and returns at most 100 hits with a ten-minute root/query/version-bound cursor. Partial limits are explicit; a changed directory returns conflict, not a successful empty result.</summary>
    [HttpPost("search"), RequestSizeLimit(65536)]
    public async Task<ActionResult<FileSearchPage>> Search(FileSearchCommand command, CancellationToken cancellationToken) =>
        Ok(await browser.SearchAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));

    /// <summary>Literal UTF-8 text-content search within registered list+download roots. No regex or glob. One first matching line per file, bounded escaped excerpt, per-file limit up to 16 MiB, at most 64 MiB and five seconds per page. Binary/oversized/unsafe entries and incomplete scans are explicit. A ten-minute content/query/size/root/version-bound cursor cannot be used for filename searches. POST requires current administrator Cookie, CSRF and same Origin; no Job or transcript is written.</summary>
    [HttpPost("content-search"), RequestSizeLimit(65536)]
    public async Task<ActionResult<FileContentSearchPage>> ContentSearch(FileContentSearchCommand command, CancellationToken cancellationToken) =>
        Ok(await browser.SearchContentAsync((await identity.ReadAsync(HttpContext)).Actor, command, cancellationToken));
}
