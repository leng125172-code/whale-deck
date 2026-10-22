using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Files;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/v1/containers/{containerId}/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContainerFilesController(ContainerFileReadService files, ContainerFileHttpTransport transport, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Ordinary running container only; returns its exact startup instance. Core containers cannot use this generic file entry.</summary>
    [HttpGet("target")]
    public async Task<ActionResult<ContainerFileTargetDto>> Target(string containerId, CancellationToken cancellationToken) =>
        Ok(await files.TargetAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, cancellationToken));

    [HttpGet("entries")]
    public async Task<ActionResult<FilePageDto>> List(string containerId, [FromQuery] string instanceId, [FromQuery] string path = "",
        [FromQuery] string after = "", [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(await files.ListAsync((await identity.ReadAsync(HttpContext)).Actor, new(containerId, instanceId, path), after, take, cancellationToken));

    [HttpGet("entry")]
    public async Task<ActionResult<FileEntryDto>> Inspect(string containerId, [FromQuery] string instanceId, [FromQuery] string path, CancellationToken cancellationToken) =>
        Ok(await files.InspectAsync((await identity.ReadAsync(HttpContext)).Actor, new(containerId, instanceId, path), cancellationToken));

    /// <summary>Instance/version-bound read-only attachment, at most 1 GiB/310s. No ranges; exact RPC EOF, SHA256 and metadata audit precede final bytes.</summary>
    [HttpGet("download"), Produces("application/octet-stream")]
    public async Task Download(string containerId, [FromQuery] string instanceId, [FromQuery] string path, [FromQuery] string expectedVersion) =>
        await transport.DownloadAsync(HttpContext, await identity.ReadAsync(HttpContext), new(containerId, instanceId, path), expectedVersion);
}
