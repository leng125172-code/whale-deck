using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/files/recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileRecoveryController(FileRecoveryInventoryService inventory, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Current-admin inventory across five Unknown workflows; never releases a lock or returns old login/owner credentials.</summary>
    [HttpGet, ProducesResponseType<FileRecoveryPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FileRecoveryPageDto>> List(CancellationToken cancellationToken, [FromQuery] string? after = null, [FromQuery] int take = 25) =>
        Ok(await inventory.ListAsync((await identity.ReadAsync(HttpContext)).Actor, after, take, cancellationToken));
    /// <summary>Read the current metadata even after the original login is gone. This does not authorize disposal, replay or success.</summary>
    [HttpGet("{kind}/{id:guid}"), ProducesResponseType<FileRecoveryCandidateDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FileRecoveryCandidateDto>> Get(string kind, Guid id, CancellationToken cancellationToken) =>
        Ok(await inventory.GetAsync((await identity.ReadAsync(HttpContext)).Actor, kind, id, cancellationToken));
}
