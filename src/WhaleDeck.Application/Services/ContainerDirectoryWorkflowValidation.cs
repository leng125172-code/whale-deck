using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerDirectoryWorkflowValidation
{
    public static readonly JsonSerializerOptions Json = new() { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static void Claims(ContainerDirectoryPlanClaims value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Actor) || value.Actor.Length > 256 || value.Actor.Any(char.IsControl) ||
            !FileManagementService.Hex(value.LoginBinding) || value.CreatedAtUtc <= DateTimeOffset.UnixEpoch || value.CreatedAtUtc.Offset != TimeSpan.Zero ||
            value.CreatedAtUtc.Ticks % 10 != 0 || value.ExpiresAtUtc.Offset != TimeSpan.Zero || value.ExpiresAtUtc != value.CreatedAtUtc.AddMinutes(5)) throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        ContainerDirectoryValidation.Plan(value.Plan);
    }
    public static void Binding(ContainerDirectoryBindingDto value)
    {
        if (value is null || value.Id == Guid.Empty || value.IdempotencyKey == Guid.Empty ||
            string.IsNullOrWhiteSpace(value.Actor) || value.Actor.Length > 256 || value.Actor.Any(char.IsControl) || value.Owner != Owner(value.Actor, value.LoginBinding) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl) || !FileManagementService.Hex(value.PlanHash) ||
            value.CreatedAtUtc <= DateTimeOffset.UnixEpoch || value.CreatedAtUtc.Offset != TimeSpan.Zero || value.CreatedAtUtc.Ticks % 10 != 0 ||
            value.ExpiresAtUtc.Offset != TimeSpan.Zero || value.ExpiresAtUtc != value.CreatedAtUtc.AddMinutes(15) || !FileManagementService.Hex(value.LoginBinding)) throw new ArgumentException("CONTAINER_DIRECTORY_INTENT_INVALID");
        ContainerDirectoryValidation.Plan(value.Plan);
    }
    public static string Owner(string actor, string login) => FileManagementService.Hash("whaledeck-container-directory-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login);
    public static ContainerDirectoryPlanDto ReadPlan(ContainerDirectoryOperation row)
    {
        if (row.PlanJson is not { Length: > 0 and <= 32768 }) throw new ArgumentException("CONTAINER_DIRECTORY_INTENT_INVALID");
        var plan = JsonSerializer.Deserialize<ContainerDirectoryPlanDto>(row.PlanJson, Json) ?? throw new ArgumentException("CONTAINER_DIRECTORY_PLAN_INVALID");
        ContainerDirectoryValidation.Plan(plan);
        if (JsonSerializer.Serialize(plan, Json) != row.PlanJson || plan.Command.Location != new ContainerFileLocationDto(row.ContainerId, row.InstanceId, row.RelativePath))
            throw new ArgumentException("CONTAINER_DIRECTORY_INTENT_INVALID");
        return plan;
    }
    public static ContainerDirectoryBindingDto ReadBinding(ContainerDirectoryOperation row) => new(row.Id, row.IdempotencyKey, row.ActorSubject,
        row.LoginBinding, row.TraceId, row.OwnerHash, ReadPlan(row), row.PlanHash, row.CreatedAtUtc, row.ExpiresAtUtc);
    public static ContainerDirectoryOperationDto Reference(ContainerDirectoryOperation row) => new(row.Id, row.OwnerHash, ReadPlan(row),
        new(row.ActorSubject, row.TraceId, row.Id, row.IdempotencyKey));
    public static DateTimeOffset Time(DateTimeOffset value) => value.ToUniversalTime().AddTicks(-(value.Ticks % 10));
    public static Guid Key(SubmitContainerDirectoryCommand command)
    {
        if (command is null || !command.Confirmed || !Guid.TryParseExact(command.IdempotencyKey, "D", out var id) || id == Guid.Empty || id.ToString("D") != command.IdempotencyKey)
            throw new ArgumentException("CONTAINER_DIRECTORY_CONFIRMATION_REQUIRED");
        return id;
    }
}
