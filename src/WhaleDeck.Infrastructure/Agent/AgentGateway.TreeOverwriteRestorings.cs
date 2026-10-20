using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using TreeRpc = WhaleDeck.Contracts.Agent.V1.FileTreeOverwriteRestoringService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeOverwriteRestoringGateway
{
    private const string OverwriteRestoringMethod = "/whaledeck.agent.v1.FileTreeOverwriteRestoringService/";
    private async Task<TreeRpc.FileTreeOverwriteRestoringServiceClient> OverwriteRestoringClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 34 || !capabilities.Capabilities.Contains("files.tree-overwrite-restore-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeOverwriteRestoringPlan> PlanTreeOverwriteRestoringAsync(App.TerminalActor actor, App.FileTreeOverwriteRestoringCommand command, CancellationToken cancellationToken)
    {
        FileTreeOverwriteRestoringValidation.Command(command); var client = await OverwriteRestoringClient(cancellationToken);
        try
        {
            var value = await client.PlanAsync(new() { Command = OverwriteRestoringCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(OverwriteRestoringMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return OverwriteRestoringPlan(value, command);
        }
        catch (RpcException error) when (OverwriteRestoringRejection(error) is { }) { throw OverwriteRestoringRejection(error)!; }
    }
    private static App.FileTreeOverwriteRestoringPlan OverwriteRestoringPlan(FileTreeOverwriteRestoringPlan value, App.FileTreeOverwriteRestoringCommand expected)
    {
        FileTreeOverwriteRestoringValidation.Command(expected);
        if (value.Command?.Source is null || value.Command.Target is null || value.TargetParent is null || value.TargetIdentity is null ||
            !Guid.TryParseExact(value.Command.CaptureId, "N", out var capture) || capture.ToString("N") != value.Command.CaptureId) throw new App.FileTransferRejectedException();
        var tree = value.Command;
        var plan = new App.FileTreeOverwriteRestoringPlan(new(new(tree.Source.RootId, tree.Source.Path), tree.ExpectedVersion,
            new(tree.Target.RootId, tree.Target.Path), "tar.gz", capture, tree.CaptureOwner, tree.CaptureKind, tree.ManifestSha256, tree.TargetExpectedVersion), value.ArchiveSha256, value.ArchiveBytes, value.ContentSha256,
            checked((int)value.EntryCount), value.TotalBytes, new(value.TargetParent.Device, value.TargetParent.Inode), value.SourcePolicyHash, value.TargetPolicyHash, value.Fingerprint,
            value.TargetManifestSha256, value.TargetTreeSha256, checked((int)value.TargetEntryCount), value.TargetTotalBytes,
            new(value.TargetIdentity.Device, value.TargetIdentity.Inode));
        FileTreeOverwriteRestoringValidation.Plan(plan); if (plan.Command != expected) throw new App.FileTransferRejectedException(); return plan;
    }
    public async Task<App.FileTreeOverwriteRestoringReceipt?> InspectTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken)
    {
        var client = await OverwriteRestoringClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = OverwriteRestoringExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(OverwriteRestoringMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return OverwriteRestoringReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeOverwriteRestoringReceipt> ApplyTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken) => OverwriteRestoringAction(operation, false, cancellationToken);
    public Task<App.FileTreeOverwriteRestoringReceipt> DiscardTreeOverwriteRestoringAsync(FileTreeOverwriteRestoringOperation operation, CancellationToken cancellationToken) => OverwriteRestoringAction(operation, true, cancellationToken);
    private async Task<App.FileTreeOverwriteRestoringReceipt> OverwriteRestoringAction(FileTreeOverwriteRestoringOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? recoveryActor = null)
    {
        var client = await OverwriteRestoringClient(cancellationToken); var plan = FileTreeOverwriteRestoringValidation.ReadPlan(operation); FileTreeOverwriteRestoringValidation.Plan(plan);
        var request = new FileTreeOverwriteRestoringApplyRequest { OperationId = OverwriteRestoringExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = OverwriteRestoringCommand(plan.Command), ArchiveSha256 = plan.ArchiveSha256, ArchiveBytes = plan.ArchiveBytes, ContentSha256 = plan.ContentSha256,
                EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes, TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode },
                SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint,
                TargetManifestSha256 = plan.TargetManifestSha256, TargetTreeSha256 = plan.TargetTreeSha256, TargetEntryCount = (uint)plan.TargetEntryCount,
                TargetTotalBytes = plan.TargetTotalBytes, TargetIdentity = new() { Device = plan.TargetIdentity.Device, Inode = plan.TargetIdentity.Inode } } };
        var value = discard ? await client.DiscardAsync(request, Headers(OverwriteRestoringMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(OverwriteRestoringMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return OverwriteRestoringReceipt(operation, value);
    }
    private static Guid OverwriteRestoringExecution(FileTreeOverwriteRestoringOperation row) => row.ExecutionJobId is { } id && id == FileTreeOverwriteRestoringValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static FileTreeOverwriteRestoringCommand OverwriteRestoringCommand(App.FileTreeOverwriteRestoringCommand value) => new() {
        Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion,
        Target = new() { RootId = value.Target.RootId, Path = value.Target.Path }, CaptureId = value.CaptureId.ToString("N"),
        CaptureOwner = value.CaptureOwner, CaptureKind = value.CaptureKind, ManifestSha256 = value.ManifestSha256, TargetExpectedVersion = value.TargetExpectedVersion };
    private static App.FileTreeOverwriteRestoringReceipt OverwriteRestoringReceipt(FileTreeOverwriteRestoringOperation row, FileTreeOverwriteRestoringReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId ||
            !Guid.TryParseExact(value.CaptureId, "N", out var capture) || capture.ToString("N") != value.CaptureId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreeOverwriteRestoringReceipt(id, value.State, value.Fingerprint, value.Format, checked((int)value.EntryCount), value.TotalBytes,
            value.ArchiveBytes, value.ArchiveSha256, value.Version, value.PartialRetained, value.Kind, capture, OverwrittenCapture(value.OverwrittenCaptureId), value.BackupMayBeRetained, value.BackupVerified,
            value.OriginalsRetained, value.TargetMayHaveChanged, value.BackupArchiveBytes, value.BackupArchiveSha256);
        FileTreeOverwriteRestoringValidation.Receipt(OverwriteRestoringExecution(row), FileTreeOverwriteRestoringValidation.ReadPlan(row), result); return result;
    }
    private static Guid? OverwrittenCapture(string value)
    {
        if (value.Length == 0) return null;
        if (!Guid.TryParseExact(value, "N", out var id) || id == Guid.Empty || id.ToString("N") != value) throw new App.FileTransferRejectedException();
        return id;
    }
    private static App.FileTreeOverwriteRestoringRejectedException? OverwriteRestoringRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
