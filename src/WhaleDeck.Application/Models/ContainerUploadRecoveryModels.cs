namespace WhaleDeck.Application.Models;

/// <summary>Retained journal facts only; no current filesystem, success or deletion proof.</summary>
public sealed record ContainerUploadRecoveryObservationDto(string ContainerId, string InstanceId, Guid OperationId, string TargetProof,
    string RootState, string OriginalRecordHash, bool Sealed, Guid? SealId, bool MayHavePublished, bool RetainedObjects, string ObservationHash);
