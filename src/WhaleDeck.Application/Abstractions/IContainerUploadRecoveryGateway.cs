using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Current administrator authority and permanent recovery intent are required by the caller.</summary>
public interface IContainerUploadRecoveryGateway
{
    Task<ContainerUploadRecoveryObservationDto> ObserveContainerUploadRecoveryAsync(ContainerFileUploadReferenceDto original, CancellationToken cancellationToken);
    Task<ContainerUploadRecoveryObservationDto> SealContainerUploadRecoveryAsync(ContainerFileUploadReferenceDto original, Guid recoveryId,
        TerminalActor currentActor, string expectedObservationHash, CancellationToken cancellationToken);
}
