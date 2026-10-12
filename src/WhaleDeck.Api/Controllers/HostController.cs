using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1/host")]
public sealed class HostController(IAgentGateway agent, HostProcessService processes) : ControllerBase
{
    /// <summary>Returns bounded, sampled process statistics. CPU 100% represents one logical core.</summary>
    [HttpGet("processes")]
    public async Task<ActionResult<HostProcessPageDto>> Processes([FromQuery] HostProcessQuery query, CancellationToken cancellationToken) =>
        Ok(await processes.ListAsync(query, cancellationToken));

    /// <summary>Reads process details only when PID and start identity still match. No command line or environment is exposed.</summary>
    [HttpGet("processes/{pid:int}")]
    public async Task<ActionResult<HostProcessDto>> Process(int pid, [FromQuery] string instanceId, CancellationToken cancellationToken) =>
        Ok(await processes.GetAsync(pid, instanceId, cancellationToken));

    [HttpGet("network-interfaces")]
    public async Task<IActionResult> NetworkInterfaces(CancellationToken cancellationToken) =>
        Ok(await agent.ListResourcesAsync("host-network-interfaces", cancellationToken));

    [HttpGet("storage-devices")]
    public async Task<IActionResult> StorageDevices(CancellationToken cancellationToken) =>
        Ok(await agent.ListResourcesAsync("host-storage-devices", cancellationToken));

    [HttpGet("journal")]
    public async Task<IActionResult> Journal(
        [FromQuery] string? unit,
        [FromQuery] int take = 200,
        [FromQuery] int sinceMinutes = 1440,
        [FromQuery] string? priority = null,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default) =>
        Ok(await agent.QueryJournalAsync(unit, take, sinceMinutes, priority, keyword, cancellationToken));
}
