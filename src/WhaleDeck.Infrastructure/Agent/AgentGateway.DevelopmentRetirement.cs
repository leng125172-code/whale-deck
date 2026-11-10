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

public sealed partial class AgentGateway : IWorkspaceRetirementGateway
{
    public Task<WorkspaceRetirementTaskProof> StartRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token) =>
        RetirementTaskAsync(actor, workspace, task, DevelopmentRetirementBinding.Start, token);
    public Task<WorkspaceRetirementTaskProof> ObserveRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token) =>
        RetirementTaskAsync(actor, workspace, task, DevelopmentRetirementBinding.Observe, token);
    public Task<WorkspaceRetirementTaskProof> CancelRetirementAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRetirementTask task, CancellationToken token) =>
        RetirementTaskAsync(actor, workspace, task, DevelopmentRetirementBinding.Cancel, token);

    private async Task<WorkspaceRetirementTaskProof> RetirementTaskAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRetirementTask task, string method, CancellationToken token)
    {
        if (workspace.State != WorkspaceStorageAllocation.Allocated || workspace.LifecycleState != WorkspaceLifecycle.Stopped ||
            workspace.LifecycleVersion != task.ExpectedEpoch || task.ExpectedEpoch < 2)
            throw new ArgumentException("WORKSPACE_RETIREMENT_BINDING_INVALID");
        var request = BuildRetirementRequest(actor, workspace, task);
        if (!DevelopmentRetirementBinding.IsValid(request)) throw new ArgumentException("WORKSPACE_RETIREMENT_BINDING_INVALID");
        var retirementRpcInvoked = false;
        try
        {
            var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
            if (!DevelopmentRetirementBinding.IsSupported(caps)) throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Unsupported, method == DevelopmentRetirementBinding.Start);
            if (_capabilityKey is not { Length: >= 32 }) throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Unconfirmed, method == DevelopmentRetirementBinding.Start);
            var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(
                DevelopmentRetirementBinding.Payload(expires, nonce, method, DevelopmentRetirementBinding.Hash(request))));
            var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
            var client = new DevelopmentRetirementService.DevelopmentRetirementServiceClient(_channel);
            var deadline = DateTime.UtcNow.AddSeconds(25);
            retirementRpcInvoked = true;
            var receipt = method switch
            {
                DevelopmentRetirementBinding.Start => await client.StartAsync(request, headers, deadline, token),
                DevelopmentRetirementBinding.Observe => await client.ObserveAsync(request, headers, deadline, token),
                DevelopmentRetirementBinding.Cancel => await client.CancelAsync(request, headers, deadline, token),
                _ => throw new InvalidOperationException()
            };
            if (!DevelopmentRetirementBinding.Matches(request, receipt)) throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Integrity);
            return new(task.TaskId, receipt.RequestSha256, receipt.Status, receipt.Code.Length == 0 ? null : receipt.Code,
                receipt.ResultJson.IsEmpty ? null : receipt.ResultJson.ToStringUtf8());
        }
        catch (RpcException error)
        {
            // A lost Start/Cancel response never establishes a host terminal
            // state. Callers retain the original task and observe it later.
            throw new WorkspaceRetirementException(error.StatusCode switch
            {
                StatusCode.Unimplemented => WorkspaceRetirementFailure.Unsupported,
                StatusCode.DataLoss => WorkspaceRetirementFailure.Integrity,
                StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.InvalidArgument or StatusCode.PermissionDenied => WorkspaceRetirementFailure.Unconfirmed,
                _ => WorkspaceRetirementFailure.Retryable
            }, method == DevelopmentRetirementBinding.Start && !retirementRpcInvoked);
        }
        catch (OperationCanceledException) when (method == DevelopmentRetirementBinding.Start && !retirementRpcInvoked)
        {
            throw new WorkspaceRetirementException(WorkspaceRetirementFailure.Retryable, startWasNotDispatched: true);
        }
    }

    internal static WorkspaceRetirementRequest BuildRetirementRequest(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRetirementTask task) => new()
        {
            Storage = new()
            {
                WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
                EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
                ActorSubject = actor.Subject, TraceId = actor.TraceId
            },
            TaskId = task.TaskId.ToString("D"), RetirementId = task.RetirementId.ToString("D"), ArchiveId = task.ArchiveId.ToString("D"), Phase = task.Phase,
            ExpectedEpoch = (ulong)task.ExpectedEpoch, ArchivePlanSha256 = task.ArchivePlanSha256, CaptureReceiptSha256 = task.CaptureReceiptSha256, RetirementPlanSha256 = task.RetirementPlanSha256
        };
}
