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

public sealed partial class AgentGateway : IWorkspaceLifecycleGateway
{
    public Task<WorkspaceLifecycleHostProof> PlanAsync(TerminalActor actor, WorkspaceDefinition workspace, string action, CancellationToken token) =>
        LifecycleAsync(actor, workspace, action, null, Guid.Empty, token);
    public Task<WorkspaceLifecycleHostProof> ApplyAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceLifecyclePlan plan, Guid operationId, CancellationToken token) =>
        LifecycleAsync(actor, workspace, plan.Action, plan, operationId, token);

    private async Task<WorkspaceLifecycleHostProof> LifecycleAsync(TerminalActor actor, WorkspaceDefinition workspace, string action,
        WorkspaceLifecyclePlan? plan, Guid operationId, CancellationToken token)
    {
        var apply = plan is not null;
        if (workspace.State != WorkspaceStorageAllocation.Allocated || workspace.LifecycleVersion < 1 ||
            plan is not null && (plan.WorkspaceId != workspace.Id || plan.ExpectedVersion != workspace.LifecycleVersion))
            throw new ArgumentException("WORKSPACE_LIFECYCLE_BINDING_INVALID");
        var request = new WorkspaceLifecycleRequest
        {
            Storage = new()
            {
                WorkspaceId = workspace.Id.ToString("D"), StorageRootId = workspace.StorageRootId,
                EnvironmentSha256 = workspace.EnvironmentSha256, DiskMib = (uint)workspace.Budget.DiskMiB,
                ActorSubject = actor.Subject, TraceId = actor.TraceId
            },
            Action = action, ExpectedEpoch = (ulong)workspace.LifecycleVersion,
            OperationId = apply ? operationId.ToString("D") : "", PlanSha256 = plan?.HostPlanSha256 ?? "",
            StorageProofSha256 = plan?.StorageProofSha256 ?? ""
        };
        if (!DevelopmentLifecycleBinding.IsValid(request, apply)) throw new ArgumentException("WORKSPACE_LIFECYCLE_BINDING_INVALID");
        try
        {
            var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
            if (!DevelopmentLifecycleBinding.IsSupported(caps)) throw new WorkspaceLifecycleException(WorkspaceLifecycleFailure.Unsupported);
            if (_capabilityKey is not { Length: >= 32 }) throw new WorkspaceLifecycleException(WorkspaceLifecycleFailure.Rejected);
            var method = apply ? DevelopmentLifecycleBinding.Apply : DevelopmentLifecycleBinding.Plan;
            var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(
                DevelopmentLifecycleBinding.Payload(expires, nonce, method, DevelopmentLifecycleBinding.Hash(request))));
            var headers = new Metadata { { "x-whaledeck-capability", $"{expires}.{nonce}.{Convert.ToHexString(signature)}" } };
            var client = new DevelopmentLifecycleService.DevelopmentLifecycleServiceClient(_channel);
            var deadline = DateTime.UtcNow.AddSeconds(28);
            var receipt = apply ? await client.ApplyAsync(request, headers, deadline, token) : await client.PlanAsync(request, headers, deadline, token);
            if (!DevelopmentLifecycleBinding.Matches(request, receipt, apply)) throw new WorkspaceLifecycleException(WorkspaceLifecycleFailure.Integrity);
            return new(workspace.Id, action, (long)receipt.Epoch, receipt.State, receipt.PlanSha256, receipt.StorageProofSha256);
        }
        catch (RpcException error)
        {
            throw new WorkspaceLifecycleException(error.StatusCode switch
            {
                StatusCode.Unimplemented => WorkspaceLifecycleFailure.Unsupported,
                StatusCode.DataLoss => WorkspaceLifecycleFailure.Integrity,
                StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.InvalidArgument or StatusCode.PermissionDenied => WorkspaceLifecycleFailure.Rejected,
                _ => WorkspaceLifecycleFailure.Retryable
            });
        }
    }
}
