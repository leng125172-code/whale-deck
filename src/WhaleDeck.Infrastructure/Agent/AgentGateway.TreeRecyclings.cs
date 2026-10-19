using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using RecycleRpc = WhaleDeck.Contracts.Agent.V1.FileTreeRecyclingService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreeRecyclingGateway
{
    private const string RecyclingMethod = "/whaledeck.agent.v1.FileTreeRecyclingService/";
    private async Task<RecycleRpc.FileTreeRecyclingServiceClient> RecyclingClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 32 || !capabilities.Capabilities.Contains("files.tree-trash-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreeRecyclingPlan> PlanTreeRecyclingAsync(App.TerminalActor actor, App.FileTreeRecyclingCommand command, CancellationToken cancellationToken)
    {
        FileTreeRecyclingValidation.Command(command); var client = await RecyclingClient(cancellationToken);
        try
        {
            var value = await client.PlanAsync(new() { Command = RecyclingCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(RecyclingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            if (value.Command?.Source is null || value.SourceParent is null) throw new App.FileTransferRejectedException();
            var plan = new App.FileTreeRecyclingPlan(new(new(value.Command.Source.RootId, value.Command.Source.Path), value.Command.ExpectedVersion),
                new(value.SourceParent.Device, value.SourceParent.Inode), value.SourcePolicyHash, value.ManifestSha256, checked((int)value.EntryCount), value.TotalBytes, value.Fingerprint);
            FileTreeRecyclingValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (RecyclingRejection(error) is { }) { throw RecyclingRejection(error)!; }
    }
    public async Task<App.FileTreeRecyclingReceipt?> InspectTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken)
    {
        var client = await RecyclingClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = RecyclingExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(RecyclingMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return RecyclingReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreeRecyclingReceipt> ApplyTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken) => RecyclingAction(operation, false, cancellationToken);
    public Task<App.FileTreeRecyclingReceipt> DiscardTreeRecyclingAsync(FileTreeRecyclingOperation operation, CancellationToken cancellationToken) => RecyclingAction(operation, true, cancellationToken);
    private async Task<App.FileTreeRecyclingReceipt> RecyclingAction(FileTreeRecyclingOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? recoveryActor = null)
    {
        var client = await RecyclingClient(cancellationToken); var plan = FileTreeRecyclingValidation.ReadPlan(operation); FileTreeRecyclingValidation.Plan(plan);
        var request = new FileTreeRecyclingApplyRequest { OperationId = RecyclingExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = RecyclingCommand(plan.Command), SourceParent = new() { Device = plan.SourceParent.Device, Inode = plan.SourceParent.Inode },
                SourcePolicyHash = plan.SourcePolicyHash, ManifestSha256 = plan.ManifestSha256, EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(RecyclingMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(RecyclingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return RecyclingReceipt(operation, value);
    }
    private static FileTreeRecyclingCommand RecyclingCommand(App.FileTreeRecyclingCommand value) => new() { Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion };
    private static Guid RecyclingExecution(FileTreeRecyclingOperation row) => row.ExecutionJobId is { } id && id == FileTreeRecyclingValidation.ExecutionId(row.Id) ? id : throw new App.FileTransferRejectedException();
    private static App.FileTreeRecyclingReceipt RecyclingReceipt(FileTreeRecyclingOperation row, FileTreeRecyclingReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId || !Guid.TryParseExact(value.CaptureId, "N", out var capture) || capture.ToString("N") != value.CaptureId)
            throw new App.FileTransferRejectedException();
        var result = new App.FileTreeRecyclingReceipt(id, value.State, value.Fingerprint, capture, value.ManifestSha256, checked((int)value.EntryCount), value.TotalBytes,
            value.CaptureVerified, value.CaptureMayBeRetained, value.ArchiveBytes, value.ArchiveSha256, value.SourceMayHaveChanged, value.SourceRemoved, value.OriginalsRetained);
        FileTreeRecyclingValidation.Receipt(RecyclingExecution(row), FileTreeRecyclingValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreeRecyclingRejectedException? RecyclingRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
}
