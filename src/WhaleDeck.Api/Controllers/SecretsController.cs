using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1/secrets")]
public sealed class SecretsController(IOneTimeSecretStore secrets) : ControllerBase
{
    [HttpPost("stage")]
    public async Task<IActionResult> Stage([FromBody] StageSecretRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var value = request.Generate
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            : request.Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 65_536)
            return ValidationProblem("A staged secret must contain between 16 and 65536 characters.");
        return Ok(await secrets.StoreAsync(User.RequireSubject(), value, cancellationToken));
    }

    [HttpPost("consume")]
    public async Task<IActionResult> Consume([FromBody] ConsumeSecretRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var value = await secrets.ConsumeAsync(User.RequireSubject(), request.Token, cancellationToken);
        return value is null ? NotFound() : Ok(new { value });
    }

    public sealed record ConsumeSecretRequest(string Token);
    public sealed record StageSecretRequest(string? Value, bool Generate);
}
