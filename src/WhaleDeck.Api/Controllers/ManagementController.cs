using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1")]
public sealed class ManagementController(
    IAgentGateway agent,
    IManagementQuery queries,
    ICatalogProvider catalog,
    IIdentityDirectory identity,
    OperationService operations,
    ApplicationService applications,
    ContainerUpdateService containerUpdates,
    ContainerDiagnosticService containerDiagnostics,
    DockerEventService dockerEvents) : ControllerBase
{
    [HttpGet("containers")]
    public async Task<IActionResult> Containers([FromQuery] bool includeStopped = true, CancellationToken cancellationToken = default) =>
        Ok(await agent.ListContainersAsync(includeStopped, cancellationToken));

    [HttpGet("containers/{containerId}/logs")]
    public async Task<IActionResult> ContainerLogs(
        string containerId,
        [FromQuery] int tail = 300,
        [FromQuery] int sinceMinutes = 60,
        CancellationToken cancellationToken = default) =>
        Ok(await agent.GetContainerLogsAsync(containerId, tail, sinceMinutes, cancellationToken));

    [HttpGet("containers/{containerId}/stats")]
    public async Task<IActionResult> ContainerStats(string containerId, CancellationToken cancellationToken = default) =>
        Ok(await agent.GetContainerStatsAsync(containerId, cancellationToken));

    /// <summary>Reads the bounded Agent container usage cache; reading does not trigger Docker sampling.</summary>
    [HttpGet("containers/usage")]
    public async Task<IActionResult> ContainerUsage(CancellationToken cancellationToken) =>
        Ok(await agent.GetContainerUsageAsync(cancellationToken));

    [HttpGet("containers/{containerId}/inspect")]
    public async Task<IActionResult> ContainerInspect(string containerId, CancellationToken cancellationToken = default) =>
        Ok(await agent.InspectContainerAsync(containerId, cancellationToken));

    /// <summary>Exports a bounded, redacted diagnostic bundle for one container.</summary>
    [HttpGet("containers/{containerId}/diagnostics")]
    public async Task<IActionResult> ContainerDiagnostics(string containerId, CancellationToken cancellationToken = default)
    {
        var bundle = await containerDiagnostics.CreateAsync(containerId, cancellationToken);
        return File(bundle.Content, "application/zip", bundle.FileName);
    }

    [HttpGet("containers/update-history")]
    public async Task<IActionResult> ContainerUpdateHistory(
        [FromQuery] string? containerId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await containerUpdates.ListAsync(containerId, take, cancellationToken));

    [HttpGet("containers/events")]
    public async Task<IActionResult> ContainerEvents(
        [FromQuery] string? containerId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await dockerEvents.ListAsync(containerId, take, cancellationToken));

    [HttpGet("applications/popular")]
    public async Task<IActionResult> Popular(CancellationToken cancellationToken) => Ok(await catalog.GetPopularAsync(cancellationToken));

    [HttpGet("applications/installations")]
    public async Task<IActionResult> ApplicationInstallations(CancellationToken cancellationToken) =>
        Ok(await applications.ListAsync(cancellationToken));

    [HttpGet("applications/installations/{id:guid}")]
    public async Task<IActionResult> Application(Guid id, CancellationToken cancellationToken)
    {
        var application = await applications.FindAsync(id, cancellationToken);
        return application is null ? NotFound() : Ok(application);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) => Ok(await identity.ListUsersAsync(cancellationToken));

    [HttpGet("groups")]
    public async Task<IActionResult> Groups(CancellationToken cancellationToken) => Ok(await identity.ListGroupsAsync(cancellationToken));

    [HttpGet("sso")]
    public async Task<IActionResult> Sso(CancellationToken cancellationToken) => Ok(await identity.ListSsoApplicationsAsync(cancellationToken));

    [HttpGet("config-repository/status")]
    public async Task<IActionResult> ConfigRepositoryStatus(CancellationToken cancellationToken) =>
        Ok(await agent.GetConfigRepositoryStatusAsync(cancellationToken));

    [HttpGet("docker/settings")]
    public async Task<IActionResult> DockerSettings(CancellationToken cancellationToken) =>
        Ok(await agent.GetDockerSettingsAsync(cancellationToken));

    [HttpPost("applications/image-metadata")]
    public async Task<IActionResult> ApplicationImageMetadata([FromBody] ImageMetadataRequest request, CancellationToken cancellationToken) =>
        Ok(await agent.InspectApplicationImageAsync(request.Image, request.PullIfMissing, cancellationToken));

    [HttpGet("{area:regex(^(images|networks|volumes|compose-projects|databases|applications|systemd)$)}")]
    public async Task<IActionResult> Resources(string area, CancellationToken cancellationToken) => Ok(await queries.ListAsync(area, cancellationToken));

    [HttpPost("{area:regex(^(containers|docker|databases|backups|applications|compose-projects|host|config-repository|platform)$)}/{operation}/plan")]
    public async Task<IActionResult> Plan(string area, string operation, [FromBody] PlanRequest request, CancellationToken cancellationToken) =>
        Ok(await agent.PlanAsync(area, operation, request.ResourceId, request.Parameters ?? new Dictionary<string, string>(), cancellationToken));

    [HttpPost("{area:regex(^(containers|docker|databases|backups|applications|compose-projects|host|config-repository|platform|identity)$)}/{operation}")]
    public async Task<IActionResult> Enqueue(string area, string operation, [FromBody] OperationRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? request.IdempotencyKey;
        var requestJson = JsonSerializer.Serialize(new { resourceId = request.ResourceId, parameters = request.Parameters ?? new Dictionary<string, string>(), planHash = request.PlanHash });
        var command = new OperationCommand(area, operation, request.ResourceId, idempotencyKey, requestJson, request.PlanHash, request.Confirmed);
        var job = await operations.EnqueueAsync(User.RequireSubject(), HttpContext.TraceIdentifier, command, cancellationToken);
        return AcceptedAtAction(nameof(JobsController.Get), "Jobs", new { id = job.Id }, job);
    }

    public sealed record OperationRequest(string? ResourceId, string IdempotencyKey, IReadOnlyDictionary<string, string>? Parameters, string? PlanHash, bool Confirmed);
    public sealed record PlanRequest(string? ResourceId, IReadOnlyDictionary<string, string>? Parameters);
    public sealed record ImageMetadataRequest(string Image, bool PullIfMissing = false);
}
