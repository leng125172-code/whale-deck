using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), ContainerAclInput]
[Route("api/v1/containers/{containerId:regex((?-i:\\A[[a-f0-9]]+\\z)):length(64)}/files/acls/restores")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(65536)]
public sealed class ContainerAclRestoresController(ContainerAclWorkflowService acls, TerminalCookieIdentity identity) : ControllerBase
{
    /// <summary>Select authoritative HDD metadata for a new plan. Confirmation/cancellation use the original ACL intent routes.</summary>
    [HttpPost, Consumes("application/json"), ProducesResponseType<ContainerAclRestoreIntentDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ContainerAclRestoreIntentDto>> Queue(string containerId, ContainerAclRestoreBody command, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        if (Request.Headers["Idempotency-Key"].Count != 1 || !Guid.TryParseExact(key, "D", out var value) || value == Guid.Empty || value.ToString("D") != key)
            return ContainerAclHttp.BadKey(HttpContext);
        var result = await acls.QueueRestoreAsync((await identity.ReadAsync(HttpContext)).Actor,
            new(new(containerId, command.InstanceId, command.Path), command.CaptureId, command.ExpectedHistoryDigest, command.ExpectedVersion), key, cancellationToken);
        return Accepted($"/api/v1/containers/{containerId}/files/acls/restores/{result.Intent.Id:D}", result);
    }

    [HttpGet("{id:regex((?-i:\\A[[a-f0-9-]]+\\z)):length(36):guid}"), ContainerAclHistoryQuery]
    public async Task<ActionResult<ContainerAclRestoreIntentDto>> Status(string containerId, Guid id, CancellationToken cancellationToken) =>
        Ok(await acls.GetRestoreAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, id, cancellationToken));

    [HttpGet("by-key/{key:guid}"), ContainerAclHistoryQuery]
    public async Task<ActionResult<ContainerAclRestoreIntentDto>> ByKey(string containerId, string key, CancellationToken cancellationToken) =>
        Ok(await acls.GetRestoreByKeyAsync((await identity.ReadAsync(HttpContext)).Actor, containerId, key, cancellationToken));
}

[JsonConverter(typeof(ContainerAclRestoreBodyConverter))]
public sealed record ContainerAclRestoreBody(string InstanceId, string Path, string CaptureId, string ExpectedHistoryDigest, string ExpectedVersion);

public sealed class ContainerAclRestoreBodyConverter : JsonConverter<ContainerAclRestoreBody>
{
    public override bool HandleNull => true;
    public override ContainerAclRestoreBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement;
        ContainerAclInput.Fields(root, ["instanceId", "path", "captureId", "expectedHistoryDigest", "expectedVersion"]);
        return new(ContainerAclInput.Text(root, "instanceId"), ContainerAclInput.Text(root, "path"), ContainerAclInput.Text(root, "captureId"),
            ContainerAclInput.Text(root, "expectedHistoryDigest"), ContainerAclInput.Text(root, "expectedVersion"));
    }
    public override void Write(Utf8JsonWriter writer, ContainerAclRestoreBody value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("instanceId", value.InstanceId); writer.WriteString("path", value.Path); writer.WriteString("captureId", value.CaptureId);
        writer.WriteString("expectedHistoryDigest", value.ExpectedHistoryDigest); writer.WriteString("expectedVersion", value.ExpectedVersion); writer.WriteEndObject();
    }
}
