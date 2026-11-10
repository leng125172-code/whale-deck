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

public sealed partial class AgentGateway : IWorkspaceRunnerGateway, IWorkspaceTestArtifactGateway
{
    public Task<WorkspaceRunnerReceipt> StartAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, DevelopmentRunnerBinding.Start, token);

    public Task<WorkspaceRunnerReceipt> ObserveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, DevelopmentRunnerBinding.Observe, token);

    public Task<WorkspaceRunnerReceipt> CancelAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, DevelopmentRunnerBinding.Cancel, token);

    public Task<WorkspaceRunnerReceipt> ReclaimAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        CancellationToken token) => WorkspaceRunAsync(actor, workspace, command, DevelopmentRunnerBinding.Reclaim, token);

    private async Task<WorkspaceRunnerReceipt> WorkspaceRunAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command, string method, CancellationToken token)
    {
        try { return await WorkspaceRunCoreAsync(actor, workspace, command, method, token); }
        catch (RpcException error) when (command.RequireControl)
        {
            throw new WorkspaceRunnerException(error.StatusCode switch
            {
                StatusCode.NotFound => WorkspaceRunnerFailure.NotFound,
                StatusCode.Unimplemented => WorkspaceRunnerFailure.Unsupported,
                StatusCode.DataLoss when error.Status.Detail is "WORKSPACE_RUN_INTERRUPTED" or "WORKSPACE_RUN_CLEANUP_UNCONFIRMED" =>
                    WorkspaceRunnerFailure.Interrupted,
                _ => WorkspaceRunnerFailure.Retryable
            });
        }
    }

    private async Task<WorkspaceRunnerReceipt> WorkspaceRunCoreAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command, string method, CancellationToken token)
    {
        var request = CreateWorkspaceRunRequest(actor, workspace, command);
        var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        if (DevelopmentRunnerBinding.RequiresRestoreSupport(request) && !DevelopmentRunnerBinding.SupportsRestore(caps))
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.RestoreRequired);
        if (!DevelopmentRunnerBinding.IsSupported(caps, method) ||
            command.RequireControl && !DevelopmentRunnerBinding.IsSupported(caps, DevelopmentRunnerBinding.Reclaim) ||
            DevelopmentRunnerBinding.ReportFormat(request) is not null &&
            (caps.ProtocolMinor < DevelopmentRunnerBinding.ReportProtocolMinor ||
                !caps.Capabilities.Contains(DevelopmentRunnerBinding.ReportCapability) ||
                method == DevelopmentRunnerBinding.Start && command.ReportRetentionDays is not null &&
                (caps.ProtocolMinor < DevelopmentRunnerBinding.ArtifactTransferProtocolMinor ||
                    !caps.Capabilities.Contains(DevelopmentRunnerBinding.ArtifactTransferCapability))))
            throw new RpcException(new Status(StatusCode.Unimplemented, "WORKSPACE_RUN_CAPABILITY_UNAVAILABLE"));
        var headers = SignWorkspaceRun(method, DevelopmentRunnerBinding.Hash(request));
        var client = new DevelopmentRunnerService.DevelopmentRunnerServiceClient(_channel);
        var seconds = method switch
        {
            DevelopmentRunnerBinding.Start => DevelopmentRunnerBinding.PreparationSeconds + command.DeadlineSeconds,
            DevelopmentRunnerBinding.Observe => DevelopmentRunnerBinding.ObserveSeconds,
            _ => DevelopmentRunnerBinding.ControlSeconds
        };
        var deadline = DateTime.UtcNow.AddSeconds(5 + seconds);
        var receipt = await (method switch
        {
            DevelopmentRunnerBinding.Start => client.StartAsync(request, headers, deadline, token),
            DevelopmentRunnerBinding.Observe => client.ObserveAsync(request, headers, deadline, token),
            DevelopmentRunnerBinding.Cancel => client.CancelAsync(request, headers, deadline, token),
            DevelopmentRunnerBinding.Reclaim => client.ReclaimAsync(request, headers, deadline, token),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        });
        if (!DevelopmentRunnerBinding.Matches(request, receipt))
            throw new RpcException(new Status(StatusCode.DataLoss, "WORKSPACE_RUN_RECEIPT_INVALID"));
        var manifest = receipt.TestReport;
        return new(command.RunId, receipt.RequestSha256, receipt.Status, receipt.HasRuntimeExitCode ? receipt.RuntimeExitCode : null,
            manifest is null ? null : new(System.Enum.Parse<WorkspaceTestFormat>(manifest.Format), manifest.State,
                (int)manifest.Size, manifest.Sha256));
    }

    private static WorkspaceRunRequest CreateWorkspaceRunRequest(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command)
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
        return request;
    }

    private Metadata SignWorkspaceRun(string method, string hash)
    {
        if (_capabilityKey is not { Length: >= 32 })
            throw new RpcException(new Status(StatusCode.PermissionDenied, "WORKSPACE_RUN_CAPABILITY_UNAVAILABLE"));

        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var payload = DevelopmentRunnerBinding.Payload(expires, nonce, method, hash);
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(payload));
        return new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
    }
}
