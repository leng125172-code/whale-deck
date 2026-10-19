using Grpc.Core;
using System.Runtime.CompilerServices;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Domain.Entities;
using App = WhaleDeck.Application.Models;
using TreeRpc = WhaleDeck.Contracts.Agent.V1.FileTreePackingService;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IFileTreePackingGateway
{
    private const string PackingMethod = "/whaledeck.agent.v1.FileTreePackingService/";
    private async Task<TreeRpc.FileTreePackingServiceClient> PackingClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var capabilities = await GetCapabilitiesAsync(timeout.Token);
        if (capabilities.ProtocolMajor != 1 || capabilities.ProtocolMinor < 30 || !capabilities.Capabilities.Contains("files.tree-pack-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "FILES_TREE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    public async Task<App.FileTreePackingPlan> PlanTreePackingAsync(App.TerminalActor actor, App.FileTreePackingCommand command, CancellationToken cancellationToken)
    {
        var client = await PackingClient(cancellationToken); FileTreePackingValidation.Command(command);
        try
        {
            var value = await client.PlanAsync(new() { Command = PackingCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } },
                Headers(PackingMethod + "Plan"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            if (value.Command?.Tree?.Source is null || value.Command.Tree.Target is null || value.TargetParent is null) throw new App.FileTransferRejectedException();
            var tree = value.Command.Tree;
            var plan = new App.FileTreePackingPlan(new(new(tree.Source.RootId, tree.Source.Path), tree.ExpectedVersion,
                new(tree.Target.RootId, tree.Target.Path), value.Command.Format), value.ManifestSha256, checked((int)value.EntryCount), value.TotalBytes,
                new(value.TargetParent.Device, value.TargetParent.Inode), value.SourcePolicyHash, value.TargetPolicyHash, value.Fingerprint);
            FileTreePackingValidation.Plan(plan); if (plan.Command != command) throw new App.FileTransferRejectedException(); return plan;
        }
        catch (RpcException error) when (PackingRejection(error) is { }) { throw PackingRejection(error)!; }
    }
    public async Task<App.FileTreePackingReceipt?> InspectTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken)
    {
        var client = await PackingClient(cancellationToken);
        try
        {
            var value = await client.InspectAsync(new() { OperationId = PackingExecution(operation).ToString("N"), Owner = operation.OwnerHash },
                Headers(PackingMethod + "Inspect"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
            return PackingReceipt(operation, value);
        }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound) { return null; }
    }
    public Task<App.FileTreePackingReceipt> ApplyTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => PackingAction(operation, false, cancellationToken);
    public Task<App.FileTreePackingReceipt> DiscardTreePackingAsync(FileTreePackingOperation operation, CancellationToken cancellationToken) => PackingAction(operation, true, cancellationToken);
    private async Task<App.FileTreePackingReceipt> PackingAction(FileTreePackingOperation operation, bool discard, CancellationToken cancellationToken, App.TerminalActor? recoveryActor = null)
    {
        var client = await PackingClient(cancellationToken); var plan = FileTreePackingValidation.ReadPlan(operation); FileTreePackingValidation.Plan(plan);
        var request = new FileTreePackingApplyRequest { OperationId = PackingExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = recoveryActor?.Subject ?? operation.ActorSubject, TraceId = recoveryActor?.TraceId ?? operation.Id.ToString("D") }, Plan = new() {
                Command = PackingCommand(plan.Command), ManifestSha256 = plan.ManifestSha256, EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes,
                TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode }, SourcePolicyHash = plan.SourcePolicyHash,
                TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint } };
        var value = discard ? await client.DiscardAsync(request, Headers(PackingMethod + "Discard"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken)
            : await client.ApplyAsync(request, Headers(PackingMethod + "Apply"), deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        return PackingReceipt(operation, value);
    }
    private static Guid PackingExecution(FileTreePackingOperation row) => row.ExecutionJobId is { } id && id == FileTreePackingValidation.ExecutionId(row.Id)
        ? id : throw new App.FileTransferRejectedException();
    private static FileTreePackingCommand PackingCommand(App.FileTreePackingCommand value) => new() {
        Format = value.Format, Tree = new() { Source = new() { RootId = value.Source.RootId, Path = value.Source.Path }, ExpectedVersion = value.ExpectedVersion,
        Target = new() { RootId = value.Target.RootId, Path = value.Target.Path } } };
    private static App.FileTreePackingReceipt PackingReceipt(FileTreePackingOperation row, FileTreePackingReceipt value)
    {
        if (!Guid.TryParseExact(value.OperationId, "N", out var id) || id.ToString("N") != value.OperationId) throw new App.FileTransferRejectedException();
        var result = new App.FileTreePackingReceipt(id, value.State, value.Fingerprint, value.Format, checked((int)value.EntryCount), value.TotalBytes,
            value.ArchiveBytes, value.ArchiveSha256, value.Version, value.PartialRetained);
        FileTreePackingValidation.Receipt(PackingExecution(row), FileTreePackingValidation.ReadPlan(row), result); return result;
    }
    private static App.FileTreePackingRejectedException? PackingRejection(RpcException error)
    {
        if (error.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.FailedPrecondition or StatusCode.Aborted or StatusCode.ResourceExhausted or StatusCode.NotFound)) return null;
        var code = error.Status.Detail;
        if (code.Length is < 7 or > 80 || !code.StartsWith("FILES_", StringComparison.Ordinal) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')) code = "FILES_TREE_PLAN_REJECTED";
        return new(code);
    }
    public async IAsyncEnumerable<App.FileDownloadPart> DownloadTreePackingAsync(FileTreePackingOperation operation, App.TerminalActor actor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await PackingClient(cancellationToken);
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (!capabilities.Capabilities.Contains("files.tree-pack-download")) throw new RpcException(new(StatusCode.Unimplemented, "FILES_PACK_DOWNLOAD_CAPABILITY_UNAVAILABLE"));
        using var call = client.Download(new() { OperationId = PackingExecution(operation).ToString("N"), Owner = operation.OwnerHash,
            Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } }, Headers(PackingMethod + "Download"),
            deadline: DateTime.UtcNow.AddMinutes(10), cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var item = call.ResponseStream.Current;
            yield return new(item.Kind, item.Metadata is null ? null : MapFile(item.Metadata), item.Data.Memory, item.Offset, item.Sha256, item.Version, item.SizeBytes);
        }
    }
}
