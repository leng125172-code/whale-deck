using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Trusted transport; callers must validate permanent intent and current authority before signing.</summary>
public interface IContainerFileWriteGateway
{
    Task<ContainerFileUploadTargetDto> InspectContainerUploadTargetAsync(ContainerFileLocationDto location, CancellationToken cancellationToken);
    Task<ContainerFileUploadReceiptDto> StageContainerFileAsync(ContainerFileStageDto request, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken);
    Task<ContainerFileUploadReceiptDto> PublishContainerFileAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken);
    Task<ContainerFileUploadReceiptDto?> InspectContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken);
    Task<ContainerFileUploadReceiptDto> DiscardContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken);
    /// <summary>Requires the durable pre-receive cancellation capability, not just the original write transport.</summary>
    Task<ContainerFileUploadReceiptDto> PreventContainerUploadAsync(ContainerFileUploadReferenceDto request, CancellationToken cancellationToken);
}
