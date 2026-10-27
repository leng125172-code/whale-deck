using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class ContainerFileUploadIntentValidation
{
    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IntentLifetime = TimeSpan.FromMinutes(15);
    public static DateTimeOffset CanonicalTime(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
    public static void Actor(string actor, string login)
    {
        _ = BffSessionValidation.SubjectHash(actor);
        ContainerFileValidation.Hash(login);
    }
    public static void Target(ContainerFileUploadTargetDto target)
    {
        if (target is null) throw new ArgumentException("CONTAINER_UPLOAD_TARGET_REQUIRED");
        ContainerFileValidation.Location(target.Location);
        ContainerFileValidation.Hash(target.TargetProof); ContainerFileValidation.Hash(target.ExpectedParentVersion);
        Version(target.ExpectedVersion);
        if (target.CurrentSizeBytes is < 0 or > 1073741824 || target.ExpectedVersion == "ABSENT" && target.CurrentSizeBytes != 0)
            throw new ArgumentException("CONTAINER_UPLOAD_TARGET_INVALID");
    }
    public static void Command(ContainerFileUploadCommand command)
    {
        if (command is null) throw new ArgumentException("CONTAINER_UPLOAD_COMMAND_REQUIRED");
        ContainerFileValidation.Location(command.Location); Payload(command.SizeBytes, command.Sha256); Version(command.ExpectedVersion);
    }
    public static void Claims(ContainerFileUploadPlanClaims claims)
    {
        if (claims is null) throw new ArgumentException("CONTAINER_UPLOAD_PLAN_REQUIRED");
        Actor(claims.Actor, claims.LoginBinding); Target(claims.Target); Payload(claims.SizeBytes, claims.Sha256);
        Times(claims.CreatedAtUtc, claims.ExpiresAtUtc, PlanLifetime);
    }
    public static void Binding(ContainerFileUploadBinding binding)
    {
        if (binding is null || binding.Id == Guid.Empty || binding.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("CONTAINER_UPLOAD_BINDING_INVALID");
        Actor(binding.Actor, binding.LoginBinding); Target(binding.Target); Payload(binding.SizeBytes, binding.Sha256);
        ContainerFileValidation.Hash(binding.PlanHash); Times(binding.CreatedAtUtc, binding.ExpiresAtUtc, IntentLifetime);
        if (string.IsNullOrWhiteSpace(binding.TraceId) || binding.TraceId.Length > 128 || binding.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_UPLOAD_TRACE_INVALID");
        try { _ = new UTF8Encoding(false, true).GetByteCount(binding.TraceId); }
        catch (EncoderFallbackException) { throw new ArgumentException("CONTAINER_UPLOAD_TRACE_INVALID"); }
    }
    public static ContainerFileUploadBinding ReadBinding(ContainerFileUploadOperation row)
    {
        if (row.OwnerHash != Owner(row.ActorSubject, row.LoginBinding)) throw new ArgumentException("CONTAINER_UPLOAD_OWNER_INVALID");
        var target = new ContainerFileUploadTargetDto(new(row.ContainerId, row.InstanceId, row.RelativePath), row.TargetProof,
            row.ExpectedParentVersion, row.ExpectedVersion, row.PreviousSizeBytes);
        var binding = new ContainerFileUploadBinding(row.Id, row.IdempotencyKey, row.ActorSubject, row.LoginBinding, row.TraceId,
            target, row.SizeBytes, row.Sha256, row.PlanHash, row.CreatedAtUtc, row.ExpiresAtUtc);
        Binding(binding); return binding;
    }
    public static string Owner(string actor, string login)
    {
        Actor(actor, login);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("whaledeck-container-file-owner-v1:" + BffSessionValidation.SubjectHash(actor) + ":" + login)));
    }
    public static ContainerFileStageDto Stage(ContainerFileUploadOperation row)
    {
        _ = ReadBinding(row);
        return new(new(row.ContainerId, row.InstanceId, row.RelativePath), row.Id, row.OwnerHash, row.SizeBytes, row.Sha256,
            row.ExpectedVersion, row.ExpectedParentVersion, row.TargetProof, Context(row));
    }
    public static ContainerFileUploadReferenceDto Reference(ContainerFileUploadOperation row)
    {
        _ = ReadBinding(row);
        return new(row.ContainerId, row.InstanceId, row.Id, row.OwnerHash, row.TargetProof, Context(row));
    }
    private static ContainerFileWriteContextDto Context(ContainerFileUploadOperation row) => new(row.ActorSubject, row.TraceId, row.Id, row.IdempotencyKey);
    private static void Payload(long size, string sha)
    { if (size is < 0 or > 1073741824) throw new ArgumentException("CONTAINER_UPLOAD_SIZE_INVALID"); ContainerFileValidation.Hash(sha); }
    private static void Version(string value) { if (value != "ABSENT") ContainerFileValidation.Hash(value); }
    private static void Times(DateTimeOffset created, DateTimeOffset expires, TimeSpan duration)
    {
        if (created.Offset != TimeSpan.Zero || expires.Offset != TimeSpan.Zero || created != CanonicalTime(created) || expires != CanonicalTime(expires) ||
            expires - created != duration || created.ToUnixTimeMilliseconds() <= 0)
            throw new ArgumentException("CONTAINER_UPLOAD_TIME_INVALID");
    }
}
