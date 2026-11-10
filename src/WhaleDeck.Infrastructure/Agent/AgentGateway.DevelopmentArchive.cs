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

public sealed partial class AgentGateway : IWorkspaceArchiveGateway
{
    public Task<WorkspaceArchiveTaskProof> StartArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token) =>
        ArchiveTaskAsync(actor, workspace, task, DevelopmentArchiveBinding.Start, token);
    public Task<WorkspaceArchiveTaskProof> ObserveArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token) =>
        ArchiveTaskAsync(actor, workspace, task, DevelopmentArchiveBinding.Observe, token);
    public Task<WorkspaceArchiveTaskProof> CancelArchiveAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceArchiveTask task, CancellationToken token) =>
        ArchiveTaskAsync(actor, workspace, task, DevelopmentArchiveBinding.Cancel, token);

    private async Task<WorkspaceArchiveTaskProof> ArchiveTaskAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceArchiveTask task, string method, CancellationToken token)
    {
        if (workspace.State != WorkspaceStorageAllocation.Allocated || workspace.LifecycleState != WorkspaceLifecycle.Stopped ||
            workspace.LifecycleVersion != task.ExpectedEpoch || task.ExpectedEpoch < 2)
            throw new ArgumentException("WORKSPACE_ARCHIVE_BINDING_INVALID");
        var request = new WorkspaceArchiveRequest
        {
            Storage = new()
            {
                WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
                EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
                ActorSubject = actor.Subject, TraceId = actor.TraceId
            },
            TaskId = task.TaskId.ToString("D"), ArchiveId = task.ArchiveId.ToString("D"), Phase = task.Phase,
            ExpectedEpoch = (ulong)task.ExpectedEpoch, PlanSha256 = task.PlanSha256, ReceiptSha256 = task.ReceiptSha256
        };
        if (!DevelopmentArchiveBinding.IsValid(request)) throw new ArgumentException("WORKSPACE_ARCHIVE_BINDING_INVALID");
        try
        {
            var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
            if (!DevelopmentArchiveBinding.IsSupported(caps)) throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Unsupported);
            if (_capabilityKey is not { Length: >= 32 }) throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Unconfirmed);
            var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(
                DevelopmentArchiveBinding.Payload(expires, nonce, method, DevelopmentArchiveBinding.Hash(request))));
            var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
            var client = new DevelopmentArchiveService.DevelopmentArchiveServiceClient(_channel);
            var deadline = DateTime.UtcNow.AddSeconds(25);
            var receipt = method switch
            {
                DevelopmentArchiveBinding.Start => await client.StartAsync(request, headers, deadline, token),
                DevelopmentArchiveBinding.Observe => await client.ObserveAsync(request, headers, deadline, token),
                DevelopmentArchiveBinding.Cancel => await client.CancelAsync(request, headers, deadline, token),
                _ => throw new InvalidOperationException()
            };
            if (!DevelopmentArchiveBinding.Matches(request, receipt)) throw new WorkspaceArchiveException(WorkspaceArchiveFailure.Integrity);
            return new(task.TaskId, receipt.RequestSha256, receipt.Status, receipt.Code.Length == 0 ? null : receipt.Code,
                receipt.ResultJson.IsEmpty ? null : receipt.ResultJson.ToStringUtf8());
        }
        catch (RpcException error)
        {
            // A lost Start/Cancel response never establishes a host terminal
            // state. Callers retain the original task and observe it later.
            throw new WorkspaceArchiveException(error.StatusCode switch
            {
                StatusCode.Unimplemented => WorkspaceArchiveFailure.Unsupported,
                StatusCode.DataLoss => WorkspaceArchiveFailure.Integrity,
                StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.InvalidArgument or StatusCode.PermissionDenied => WorkspaceArchiveFailure.Unconfirmed,
                _ => WorkspaceArchiveFailure.Retryable
            });
        }
    }
}
