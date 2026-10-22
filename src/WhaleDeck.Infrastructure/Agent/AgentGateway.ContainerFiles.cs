using System.Runtime.CompilerServices;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerFileGateway
{
    private const string ContainerFilesMethod = "/whaledeck.agent.v1.ContainerFileService/";
    private async Task<ContainerFileService.ContainerFileServiceClient> ContainerFilesClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 38 || !capabilities.Capabilities.Contains("docker.files.read"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_FILES_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }

    public async Task<ContainerFileTargetDto> InspectContainerFileTargetAsync(string containerId, CancellationToken cancellationToken)
    {
        var client = await ContainerFilesClient(cancellationToken);
        var target = await client.InspectTargetAsync(new() { ResourceType = "Container", ResourceId = containerId },
            Headers(ContainerFilesMethod + "InspectTarget"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return new(target.ContainerId, target.Name, target.InstanceId, target.ImageId, target.ConfiguredUser);
    }

    public async Task<FilePageDto> ListContainerFilesAsync(ContainerFileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        var client = await ContainerFilesClient(cancellationToken);
        var response = await client.ListAsync(new() { Location = ContainerLocation(location), After = after, Take = checked((uint)take) },
            Headers(ContainerFilesMethod + "List"), deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        return new(response.Entries.Select(MapFile).ToArray(), response.NextCursor, response.Version);
    }

    public async Task<FileEntryDto?> InspectContainerFileAsync(ContainerFileLocationDto location, CancellationToken cancellationToken)
    {
        var client = await ContainerFilesClient(cancellationToken);
        try
        {
            return MapFile(await client.InspectAsync(ContainerLocation(location), Headers(ContainerFilesMethod + "Inspect"),
                deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken));
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }

    public async IAsyncEnumerable<FileDownloadPart> DownloadContainerFileAsync(ContainerFileLocationDto location, string expectedVersion,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await ContainerFilesClient(cancellationToken);
        using var call = client.Download(new() { Location = ContainerLocation(location), ExpectedVersion = expectedVersion },
            Headers(ContainerFilesMethod + "Download"), deadline: DateTime.UtcNow.AddSeconds(310), cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var frame = call.ResponseStream.Current;
            yield return new(frame.Kind, frame.Metadata is null ? null : MapFile(frame.Metadata), frame.Data.Memory,
                frame.Offset, frame.Sha256, frame.Version, frame.SizeBytes);
        }
    }

    private static ContainerFileLocation ContainerLocation(ContainerFileLocationDto location) =>
        new() { ContainerId = location.ContainerId, InstanceId = location.InstanceId, Path = location.Path };
}
