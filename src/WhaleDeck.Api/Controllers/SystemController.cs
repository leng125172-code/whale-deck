using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Contracts;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
    {
        var build = BuildIdentity.FromAssembly(typeof(Program).Assembly);
        return Ok(new
        {
            service = "whaledeck-api",
            version = $"V{build.Version}",
            build,
            status = "ok",
            utcTime = DateTimeOffset.UtcNow,
        });
    }
}
