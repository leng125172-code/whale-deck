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
    public Task AllocateAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token) =>
        WorkspaceStorageAsync(actor, workspace, attest: false, token);

    public Task AttestAsync(TerminalActor actor, WorkspaceDefinition workspace, CancellationToken token) =>
        WorkspaceStorageAsync(actor, workspace, attest: true, token);

    private async Task WorkspaceStorageAsync(TerminalActor actor, WorkspaceDefinition workspace, bool attest, CancellationToken token)
    {
        var requiredState = attest ? WorkspaceStorageAllocation.Allocated : WorkspaceStorageAllocation.Allocating;
        if (workspace.State != requiredState || actor.Subject != workspace.OwnerSubject ||
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
        if (!(attest ? DevelopmentStorageBinding.SupportsAttestation(caps) : DevelopmentStorageBinding.IsSupported(caps)))
            throw new RpcException(new Status(StatusCode.Unimplemented, "WORKSPACE_STORAGE_CAPABILITY_UNAVAILABLE"));
        if (_capabilityKey is not { Length: >= 32 })
            throw new RpcException(new Status(StatusCode.PermissionDenied, "WORKSPACE_STORAGE_CAPABILITY_UNAVAILABLE"));

        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var method = attest ? DevelopmentStorageBinding.AttestationMethod : DevelopmentStorageBinding.Method;
        var payload = DevelopmentStorageBinding.Payload(expires, nonce, method, DevelopmentStorageBinding.Hash(request));
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(payload));
        var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
        var client = new DevelopmentStorageService.DevelopmentStorageServiceClient(_channel);
        var deadline = DateTime.UtcNow.AddSeconds(28);
        var receipt = attest
            ? await client.AttestAsync(request, headers, deadline, token)
            : await client.AllocateAsync(request, headers, deadline, token);
        if (!DevelopmentStorageBinding.Matches(request, receipt))
            throw new RpcException(new Status(StatusCode.DataLoss, "WORKSPACE_STORAGE_RECEIPT_INVALID"));
    }
}
