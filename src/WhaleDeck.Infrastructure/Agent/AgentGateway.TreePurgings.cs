using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using PurgeRpc = WhaleDeck.Contracts.Agent.V1.FileTreePurgingService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreePurgingGateway
{
    private const string PurgingMethod = "/whaledeck.agent.v1.FileTreePurgingService/";
    private async Task<PurgeRpc.FileTreePurgingServiceClient> PurgingClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 36 || !capabilities.Capabilities.Contains("files.tree-purge-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreePurgingPlan> PlanTreePurgingAsync(App.TerminalActor actor, App.FileTreePurgingCommand command, CancellationToken cancellationToken)
    {
        FileTreePurgingValidation.Command(command); var client = await PurgingClient(cancellationToken);
        try
        {
            var plan = await client.PlanAsync(new() { Command = PurgingCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(PurgingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return PurgingPlan(plan, command);
        }
        catch (RpcException error) when (PurgingRejection(error) is { }) { throw PurgingRejection(error)!; }
    }
    private static App.FileTreePurgingPlan PurgingPlan(FileTreePurgingPlan plan, App.FileTreePurgingCommand expected)
    {
        if (plan.Command is null || !plan.Command.Equals(PurgingCommand(expected)) ||
            plan.StoreIdentity is null || plan.ArchiveIdentity is null) throw new App.FileTransferRejectedException();
        var result = new App.FileTreePurgingPlan(expected, plan.SourcePolicyHash, PurgingIdentity(plan.SourceParent), plan.StorageHash,
            PurgingIdentity(plan.StoreIdentity)!, plan.CaptureMetadataHash, plan.OriginReceiptHash, plan.ManifestFileSha256,
            PurgingIdentity(plan.ArchiveIdentity)!, plan.ArchiveBytes, plan.ArchiveSha256, plan.ContentSha256, checked((int)plan.EntryCount), plan.TotalBytes,
            PurgingIdentity(plan.RetainedIdentity), PurgingIdentity(plan.StagingIdentity), plan.HasRetainedTreeSha256 ? plan.RetainedTreeSha256 : null, plan.Fingerprint);
        FileTreePurgingValidation.Plan(result); return result;
    }
    private static App.FileParentIdentity? PurgingIdentity(FileDirectoryIdentity? identity) => identity is null ? null : new(identity.Device, identity.Inode);
    public async Task<App.FileTreePurgingReceipt?> InspectTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken)
    {
        var id = PurgingExecution(operation); var client = await PurgingClient(cancellationToken);
        try
        {
            var receipt = await client.InspectAsync(new() { OperationId = id.ToString("N"), Owner = operation.OwnerHash }, Headers(PurgingMethod + "Inspect"),
                deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return PurgingReceipt(operation, receipt);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreePurgingReceipt> ApplyTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken) => PurgingAction(operation, false, cancellationToken);
    public Task<App.FileTreePurgingReceipt> DiscardTreePurgingAsync(FileTreePurgingOperation operation, CancellationToken cancellationToken) => PurgingAction(operation, true, cancellationToken);
    private async Task<App.FileTreePurgingReceipt> PurgingAction(FileTreePurgingOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? currentActor = null)
    {
        var id = PurgingExecution(operation); var plan = FileTreePurgingValidation.ReadPlan(operation); FileTreePurgingValidation.Plan(plan);
        var client = await PurgingClient(cancellationToken);
        var request = new FileTreePurgingApplyRequest { OperationId = id.ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = currentActor?.Subject ?? operation.ActorSubject, TraceId = currentActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = PurgingCommand(plan.Command), SourcePolicyHash = plan.SourcePolicyHash, SourceParent = PurgingIdentity(plan.SourceParent),
                StorageHash = plan.StorageHash, StoreIdentity = PurgingIdentity(plan.StoreIdentity), CaptureMetadataHash = plan.CaptureMetadataHash,
                OriginReceiptHash = plan.OriginReceiptHash, ManifestFileSha256 = plan.ManifestFileSha256, ArchiveIdentity = PurgingIdentity(plan.ArchiveIdentity),
                ArchiveBytes = plan.ArchiveBytes, ArchiveSha256 = plan.ArchiveSha256, ContentSha256 = plan.ContentSha256, EntryCount = checked((uint)plan.EntryCount),
                TotalBytes = plan.TotalBytes, RetainedIdentity = PurgingIdentity(plan.RetainedIdentity), StagingIdentity = PurgingIdentity(plan.StagingIdentity), Fingerprint = plan.Fingerprint } };
        if (plan.RetainedTreeSha256 is not null) request.Plan.RetainedTreeSha256 = plan.RetainedTreeSha256;
        var result = discard ? await client.DiscardAsync(request, Headers(PurgingMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(PurgingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return PurgingReceipt(operation, result);
    }
    private static FileDirectoryIdentity? PurgingIdentity(App.FileParentIdentity? identity) => identity is null ? null : new() { Device = identity.Device, Inode = identity.Inode };
    private static FileTreePurgingCommand PurgingCommand(App.FileTreePurgingCommand command) => new() {
        Source = new() { RootId = command.Source.RootId, Path = command.Source.Path }, ExpectedVersion = command.ExpectedVersion,
        CaptureId = command.CaptureId.ToString("N"), CaptureOwner = command.CaptureOwner, CaptureKind = command.CaptureKind, ManifestSha256 = command.ManifestSha256,
        Scope = command.Scope switch { "Archive" => FileTreePurgeScope.Archive, "Originals" => FileTreePurgeScope.Originals, "All" => FileTreePurgeScope.All,
            _ => throw new App.FileTransferRejectedException() } };
    private static Guid PurgingExecution(FileTreePurgingOperation row) => row.ExecutionJobId is { } id && id != row.CaptureId && id != row.Id &&
        id == FileTreePurgingValidation.ExecutionId(row.Id) && FileManagementService.Hex(row.OwnerHash)
        ? id : throw new App.FileTransferRejectedException();
    private static App.FileTreePurgingReceipt PurgingReceipt(FileTreePurgingOperation row, FileTreePurgingReceipt receipt)
    {
        if (!Guid.TryParseExact(receipt.OperationId, "N", out var id) || id.ToString("N") != receipt.OperationId ||
            !Guid.TryParseExact(receipt.CaptureId, "N", out var capture) || capture.ToString("N") != receipt.CaptureId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreePurgingReceipt(id, receipt.Kind, receipt.State, receipt.Fingerprint, capture,
            receipt.Scope switch { FileTreePurgeScope.Archive => "Archive", FileTreePurgeScope.Originals => "Originals", FileTreePurgeScope.All => "All", _ => throw new App.FileTransferRejectedException() },
            checked((int)receipt.EntryCount), receipt.TotalBytes, receipt.ArchiveBytes, receipt.DeletionStarted, receipt.ArchiveRemoved, receipt.OriginalsRemoved);
        FileTreePurgingValidation.Receipt(PurgingExecution(row), FileTreePurgingValidation.ReadPlan(row), result);
        if (row.ReceiptJson is not null) FileTreePurgingValidation.MonotonicReceipt(FileTreePurgingValidation.ReadReceipt(row), result);
        return result;
    }
    private static App.FileTreePurgingRejectedException? PurgingRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(value => !char.IsAsciiLetterUpper(value) && value != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
