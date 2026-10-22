using System.Text.Json;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class ContainerFileReadAudit(PlatformDbContext db) : IContainerFileReadAudit
{
    public async Task RecordAsync(TerminalActor actor, ContainerFileLocationDto location, string action, string result,
        string? version, long? sizeBytes, string? sha256, CancellationToken cancellationToken)
    {
        ContainerFileValidation.Location(location, true);
        if (action is not ("target" or "list" or "inspect" or "download") || result is not ("Started" or "Validated" or "Incomplete") ||
            sizeBytes is < 0 or > FileManagementService.MaximumBytes || version is not null && !FileManagementService.Hex(version) ||
            sha256 is not null && !FileManagementService.Hex(sha256)) throw new ArgumentException("CONTAINER_FILES_AUDIT_INVALID");
        db.AuditEvents.Add(new AuditEvent { ActorSubject = actor.Subject, Action = "containers.files." + action, TargetType = "container",
            TargetId = location.ContainerId, TraceId = actor.TraceId, SourceIp = actor.SourceIp, Result = result,
            DetailJson = JsonSerializer.Serialize(new { pathHash = FileManagementService.Hash(location.Path),
                instanceHash = FileManagementService.Hash(location.InstanceId), version, sizeBytes, sha256 }) });
        await db.SaveChangesAsync(cancellationToken);
    }
}
