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

public sealed partial class AgentGateway : IWorkspaceRunnerGateway
{
    public Task<WorkspaceRunnerReceipt> StartAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, observe: false, token);

    public Task<WorkspaceRunnerReceipt> ObserveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, observe: true, token);

    private async Task<WorkspaceRunnerReceipt> WorkspaceRunAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command, bool observe, CancellationToken token)
    {
        if (workspace.State != WorkspaceStorageAllocation.Allocated || actor.Subject != workspace.OwnerSubject ||
            workspace.Budget.DiskMiB is < 256 or > WorkspaceRegistrationPolicy.MaximumWorkspaceDiskMiB ||
            command.Arguments is not { Length: > 0 and <= 64 } || command.Arguments.Any(arg => arg is null) ||
            command.CpuMillis < 100 || command.CpuMillis > workspace.Budget.CpuMillis ||
            command.MemoryMiB < 128 || command.MemoryMiB > workspace.Budget.MemoryMiB ||
            command.DeadlineSeconds is < 1 or > 3600 || !workspace.Environment.Runtimes.Any(pin => pin.Key == command.Runtime))
            throw new ArgumentException("WORKSPACE_RUN_BINDING_INVALID");

        var request = new WorkspaceRunRequest
        {
            Storage = new()
            {
                WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
                EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
                ActorSubject = actor.Subject, TraceId = actor.TraceId
            },
            RunId = command.RunId.ToString("N"), EnvironmentJson = DevelopmentWorkspaces.EncodeEnvironment(workspace.Environment),
            Runtime = command.Runtime, CpuMillis = (uint)command.CpuMillis, MemoryMib = (uint)command.MemoryMiB,
            DeadlineSeconds = (uint)command.DeadlineSeconds
        };
        request.Argv.AddRange(command.Arguments);
        if (!DevelopmentRunnerBinding.IsValid(request)) throw new ArgumentException("WORKSPACE_RUN_BINDING_INVALID");
        var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        if (!DevelopmentRunnerBinding.IsSupported(caps))
            throw new RpcException(new Status(StatusCode.Unimplemented, "WORKSPACE_RUN_CAPABILITY_UNAVAILABLE"));
        if (_capabilityKey is not { Length: >= 32 })
            throw new RpcException(new Status(StatusCode.PermissionDenied, "WORKSPACE_RUN_CAPABILITY_UNAVAILABLE"));

        var method = observe ? DevelopmentRunnerBinding.Observe : DevelopmentRunnerBinding.Start;
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var payload = DevelopmentRunnerBinding.Payload(expires, nonce, method, DevelopmentRunnerBinding.Hash(request));
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(payload));
        var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
        var client = new DevelopmentRunnerService.DevelopmentRunnerServiceClient(_channel);
        var deadline = DateTime.UtcNow.AddSeconds(5 + (observe ? DevelopmentRunnerBinding.ObserveSeconds :
            DevelopmentRunnerBinding.PreparationSeconds + command.DeadlineSeconds));
        var receipt = observe ? await client.ObserveAsync(request, headers, deadline, token) :
            await client.StartAsync(request, headers, deadline, token);
        if (!DevelopmentRunnerBinding.Matches(request, receipt))
            throw new RpcException(new Status(StatusCode.DataLoss, "WORKSPACE_RUN_RECEIPT_INVALID"));
        return new(command.RunId, receipt.RequestSha256, receipt.Status, receipt.HasRuntimeExitCode ? receipt.RuntimeExitCode : null);
    }
}
