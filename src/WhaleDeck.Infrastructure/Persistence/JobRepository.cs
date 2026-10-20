using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Infrastructure.Persistence;

public sealed class JobRepository(PlatformDbContext dbContext) : IJobRepository
{
    public async Task<OperationJob> EnqueueAsync(OperationJob job, OutboxMessage outbox, AuditEvent audit, CancellationToken cancellationToken)
    {
        var existing = await dbContext.OperationJobs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ActorSubject == job.ActorSubject && item.IdempotencyKey == job.IdempotencyKey, cancellationToken);
        if (existing is not null) return MatchExisting(existing, job);

        dbContext.OperationJobs.Add(job);
        dbContext.OperationJobEvents.Add(new OperationJobEvent
        {
            JobId = job.Id,
            Sequence = 1,
            State = job.State,
            Phase = job.Phase,
            MessageCode = "JOB_ACCEPTED"
        });
        dbContext.OutboxMessages.Add(outbox);
        dbContext.AuditEvents.Add(audit);
        if (job.JobType is "backups.run" or "applications.backup")
        {
            using var request = JsonDocument.Parse(job.RequestJson);
            var resourceExternalId = request.RootElement.GetProperty("resourceId").GetString();
            var resource = await dbContext.ManagedResources
                .Where(item => (item.ResourceType == "Database" || item.ResourceType == "Container" ||
                                job.JobType == "applications.backup" && item.ResourceType == "ComposeApplication") &&
                               item.ExternalId == resourceExternalId)
                .OrderByDescending(item => item.ResourceType == "Database")
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException("Managed database resource was not found.");
            var retentionDays = 14;
            if (request.RootElement.TryGetProperty("parameters", out var parameters) &&
                parameters.TryGetProperty("retentionDays", out var configured) &&
                int.TryParse(configured.GetString(), out var parsedRetentionDays))
            {
                retentionDays = Math.Clamp(parsedRetentionDays, 1, 3650);
            }
            dbContext.BackupRecords.Add(new BackupRecord
            {
                InstanceResourceId = resource.Id,
                JobId = job.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(retentionDays)
            });
        }
        // SaveChanges commits the job, event, outbox and audit atomically and is
        // compatible with the configured Npgsql retrying execution strategy.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.OperationJobs.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ActorSubject == job.ActorSubject && item.IdempotencyKey == job.IdempotencyKey, cancellationToken);
            if (winner is null) throw;
            return MatchExisting(winner, job);
        }
        return job;
    }

    private static OperationJob MatchExisting(OperationJob existing, OperationJob requested)
    {
        if (existing.JobType != requested.JobType || !JsonRequestsEqual(existing.RequestJson, requested.RequestJson))
            throw new InvalidOperationException("The idempotency key is already associated with a different request.");
        return existing;
    }

    internal static bool JsonRequestsEqual(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElementsEqual(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectsEqual(left, right),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                                   left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => JsonElementsEqual(pair.First, pair.Second)),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    private static bool ObjectsEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var rightProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        return leftProperties.Count == rightProperties.Count && leftProperties.All(pair =>
            rightProperties.TryGetValue(pair.Key, out var rightValue) && JsonElementsEqual(pair.Value, rightValue));
    }

    public Task<OperationJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.OperationJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<OperationJob>> ListAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.OperationJobs.AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).Take(take).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<OperationJobEvent>> ListEventsAsync(Guid id, long afterSequence, CancellationToken cancellationToken) =>
        await dbContext.OperationJobEvents.AsNoTracking().Where(item => item.JobId == id && item.Sequence > afterSequence)
            .OrderBy(item => item.Sequence).Take(500).ToArrayAsync(cancellationToken);

    public async Task RequestCancellationAsync(Guid id, string actorSubject, CancellationToken cancellationToken)
    {
        var job = await dbContext.OperationJobs.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Job was not found.");
        if (job.State is "Succeeded" or "Failed" or "RolledBack" or "Canceled")
        {
            throw new InvalidOperationException("Completed jobs cannot be canceled.");
        }
        job.CancelRequestedAtUtc = DateTimeOffset.UtcNow;
        job.Version++;
        dbContext.OutboxMessages.Add(new OutboxMessage { MessageType = job.JobType switch { "notifications.private-configuration" => "NotificationPrivateCancellationRequested", "files.upload" => "FileUploadCancellationRequested", "files.mutation" => "FileMutationCancellationRequested", "files.tree-copy-plan" or "files.tree-copy" => "FileTreeCopyCancellationRequested", "files.tree-pack-plan" or "files.tree-pack" => "FileTreePackingCancellationRequested", "files.tree-trash-plan" or "files.tree-trash" => "FileTreeRecyclingCancellationRequested", "files.tree-unpack-plan" or "files.tree-unpack" => "FileTreeUnpackingCancellationRequested", "files.tree-restore-plan" or "files.tree-restore" => "FileTreeRestoringCancellationRequested", "files.tree-overwrite-restore-plan" or "files.tree-overwrite-restore" => "FileTreeOverwriteRestoringCancellationRequested", "files.tree-move-plan" or "files.tree-move" => "FileTreeMoveCancellationRequested", "files.recovery-plan" or "files.recovery" => "FileRecoveryCancellationRequested", _ => "OperationCancellationRequested" }, PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { jobId = id, actor = actorSubject }) });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
