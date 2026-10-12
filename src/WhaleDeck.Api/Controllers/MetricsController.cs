using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1/metrics")]
public sealed class MetricsController(IMetricsQuery metrics) : ControllerBase
{
    /// <summary>Read bounded live samples or aggregated 1h/24h/7d history. Missing measurements remain null.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string[] kinds, [FromQuery] int take = 60, [FromQuery] string window = "live", CancellationToken cancellationToken = default)
    {
        return Ok(await metrics.GetHistoryAsync(kinds, take, cancellationToken, window));
    }
}
