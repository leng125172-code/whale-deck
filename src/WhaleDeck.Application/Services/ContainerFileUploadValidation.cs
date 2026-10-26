using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public static class ContainerFileUploadValidation
{
    public static void Stage(ContainerFileStageDto request)
    {
        ContainerFileValidation.Location(request.Location);
        if (request.OperationId == Guid.Empty || request.SizeBytes is < 0 or > 1073741824L) throw new ArgumentException("CONTAINER_UPLOAD_REQUEST_INVALID");
        ContainerFileValidation.Hash(request.Owner); ContainerFileValidation.Hash(request.Sha256);
        ContainerFileValidation.Hash(request.ExpectedParentVersion); ContainerFileValidation.Hash(request.ExpectedTargetProof);
        if (request.ExpectedVersion != "ABSENT") ContainerFileValidation.Hash(request.ExpectedVersion);
        Context(request.Context);
    }
    public static void Reference(ContainerFileUploadReferenceDto request)
    {
        ContainerFileValidation.Location(new(request.ContainerId, request.InstanceId, ""), empty: true);
        if (request.OperationId == Guid.Empty) throw new ArgumentException("CONTAINER_UPLOAD_REQUEST_INVALID");
        ContainerFileValidation.Hash(request.Owner); ContainerFileValidation.Hash(request.ExpectedTargetProof); Context(request.Context);
    }
    private static void Context(ContainerFileWriteContextDto value)
    {
        if (value is null || value.JobId == Guid.Empty || value.IdempotencyKey == Guid.Empty ||
            string.IsNullOrWhiteSpace(value.ActorSubject) || value.ActorSubject.Length > 256 || value.ActorSubject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(value.TraceId) || value.TraceId.Length > 128 || value.TraceId.Any(char.IsControl))
            throw new ArgumentException("CONTAINER_UPLOAD_CONTEXT_INVALID");
    }
}
