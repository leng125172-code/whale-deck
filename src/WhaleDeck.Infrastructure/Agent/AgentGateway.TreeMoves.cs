using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using MoveRpc = WhaleDeck.Contracts.Agent.V1.FileTreeMoveService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeMoveGateway
{
    private const string MoveTreeMethod = "/whaledeck.agent.v1.FileTreeMoveService/";
    private async Task<MoveRpc.FileTreeMoveServiceClient> MoveTreeClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 28 || !capabilities.Capabilities.Contains("files.tree-move-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeMovePlan> PlanTreeMoveAsync(App.TerminalActor actor, App.FileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        var client = await MoveTreeClient(cancellationToken); FileTreeMoveValidation.Command(command);
        try
        {
            var value = await client.PlanAsync(new() { Command = TreeCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(MoveTreeMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            var copy = value.CopyPlan;
            if (copy?.Command?.Source is null || copy.Command.Target is null || copy.TargetParent is null || value.SourceParent is null) throw new App.FileTransferRejectedException();
            var plan = new App.FileTreeMovePlan(new(new(new(copy.Command.Source.RootId, copy.Command.Source.Path), copy.Command.ExpectedVersion,
                new(copy.Command.Target.RootId, copy.Command.Target.Path)), copy.ManifestSha256, checked((int)copy.EntryCount), copy.TotalBytes,
                new(copy.TargetParent.Device, copy.TargetParent.Inode), copy.SourcePolicyHash, copy.TargetPolicyHash, copy.Fingerprint),
                new(value.SourceParent.Device, value.SourceParent.Inode), value.Fingerprint);
            FileTreeMoveValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (MoveTreeRejection(error) is { }) { throw MoveTreeRejection(error)!; }
    }
    public async Task<App.FileTreeMoveReceipt?> InspectTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken)
    {
        var client = await MoveTreeClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = MoveTreeExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(MoveTreeMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return MoveTreeReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeMoveReceipt> ApplyTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken) => MoveTreeAction(operation, false, cancellationToken);
    public Task<App.FileTreeMoveReceipt> DiscardTreeMoveAsync(FileTreeMoveOperation operation, CancellationToken cancellationToken) => MoveTreeAction(operation, true, cancellationToken);
    private async Task<App.FileTreeMoveReceipt> MoveTreeAction(FileTreeMoveOperation operation, bool discard, CancellationToken cancellationToken)
    {
        var client = await MoveTreeClient(cancellationToken); var plan = FileTreeMoveValidation.ReadPlan(operation); FileTreeMoveValidation.Plan(plan); var copy = plan.CopyPlan;
        var request = new FileTreeMoveApplyRequest { OperationId = MoveTreeExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = operation.ActorSubject, TraceId = operation.Id.ToString("D") }, Plan = new() {
                CopyPlan = new() { Command = TreeCommand(copy.Command), ManifestSha256 = copy.ManifestSha256, EntryCount = (uint)copy.EntryCount, TotalBytes = copy.TotalBytes,
                    TargetParent = new() { Device = copy.TargetParent.Device, Inode = copy.TargetParent.Inode }, SourcePolicyHash = copy.SourcePolicyHash,
                    TargetPolicyHash = copy.TargetPolicyHash, Fingerprint = copy.Fingerprint },
                SourceParent = new() { Device = plan.SourceParent.Device, Inode = plan.SourceParent.Inode }, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(MoveTreeMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(MoveTreeMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return MoveTreeReceipt(operation, value);
    }
    private static Guid MoveTreeExecution(FileTreeMoveOperation row) => row.ExecutionJobId is { } id && id == FileTreeMoveValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static App.FileTreeMoveReceipt MoveTreeReceipt(FileTreeMoveOperation row, FileTreeMoveReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreeMoveReceipt(id, value.State, value.Fingerprint, checked((int)value.EntryCount), value.TotalBytes, value.Version,
            value.DestinationPublished, value.SourceRemoved, value.SourceRetained, value.PartialRetained);
        FileTreeMoveValidation.Receipt(MoveTreeExecution(row), FileTreeMoveValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreeMoveRejectedException? MoveTreeRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
