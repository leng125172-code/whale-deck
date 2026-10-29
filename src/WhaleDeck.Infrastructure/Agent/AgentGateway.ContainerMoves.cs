using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using App = WhaleDeck.Application.Models;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerMoveGateway
{
    public async Task RequireContainerMoveWorkflowAsync(CancellationToken cancellationToken) => _ = await ContainerMoves(cancellationToken);
    private async Task<ContainerMoveService.ContainerMoveServiceClient> ContainerMoves(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 46 || !caps.Capabilities.Contains("docker.files.move-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_MOVE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata ContainerMoveHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_MOVE_CAPABILITY_UNAVAILABLE"));
        var hash = ContainerMoveBinding.Hash(request); var method = ContainerMoveBinding.Service + action;
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var mac = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerMoveBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(mac) }, { ContainerMoveBinding.Header, hash } };
    }
    public async Task<App.ContainerMovePlanDto> PlanContainerMoveAsync(App.TerminalActor actor, App.ContainerMoveCommandDto command, CancellationToken cancellationToken)
    {
        ContainerMoveValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_MOVE_CONTEXT_INVALID");
        var client = await ContainerMoves(cancellationToken);
        var request = new ContainerMovePlanRequest { Command = ContainerMoveCommandInput(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var response = await client.PlanAsync(request, ContainerMoveHeaders("Plan", request), DateTime.UtcNow.AddSeconds(630), cancellationToken);
        var plan = ContainerMovePlanOutput(response);
        if (plan.Command != command) throw ContainerMoveValidation.Invalid();
        return plan;
    }
    public Task<App.ContainerMoveReceiptDto> ApplyContainerMoveAsync(App.ContainerMoveOperationDto operation, CancellationToken cancellationToken) => ContainerMoveAction("Apply", operation, cancellationToken);
    public Task<App.ContainerMoveReceiptDto> DiscardContainerMoveAsync(App.ContainerMoveOperationDto operation, CancellationToken cancellationToken) => ContainerMoveAction("Discard", operation, cancellationToken);
    public async Task<App.ContainerMoveReceiptDto?> InspectContainerMoveAsync(App.ContainerMoveOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await ContainerMoveAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_MOVE_NOT_FOUND") { return null; }
    }
    private async Task<App.ContainerMoveReceiptDto> ContainerMoveAction(string action, App.ContainerMoveOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerMoveValidation.Operation(operation); var client = await ContainerMoves(cancellationToken);
        var request = new ContainerMoveRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = ContainerMovePlanInput(operation.Plan), Context = UploadContext(operation.Context) };
        var headers = ContainerMoveHeaders(action, request); var until = DateTime.UtcNow.AddSeconds(630);
        var value = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, until, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, until, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, until, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_MOVE_ACTION_INVALID")
        };
        if (value.OperationId != request.OperationId) throw ContainerMoveValidation.Invalid();
        var receipt = new App.ContainerMoveReceiptDto(operation.Id, value.ContainerId, value.InstanceId, value.OperationKind, value.Kind, value.Fingerprint,
            value.TargetProof, value.State, value.Version, value.BindingCurrent, value.PrivateMayExist, value.Replayed, value.HasDestinationPublished ? value.DestinationPublished : null,
            value.HasSourceRemoved ? value.SourceRemoved : null, value.HasSourceRetained ? value.SourceRetained : null);
        ContainerMoveValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerMoveCommand ContainerMoveCommandInput(App.ContainerMoveCommandDto command) => new()
        { Source = ContainerLocation(command.Source), TargetPath = command.TargetPath, ExpectedVersion = command.ExpectedVersion };
    private static FileObjectIdentity MoveObject(App.FileObjectIdentityDto value) => new() { Device = value.Device, Inode = value.Inode, Mode = value.Mode,
        OwnerUid = value.OwnerUid, OwnerGid = value.OwnerGid, LinkCount = value.LinkCount, SizeBytes = value.SizeBytes, ModifiedAtUnixNs = value.ModifiedAtUnixNs };
    private static App.FileObjectIdentityDto MoveObject(FileObjectIdentity value) => new(value.Device, value.Inode, value.Mode,
        value.OwnerUid, value.OwnerGid, value.LinkCount, value.SizeBytes, value.ModifiedAtUnixNs);
    private static ContainerMovePlan ContainerMovePlanInput(App.ContainerMovePlanDto plan)
    {
        ContainerMoveValidation.Plan(plan);
        var result = new ContainerMovePlan { SchemaVersion = plan.SchemaVersion, OperationKind = plan.OperationKind, Command = ContainerMoveCommandInput(plan.Command), Kind = plan.Kind,
            TargetProof = plan.TargetProof, SourceIdentity = MoveObject(plan.SourceIdentity), SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs,
            SourceParent = Parent(plan.SourceParent), TargetParent = Parent(plan.TargetParent), TargetParentVersion = plan.TargetParentVersion,
            OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint };
        if (plan.FilePlan is { } file) result.FilePlan = new() { Command = MutationCommand(file.Command), SourceIdentity = MoveObject(file.SourceIdentity!),
            SourceParent = Parent(file.SourceParent), TargetParent = Parent(file.TargetParent), SizeBytes = file.SizeBytes, Sha256 = file.Sha256, Fingerprint = file.Fingerprint };
        else if (plan.TreePlan is { } move)
        {
            var tree = move.CopyPlan;
            result.TreePlan = new() { SourceParent = Parent(move.SourceParent), Fingerprint = move.Fingerprint,
                CopyPlan = new() { Command = TreeCommand(tree.Command), ManifestSha256 = tree.ManifestSha256,
                    EntryCount = (uint)tree.EntryCount, TotalBytes = tree.TotalBytes, TargetParent = Parent(tree.TargetParent),
                    SourcePolicyHash = tree.SourcePolicyHash, TargetPolicyHash = tree.TargetPolicyHash, Fingerprint = tree.Fingerprint } };
        }
        return result;
    }
    private static App.ContainerMovePlanDto ContainerMovePlanOutput(ContainerMovePlan value)
    {
        if (value.Command?.Source is not { } source || value.SourceIdentity is null || value.SourceParent is null || value.TargetParent is null)
            throw ContainerMoveValidation.Invalid();
        App.FileMutationPlan? file = null; App.FileTreeMovePlan? tree = null;
        if (value.InnerPlanCase == ContainerMovePlan.InnerPlanOneofCase.FilePlan)
        {
            var inner = value.FilePlan;
            if (inner.Command?.Source is null || inner.Command.Target is null || inner.Command.Kind != FileOperationKind.MoveFile || inner.SourceIdentity is null)
                throw ContainerMoveValidation.Invalid();
            file = new(new("move", new(inner.Command.Source.RootId, inner.Command.Source.Path), new(inner.Command.Target.RootId, inner.Command.Target.Path), inner.Command.ExpectedVersion, inner.Command.Mode),
                MoveObject(inner.SourceIdentity), Parent(inner.SourceParent), Parent(inner.TargetParent), inner.SizeBytes, inner.Sha256, inner.Fingerprint);
        }
        else if (value.InnerPlanCase == ContainerMovePlan.InnerPlanOneofCase.TreePlan)
        {
            var move = value.TreePlan; var inner = move.CopyPlan;
            if (inner?.Command?.Source is null || inner.Command.Target is null || inner.TargetParent is null || move.SourceParent is null || inner.EntryCount > int.MaxValue) throw ContainerMoveValidation.Invalid();
            var copy = new App.FileTreeCopyPlan(new(new(inner.Command.Source.RootId, inner.Command.Source.Path), inner.Command.ExpectedVersion, new(inner.Command.Target.RootId, inner.Command.Target.Path)),
                inner.ManifestSha256, (int)inner.EntryCount, inner.TotalBytes, new(inner.TargetParent.Device, inner.TargetParent.Inode), inner.SourcePolicyHash, inner.TargetPolicyHash, inner.Fingerprint);
            tree = new(copy, new(move.SourceParent.Device, move.SourceParent.Inode), move.Fingerprint);
        }
        var plan = new App.ContainerMovePlanDto(value.SchemaVersion, new(new(source.ContainerId, source.InstanceId, source.Path), value.Command.TargetPath, value.Command.ExpectedVersion),
            value.OperationKind, value.Kind, value.TargetProof, MoveObject(value.SourceIdentity), value.SourceChangedAtUnixNs,
            new(value.SourceParent.Device, value.SourceParent.Inode), new(value.TargetParent.Device, value.TargetParent.Inode), value.TargetParentVersion,
            value.OwnerUid, value.OwnerGid, value.Fingerprint, file, tree);
        try { ContainerMoveValidation.Plan(plan); } catch (ArgumentException) { throw ContainerMoveValidation.Invalid(); }
        return plan;
    }
}
