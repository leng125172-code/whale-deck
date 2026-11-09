using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IWorkspaceStorageGateway
{
    public async Task AllocateAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token)
    {
        if (workspace.State != WorkspaceStorageAllocation.Allocating || actor.Subject != workspace.OwnerSubject ||
            workspace.Budget.DiskMiB is < 256 or > WorkspaceRegistrationPolicy.MaximumWorkspaceDiskMiB)
            throw new ArgumentException("WORKSPACE_STORAGE_BINDING_INVALID");
        var request = new WorkspaceAllocationRequest
        {
            WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
            EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
            ActorSubject = actor.Subject, TraceId = actor.TraceId
        };
        if (!DevelopmentStorageBinding.IsValid(request)) throw new ArgumentException("WORKSPACE_STORAGE_BINDING_INVALID");

        var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        if (!DevelopmentStorageBinding.IsSupported(caps))
            throw new RpcException(new Status(StatusCode.Unimplemented, "WORKSPACE_STORAGE_CAPABILITY_UNAVAILABLE"));
        if (_capabilityKey is not { Length: >= 32 })
            throw new RpcException(new Status(StatusCode.PermissionDenied, "WORKSPACE_STORAGE_CAPABILITY_UNAVAILABLE"));

        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var payload = DevelopmentStorageBinding.Payload(expires, nonce, DevelopmentStorageBinding.Method, DevelopmentStorageBinding.Hash(request));
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(payload));
        var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
        var client = new DevelopmentStorageService.DevelopmentStorageServiceClient(_channel);
        var receipt = await client.AllocateAsync(request, headers, deadline: DateTime.UtcNow.AddSeconds(28), cancellationToken: token);
        if (!DevelopmentStorageBinding.Matches(request, receipt))
            throw new RpcException(new Status(StatusCode.DataLoss, "WORKSPACE_STORAGE_RECEIPT_INVALID"));
    }
}
