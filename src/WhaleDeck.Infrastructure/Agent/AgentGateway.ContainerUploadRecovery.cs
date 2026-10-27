using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerUploadRecoveryGateway
{
    private static readonly HashSet<string> ContainerRecoveryStates = new(StringComparer.Ordinal)
    { "Missing", "Receiving", "Verified", "Staged", "Interrupted", "Aborted", "Discarded", "Prevented", "Unknown", "PublishPreparing", "PublishApplying", "PublishInterrupted", "Published", "PublishDiscarding", "PublishDiscarded" };

    private async Task<ContainerFileWriteService.ContainerFileWriteServiceClient> ContainerRecoveryClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 41 ||
            !capabilities.Capabilities.Contains("docker.files.bound-uploads") || !capabilities.Capabilities.Contains("docker.files.upload-cancel-fence") ||
            !capabilities.Capabilities.Contains("docker.files.upload-recovery-seal"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_UPLOAD_RECOVERY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<ContainerUploadRecoveryObservationDto> ObserveContainerUploadRecoveryAsync(ContainerFileUploadReferenceDto original, CancellationToken cancellationToken)
    {
        var request = RecoveryReference(original); var client = await ContainerRecoveryClient(cancellationToken);
        var response = await client.ObserveRecoveryAsync(request, ContainerWriteHeaders("ObserveRecovery", request),
            deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        return RecoveryObservation(original, request, response, null);
    }
    public async Task<ContainerUploadRecoveryObservationDto> SealContainerUploadRecoveryAsync(ContainerFileUploadReferenceDto original, Guid recoveryId,
        TerminalActor currentActor, string expectedObservationHash, CancellationToken cancellationToken)
    {
        var reference = RecoveryReference(original);
        if (recoveryId == Guid.Empty || recoveryId == original.OperationId || !FileManagementService.Hex(expectedObservationHash))
            throw new ArgumentException("CONTAINER_UPLOAD_RECOVERY_REQUEST_INVALID");
        _ = BffSessionValidation.SubjectHash(currentActor.Subject);
        if (!BffSessionValidation.ValidBinding(currentActor.LoginBinding)) throw new ArgumentException("CONTAINER_UPLOAD_RECOVERY_ACTOR_INVALID");
        // A new recovery actor/job is separate from the immutable original.
        var current = new ContainerFileWriteContextDto(currentActor.Subject, currentActor.TraceId, recoveryId, recoveryId);
        ContainerFileUploadValidation.Reference(original with { Context = current });
        try { _ = new System.Text.UTF8Encoding(false, true).GetByteCount(current.TraceId); }
        catch (System.Text.EncoderFallbackException) { throw new ArgumentException("CONTAINER_UPLOAD_CONTEXT_INVALID"); }
        var request = new ContainerUploadRecoveryRequest { Original = reference, RecoveryId = recoveryId.ToString("N"),
            ExpectedObservationHash = expectedObservationHash, Context = UploadContext(current) };
        var client = await ContainerRecoveryClient(cancellationToken);
        var response = await client.SealRecoveryAsync(request, ContainerWriteHeaders("SealRecovery", request),
            deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        return RecoveryObservation(original, reference, response, recoveryId);
    }
    private static ContainerUploadReference RecoveryReference(ContainerFileUploadReferenceDto original)
    {
        ContainerFileUploadValidation.Reference(original);
        return new() { ContainerId = original.ContainerId, InstanceId = original.InstanceId, OperationId = original.OperationId.ToString("N"),
            Owner = original.Owner, ExpectedTargetProof = original.ExpectedTargetProof, Context = UploadContext(original.Context) };
    }
    private static ContainerUploadRecoveryObservationDto RecoveryObservation(ContainerFileUploadReferenceDto original, ContainerUploadReference reference,
        ContainerUploadRecoveryObservation response, Guid? sealedBy)
    {
        Guid? seal = response.SealId.Length == 0 ? null : Guid.TryParseExact(response.SealId, "N", out var parsed) && parsed != Guid.Empty && parsed.ToString("N") == response.SealId ? parsed : throw RecoveryOutput();
        if (response.ContainerId != original.ContainerId || response.InstanceId != original.InstanceId || response.OperationId != reference.OperationId ||
            response.TargetProof != original.ExpectedTargetProof || !ContainerRecoveryStates.Contains(response.RootState) || response.BindingCurrent ||
            response.Sealed != seal.HasValue || seal == original.OperationId || sealedBy.HasValue && (!response.Sealed || seal != sealedBy) ||
            response.RetainedObjects != (response.RootState is not ("Missing" or "Prevented")) ||
            response.RootState is "Missing" or "Prevented" && response.MayHavePublished ||
            response.RootState is "Published" or "Unknown" && !response.MayHavePublished ||
            !FileManagementService.Hex(response.OriginalRecordHash) || !FileManagementService.Hex(response.ObservationHash) ||
            ContainerUploadRecoveryBinding.ObservationHash(reference, response) != response.ObservationHash) throw RecoveryOutput();
        return new(response.ContainerId, response.InstanceId, original.OperationId, response.TargetProof, response.RootState, response.OriginalRecordHash,
            response.Sealed, seal, response.MayHavePublished, response.RetainedObjects, response.ObservationHash);
    }
    private static InvalidOperationException RecoveryOutput() => new("CONTAINER_UPLOAD_RECOVERY_OUTPUT_INVALID");
}
