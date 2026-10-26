namespace WhaleDeck.Application.Models;

public sealed record ContainerFileUploadTargetDto(ContainerFileLocationDto Location, string TargetProof,
    string ExpectedParentVersion, string ExpectedVersion, long CurrentSizeBytes);
public sealed record ContainerFileWriteContextDto(string ActorSubject, string TraceId, Guid JobId, Guid IdempotencyKey);
public sealed record ContainerFileStageDto(ContainerFileLocationDto Location, Guid OperationId, string Owner, long SizeBytes,
    string Sha256, string ExpectedVersion, string ExpectedParentVersion, string ExpectedTargetProof, ContainerFileWriteContextDto Context);
public sealed record ContainerFileUploadReferenceDto(string ContainerId, string InstanceId, Guid OperationId, string Owner,
    string ExpectedTargetProof, ContainerFileWriteContextDto Context);
public sealed record ContainerFileUploadReceiptDto(string ContainerId, string InstanceId, Guid OperationId, string TargetProof,
    FileUploadReceiptDto Upload, bool Replayed);
