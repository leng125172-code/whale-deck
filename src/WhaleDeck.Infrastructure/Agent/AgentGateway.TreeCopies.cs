using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using TreeRpc = WhaleDeck.Contracts.Agent.V1.FileTreeCopyService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeCopyGateway
{
    private const string TreeMethod = "/whaledeck.agent.v1.FileTreeCopyService/";
    private async Task<TreeRpc.FileTreeCopyServiceClient> TreeClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 27 || !capabilities.Capabilities.Contains("files.tree-copy-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeCopyPlan> PlanTreeCopyAsync(App.TerminalActor actor, App.FileTreeCopyCommand command, CancellationToken cancellationToken)
    {
        var client = await TreeClient(cancellationToken); FileTreeCopyValidation.Command(command);
        try
        {
            var value = await client.PlanAsync(new() { Command = TreeCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(TreeMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            if (value.Command is null || value.TargetParent is null) throw new App.FileTransferRejectedException();
            var plan = new App.FileTreeCopyPlan(new(new(value.Command.Source.RootId, value.Command.Source.Path), value.Command.ExpectedVersion,
                new(value.Command.Target.RootId, value.Command.Target.Path)), value.ManifestSha256, checked((int)value.EntryCount), value.TotalBytes,
                new(value.TargetParent.Device, value.TargetParent.Inode), value.SourcePolicyHash, value.TargetPolicyHash, value.Fingerprint);
            FileTreeCopyValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (TreeRejection(error) is { }) { throw TreeRejection(error)!; }
    }
    public async Task<App.FileTreeCopyReceipt?> InspectTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken)
    {
        var client = await TreeClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = TreeExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(TreeMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return TreeReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeCopyReceipt> ApplyTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken) => TreeAction(operation, false, cancellationToken);
    public Task<App.FileTreeCopyReceipt> DiscardTreeCopyAsync(FileTreeCopyOperation operation, CancellationToken cancellationToken) => TreeAction(operation, true, cancellationToken);
    private async Task<App.FileTreeCopyReceipt> TreeAction(FileTreeCopyOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? recoveryActor = null)
    {
        var client = await TreeClient(cancellationToken); var plan = FileTreeCopyValidation.ReadPlan(operation); FileTreeCopyValidation.Plan(plan);
        var request = new FileTreeCopyApplyRequest { OperationId = TreeExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = TreeCommand(plan.Command), ManifestSha256 = plan.ManifestSha256, EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes,
                TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode }, SourcePolicyHash = plan.SourcePolicyHash,
                TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(TreeMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(TreeMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return TreeReceipt(operation, value);
    }
    private static Guid TreeExecution(FileTreeCopyOperation row) => row.ExecutionJobId is { } id && id == FileTreeCopyValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static FileTreeCopyCommand TreeCommand(App.FileTreeCopyCommand value) => new() {
        Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion,
        Target = new() { RootId = value.Target.RootId, Path = value.Target.Path } };
    private static App.FileTreeCopyReceipt TreeReceipt(FileTreeCopyOperation row, FileTreeCopyReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreeCopyReceipt(id, value.State, value.Fingerprint, checked((int)value.EntryCount), value.TotalBytes, value.Version, value.PartialRetained);
        FileTreeCopyValidation.Receipt(TreeExecution(row), FileTreeCopyValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreeCopyRejectedException? TreeRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
