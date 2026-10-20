using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using TreeRpc = WhaleDeck.Contracts.Agent.V1.FileTreeRestoringService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeRestoringGateway
{
    private const string RestoringMethod = "/whaledeck.agent.v1.FileTreeRestoringService/";
    private async Task<TreeRpc.FileTreeRestoringServiceClient> RestoringClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 33 || !capabilities.Capabilities.Contains("files.tree-restore-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeRestoringPlan> PlanTreeRestoringAsync(App.TerminalActor actor, App.FileTreeRestoringCommand command, CancellationToken cancellationToken)
    {
        var client = await RestoringClient(cancellationToken); FileTreeRestoringValidation.Command(command);
        try
        {
            var value = await client.PlanAsync(new() { Command = RestoringCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(RestoringMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            if (value.Command?.Source is null || value.Command.Target is null || value.TargetParent is null ||
                !Guid.TryParseExact(value.Command.CaptureId, "N", out var capture) || capture.ToString("N") != value.Command.CaptureId) throw new App.FileTransferRejectedException();
            var tree = value.Command;
            var plan = new App.FileTreeRestoringPlan(new(new(tree.Source.RootId, tree.Source.Path), tree.ExpectedVersion,
                new(tree.Target.RootId, tree.Target.Path), "tar.gz", capture, tree.CaptureOwner, tree.CaptureKind, tree.ManifestSha256), value.ArchiveSha256, value.ArchiveBytes, value.ContentSha256,
                checked((int)value.EntryCount), value.TotalBytes, new(value.TargetParent.Device, value.TargetParent.Inode), value.SourcePolicyHash, value.TargetPolicyHash, value.Fingerprint);
            FileTreeRestoringValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (RestoringRejection(error) is { }) { throw RestoringRejection(error)!; }
    }
    public async Task<App.FileTreeRestoringReceipt?> InspectTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken)
    {
        var client = await RestoringClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = RestoringExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(RestoringMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return RestoringReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeRestoringReceipt> ApplyTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => RestoringAction(operation, false, cancellationToken);
    public Task<App.FileTreeRestoringReceipt> DiscardTreeRestoringAsync(FileTreeRestoringOperation operation, CancellationToken cancellationToken) => RestoringAction(operation, true, cancellationToken);
    private async Task<App.FileTreeRestoringReceipt> RestoringAction(FileTreeRestoringOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? recoveryActor = null)
    {
        var client = await RestoringClient(cancellationToken); var plan = FileTreeRestoringValidation.ReadPlan(operation); FileTreeRestoringValidation.Plan(plan);
        var request = new FileTreeRestoringApplyRequest { OperationId = RestoringExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = RestoringCommand(plan.Command), ArchiveSha256 = plan.ArchiveSha256, ArchiveBytes = plan.ArchiveBytes, ContentSha256 = plan.ContentSha256,
                EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes, TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode },
                SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(RestoringMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(RestoringMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return RestoringReceipt(operation, value);
    }
    private static Guid RestoringExecution(FileTreeRestoringOperation row) => row.ExecutionJobId is { } id && id == FileTreeRestoringValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static FileTreeRestoringCommand RestoringCommand(App.FileTreeRestoringCommand value) => new() {
        Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion,
        Target = new() { RootId = value.Target.RootId, Path = value.Target.Path }, CaptureId = value.CaptureId.ToString("N"),
        CaptureOwner = value.CaptureOwner, CaptureKind = value.CaptureKind, ManifestSha256 = value.ManifestSha256 };
    private static App.FileTreeRestoringReceipt RestoringReceipt(FileTreeRestoringOperation row, FileTreeRestoringReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId ||
            !Guid.TryParseExact(value.CaptureId, "N", out var capture) || capture.ToString("N") != value.CaptureId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreeRestoringReceipt(id, value.State, value.Fingerprint, value.Format, checked((int)value.EntryCount), value.TotalBytes,
            value.ArchiveBytes, value.ArchiveSha256, value.Version, value.PartialRetained, value.Kind, capture);
        FileTreeRestoringValidation.Receipt(RestoringExecution(row), FileTreeRestoringValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreeRestoringRejectedException? RestoringRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
