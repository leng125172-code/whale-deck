using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerFileHistoryGateway
{
    public async Task RequireContainerHistoryWorkflowAsync(CancellationToken cancellationToken) => _ = await ContainerHistoryClient(cancellationToken);
    private async Task<ContainerHistoryService.ContainerHistoryServiceClient> ContainerHistoryClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 42 || !capabilities.Capabilities.Contains("docker.files.history") ||
            !capabilities.Capabilities.Contains("docker.files.history-source"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }

    private Metadata ContainerHistoryHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_HISTORY_CAPABILITY_UNAVAILABLE"));
        var method = ContainerHistoryBinding.Service + action; var hash = ContainerHistoryBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var mac = Convert.ToHexString(HMACSHA256.HashData(_capabilityKey, System.Text.Encoding.UTF8.GetBytes(ContainerHistoryBinding.Payload(expires, nonce, method, hash))));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + mac }, { ContainerHistoryBinding.Header, hash } };
    }

    public async Task<ContainerHistoryPageDto> ListContainerHistoryAsync(ContainerFileLocationDto location, string after, int take,
        string expectedVersion, CancellationToken cancellationToken)
    {
        ContainerFileHistoryValidation.List(location, after, take, expectedVersion);
        var client = await ContainerHistoryClient(cancellationToken);
        var request = new ContainerHistoryListRequest { Location = ContainerLocation(location), After = after, Take = (uint)take, ExpectedVersion = expectedVersion };
        var response = await client.ListAsync(request, ContainerHistoryHeaders("List", request), deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken);
        if (response.Location is null || response.Entries.Count > 50 || response.Scanned > 500 || response.InvalidRecords > response.Scanned)
            throw ContainerFileHistoryValidation.InvalidOutput();
        var page = new ContainerHistoryPageDto(new(response.Location.ContainerId, response.Location.InstanceId, response.Location.Path), response.TargetProof,
            response.Entries.Select(MapContainerHistory).ToArray(), response.Version, response.NextCursor, checked((int)response.Scanned), checked((int)response.InvalidRecords), response.Complete);
        ContainerFileHistoryValidation.Page(page, location, after, take, expectedVersion); return page;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadContainerHistorySourceAsync(ContainerFileLocationDto location, ContainerHistoryEntryDto expected,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ContainerFileHistoryValidation.Source(location, expected);
        var client = await ContainerHistoryClient(cancellationToken);
        var request = new ContainerHistorySourceRequest { Location = ContainerLocation(location), CaptureId = expected.CaptureId, ExpectedCaptureHash = expected.CaptureHash };
        using var call = client.ReadSource(request, ContainerHistoryHeaders("ReadSource", request), deadline: DateTime.UtcNow.AddSeconds(310), cancellationToken: cancellationToken);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var metadata = false; var ended = false; var proof = ""; long offset = 0;
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var frame = call.ResponseStream.Current;
            if (ended) throw ContainerFileHistoryValidation.InvalidOutput();
            if (frame.Kind == "metadata" && !metadata)
            {
                if (frame.Metadata is null || MapContainerHistory(frame.Metadata) != expected || frame.Data.Length != 0 || frame.Offset != 0 ||
                    frame.Sha256.Length != 0 || frame.SizeBytes != 0 || frame.CaptureHash != expected.CaptureHash) throw ContainerFileHistoryValidation.InvalidOutput();
                ContainerFileValidation.Hash(frame.TargetProof); proof = frame.TargetProof; metadata = true; continue;
            }
            if (frame.Kind == "chunk" && metadata)
            {
                if (frame.Metadata is not null || frame.Data.Length is < 1 or > 32768 || frame.Offset != offset || offset + frame.Data.Length > expected.SizeBytes ||
                    frame.Sha256.Length != 0 || frame.SizeBytes != 0 || frame.TargetProof.Length != 0 || frame.CaptureHash.Length != 0)
                    throw ContainerFileHistoryValidation.InvalidOutput();
                digest.AppendData(frame.Data.Span); offset += frame.Data.Length;
                yield return frame.Data.Memory; continue;
            }
            if (frame.Kind == "end" && metadata)
            {
                if (frame.Metadata is not null || frame.Data.Length != 0 || frame.Offset != 0 || offset != expected.SizeBytes || frame.SizeBytes != offset ||
                    frame.TargetProof != proof || frame.CaptureHash != expected.CaptureHash || frame.Sha256 != expected.Sha256 ||
                    frame.Sha256 != Convert.ToHexString(digest.GetHashAndReset())) throw ContainerFileHistoryValidation.InvalidOutput();
                ended = true; continue;
            }
            throw ContainerFileHistoryValidation.InvalidOutput();
        }
        if (!ended) throw ContainerFileHistoryValidation.InvalidOutput();
    }

    private static ContainerHistoryEntryDto MapContainerHistory(ContainerHistoryEntry value)
    {
        var result = new ContainerHistoryEntryDto(value.CaptureId, value.ContainerId, value.OriginalInstanceId, value.Path, value.SourceVersion,
            value.SizeBytes, value.Sha256, value.CreatedAtUnixMs, value.DifferentSourceDevice, value.Availability, value.CaptureHash);
        ContainerFileHistoryValidation.Entry(result); return result;
    }
}
