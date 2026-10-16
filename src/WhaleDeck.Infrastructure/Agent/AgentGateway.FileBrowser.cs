using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileBrowserGateway
{
    private async Task<Contracts.Agent.V1.FileTransferService.FileTransferServiceClient> Browser(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || !capabilities.Capabilities.Contains("files.bounded-browser")) throw new RpcException(new(StatusCode.Unimplemented, "FILES_BROWSER_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<FilePageDto> DirectoriesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        var client = await Browser(cancellationToken);
        var result = await client.DirectoriesAsync(new() { Location = new() { RootId = location.RootId, Path = location.Path }, After = after, Take = checked((uint)take) },
            Headers(FilesMethod + "Directories"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return new(result.Entries.Select(MapFile).ToArray(), result.NextCursor, result.Version);
    }
    public async Task<FileSearchPage> SearchAsync(FileSearchCommand command, CancellationToken cancellationToken)
    {
        var client = await Browser(cancellationToken);
        var result = await client.SearchAsync(new() { Location = new() { RootId = command.RootId, Path = command.Path }, Query = command.Query, Kind = command.Kind,
            MatchCase = command.MatchCase, Cursor = command.Cursor, Take = checked((uint)command.Take) }, Headers(FilesMethod + "Search"), deadline: DateTime.UtcNow.AddSeconds(15), cancellationToken: cancellationToken);
        return new(result.Entries.Select(item => new Application.Models.FileSearchHit(item.Path, MapFile(item.Entry))).ToArray(), result.NextCursor,
            checked((int)result.ScannedEntries), checked((int)result.ExcludedEntries), result.Complete, result.StopReason, result.DepthLimited);
    }
}
