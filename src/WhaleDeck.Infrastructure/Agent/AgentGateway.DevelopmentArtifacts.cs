using System.Security.Cryptography;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway
{
    public async Task<byte[]> ReadTestArtifactAsync(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command, WorkspaceRunnerReceipt receipt, CancellationToken token)
    {
        var request = ArtifactRequest(actor, workspace, command, receipt);
        try
        {
            await RequireArtifactCapabilityAsync(request.Run, token);
            var headers = SignWorkspaceRun(DevelopmentRunnerBinding.ReadTestArtifact, DevelopmentRunnerBinding.Hash(request));
            var client = new DevelopmentRunnerService.DevelopmentRunnerServiceClient(_channel);
            using var call = client.ReadTestArtifact(request, headers, DateTime.UtcNow.AddSeconds(25), token);
            var content = new byte[request.Size];
            var offset = 0;
            while (await call.ResponseStream.MoveNext(token))
            {
                var chunk = call.ResponseStream.Current.Content;
                if (chunk.Length is 0 or > DevelopmentRunnerBinding.ArtifactChunkBytes || chunk.Length > content.Length - offset)
                    throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Integrity);
                chunk.Span.CopyTo(content.AsSpan(offset));
                offset += chunk.Length;
            }
            if (offset != content.Length || Convert.ToHexStringLower(SHA256.HashData(content)) != request.ArtifactSha256)
                throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Integrity);
            return content;
        }
        catch (RpcException error)
        {
            token.ThrowIfCancellationRequested();
            throw ArtifactError(error);
        }
    }

    public async Task RetireTestArtifactAsync(TerminalActor actor, WorkspaceDefinition workspace, WorkspaceRunCommand command,
        WorkspaceRunnerReceipt receipt, CancellationToken token)
    {
        var request = ArtifactRequest(actor, workspace, command, receipt);
        try
        {
            await RequireArtifactCapabilityAsync(request.Run, token);
            var headers = SignWorkspaceRun(DevelopmentRunnerBinding.RetireTestArtifact, DevelopmentRunnerBinding.Hash(request));
            var client = new DevelopmentRunnerService.DevelopmentRunnerServiceClient(_channel);
            var retired = await client.RetireTestArtifactAsync(request, headers, DateTime.UtcNow.AddSeconds(25), token);
            if (!DevelopmentRunnerBinding.Matches(request, retired))
                throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Integrity);
        }
        catch (RpcException error)
        {
            token.ThrowIfCancellationRequested();
            throw ArtifactError(error);
        }
    }

    private static WorkspaceTestArtifactRequest ArtifactRequest(TerminalActor actor, WorkspaceDefinition workspace,
        WorkspaceRunCommand command, WorkspaceRunnerReceipt receipt)
    {
        var run = CreateWorkspaceRunRequest(actor, workspace, command);
        if (receipt.RunId != command.RunId || receipt.Status is not ("Succeeded" or "Failed") ||
            receipt.TestReport is not { State: "Present", Size: > 0 } manifest ||
            DevelopmentRunnerBinding.ReportFormat(run) != manifest.Format.ToString())
            throw new ArgumentException("WORKSPACE_RUN_REPORT_BINDING_INVALID");
        var request = new WorkspaceTestArtifactRequest
        {
            Run = run, RequestSha256 = receipt.RequestSha256, ArtifactSha256 = manifest.Sha256, Size = (uint)manifest.Size
        };
        if (!DevelopmentRunnerBinding.IsValid(request)) throw new ArgumentException("WORKSPACE_RUN_REPORT_BINDING_INVALID");
        return request;
    }

    private async Task RequireArtifactCapabilityAsync(WorkspaceRunRequest run, CancellationToken token)
    {
        var caps = await _agent.GetCapabilitiesAsync(new Empty(), deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: token);
        if (DevelopmentRunnerBinding.RequiresRestoreSupport(run) && !DevelopmentRunnerBinding.SupportsRestore(caps))
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.RestoreRequired);
        if (!DevelopmentRunnerBinding.IsSupported(caps) || caps.ProtocolMinor < DevelopmentRunnerBinding.ArtifactTransferProtocolMinor ||
            !caps.Capabilities.Contains(DevelopmentRunnerBinding.ArtifactTransferCapability))
            throw new WorkspaceRunnerException(WorkspaceRunnerFailure.Unsupported);
    }

    private static WorkspaceRunnerException ArtifactError(RpcException error) => new(error.StatusCode switch
    {
        StatusCode.NotFound => WorkspaceRunnerFailure.NotFound,
        StatusCode.Unimplemented => WorkspaceRunnerFailure.Unsupported,
        StatusCode.Aborted or StatusCode.DataLoss => WorkspaceRunnerFailure.Integrity,
        _ => WorkspaceRunnerFailure.Retryable
    });
}
