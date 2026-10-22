using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

/// <summary>Container reads are instance-bound, not aliases for registered host file roots.</summary>
public interface IContainerFileGateway
{
    Task<ContainerFileTargetDto> InspectContainerFileTargetAsync(string containerId, CancellationToken cancellationToken);
    Task<FilePageDto> ListContainerFilesAsync(ContainerFileLocationDto location, string after, int take, CancellationToken cancellationToken);
    Task<FileEntryDto?> InspectContainerFileAsync(ContainerFileLocationDto location, CancellationToken cancellationToken);
    IAsyncEnumerable<FileDownloadPart> DownloadContainerFileAsync(ContainerFileLocationDto location, string expectedVersion, CancellationToken cancellationToken);
}

public interface IContainerFileReadAudit
{
    Task RecordAsync(TerminalActor actor, ContainerFileLocationDto location, string action, string result,
        string? version, long? sizeBytes, string? sha256, CancellationToken cancellationToken);
}
