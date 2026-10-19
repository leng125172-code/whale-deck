using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using TreeRpc = WhaleDeck.Contracts.Agent.V1.FileTreeUnpackingService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeUnpackingGateway
{
    private const string UnpackingMethod = "/whaledeck.agent.v1.FileTreeUnpackingService/";
    private async Task<TreeRpc.FileTreeUnpackingServiceClient> UnpackingClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 31 || !capabilities.Capabilities.Contains("files.tree-unpack-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeUnpackingPlan> PlanTreeUnpackingAsync(App.TerminalActor actor, App.FileTreeUnpackingCommand command, CancellationToken cancellationToken)
    {
        var client = await UnpackingClient(cancellationToken); FileTreeUnpackingValidation.Command(command);
        try
        {
            var value = await client.PlanAsync(new() { Command = UnpackingCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(UnpackingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            if (value.Command?.Tree?.Source is null || value.Command.Tree.Target is null || value.TargetParent is null) throw new App.FileTransferRejectedException();
            var tree = value.Command.Tree;
            var plan = new App.FileTreeUnpackingPlan(new(new(tree.Source.RootId, tree.Source.Path), tree.ExpectedVersion,
                new(tree.Target.RootId, tree.Target.Path), value.Command.Format), value.ArchiveSha256, value.ArchiveBytes, value.ContentSha256,
                checked((int)value.EntryCount), value.TotalBytes, new(value.TargetParent.Device, value.TargetParent.Inode), value.SourcePolicyHash, value.TargetPolicyHash, value.Fingerprint);
            FileTreeUnpackingValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (UnpackingRejection(error) is { }) { throw UnpackingRejection(error)!; }
    }
    public async Task<App.FileTreeUnpackingReceipt?> InspectTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken)
    {
        var client = await UnpackingClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = UnpackingExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(UnpackingMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return UnpackingReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeUnpackingReceipt> ApplyTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken) => UnpackingAction(operation, false, cancellationToken);
    public Task<App.FileTreeUnpackingReceipt> DiscardTreeUnpackingAsync(FileTreeUnpackingOperation operation, CancellationToken cancellationToken) => UnpackingAction(operation, true, cancellationToken);
    private async Task<App.FileTreeUnpackingReceipt> UnpackingAction(FileTreeUnpackingOperation operation, bool discard, CancellationToken cancellationToken)
    {
        var client = await UnpackingClient(cancellationToken); var plan = FileTreeUnpackingValidation.ReadPlan(operation); FileTreeUnpackingValidation.Plan(plan);
        var request = new FileTreeUnpackingApplyRequest { OperationId = UnpackingExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = operation.ActorSubject, TraceId = operation.Id.ToString("D") }, Plan = new() {
                Command = UnpackingCommand(plan.Command), ArchiveSha256 = plan.ArchiveSha256, ArchiveBytes = plan.ArchiveBytes, ContentSha256 = plan.ContentSha256,
                EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes, TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode },
                SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(UnpackingMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(UnpackingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return UnpackingReceipt(operation, value);
    }
    private static Guid UnpackingExecution(FileTreeUnpackingOperation row) => row.ExecutionJobId is { } id && id == FileTreeUnpackingValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static FileTreeUnpackingCommand UnpackingCommand(App.FileTreeUnpackingCommand value) => new() {
        Format = value.Format, Tree = new() { Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion,
        Target = new() { RootId = value.Target.RootId, Path = value.Target.Path } } };
    private static App.FileTreeUnpackingReceipt UnpackingReceipt(FileTreeUnpackingOperation row, FileTreeUnpackingReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreeUnpackingReceipt(id, value.State, value.Fingerprint, value.Format, checked((int)value.EntryCount), value.TotalBytes,
            value.ArchiveBytes, value.ArchiveSha256, value.Version, value.PartialRetained);
        FileTreeUnpackingValidation.Receipt(UnpackingExecution(row), FileTreeUnpackingValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreeUnpackingRejectedException? UnpackingRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
