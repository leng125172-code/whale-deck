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

public sealed partial class AgentGateway : IContainerCopyGateway
{
    public async Task RequireContainerCopyWorkflowAsync(CancellationToken cancellationToken) => _ = await ContainerCopies(cancellationToken);
    private async Task<ContainerCopyService.ContainerCopyServiceClient> ContainerCopies(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 45 || !caps.Capabilities.Contains("docker.files.copy-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_COPY_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata ContainerCopyHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_COPY_CAPABILITY_UNAVAILABLE"));
        var hash = ContainerCopyBinding.Hash(request); var method = ContainerCopyBinding.Service + action;
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var mac = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerCopyBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(mac) }, { ContainerCopyBinding.Header, hash } };
    }
    public async Task<App.ContainerCopyPlanDto> PlanContainerCopyAsync(App.TerminalActor actor, App.ContainerCopyCommandDto command, CancellationToken cancellationToken)
    {
        ContainerCopyValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_COPY_CONTEXT_INVALID");
        var client = await ContainerCopies(cancellationToken);
        var request = new ContainerCopyPlanRequest { Command = ContainerCopyCommandInput(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var response = await client.PlanAsync(request, ContainerCopyHeaders("Plan", request), DateTime.UtcNow.AddSeconds(630), cancellationToken);
        var plan = ContainerCopyPlanOutput(response);
        if (plan.Command != command) throw ContainerCopyValidation.Invalid();
        return plan;
    }
    public Task<App.ContainerCopyReceiptDto> ApplyContainerCopyAsync(App.ContainerCopyOperationDto operation, CancellationToken cancellationToken) => ContainerCopyAction("Apply", operation, cancellationToken);
    public Task<App.ContainerCopyReceiptDto> DiscardContainerCopyAsync(App.ContainerCopyOperationDto operation, CancellationToken cancellationToken) => ContainerCopyAction("Discard", operation, cancellationToken);
    public async Task<App.ContainerCopyReceiptDto?> InspectContainerCopyAsync(App.ContainerCopyOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await ContainerCopyAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_COPY_NOT_FOUND") { return null; }
    }
    private async Task<App.ContainerCopyReceiptDto> ContainerCopyAction(string action, App.ContainerCopyOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerCopyValidation.Operation(operation); var client = await ContainerCopies(cancellationToken);
        var request = new ContainerCopyRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = ContainerCopyPlanInput(operation.Plan), Context = UploadContext(operation.Context) };
        var headers = ContainerCopyHeaders(action, request); var until = DateTime.UtcNow.AddSeconds(630);
        var value = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, until, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, until, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, until, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_COPY_ACTION_INVALID")
        };
        if (value.OperationId != request.OperationId) throw ContainerCopyValidation.Invalid();
        var receipt = new App.ContainerCopyReceiptDto(operation.Id, value.ContainerId, value.InstanceId, value.Kind, value.Fingerprint,
            value.TargetProof, value.State, value.Version, value.BindingCurrent, value.PrivateMayExist, value.Replayed);
        ContainerCopyValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerCopyCommand ContainerCopyCommandInput(App.ContainerCopyCommandDto command) => new()
        { Source = ContainerLocation(command.Source), TargetPath = command.TargetPath, ExpectedVersion = command.ExpectedVersion };
    private static FileObjectIdentity CopyObject(App.FileObjectIdentityDto value) => new() { Device = value.Device, Inode = value.Inode, Mode = value.Mode,
        OwnerUid = value.OwnerUid, OwnerGid = value.OwnerGid, LinkCount = value.LinkCount, SizeBytes = value.SizeBytes, ModifiedAtUnixNs = value.ModifiedAtUnixNs };
    private static App.FileObjectIdentityDto CopyObject(FileObjectIdentity value) => new(value.Device, value.Inode, value.Mode,
        value.OwnerUid, value.OwnerGid, value.LinkCount, value.SizeBytes, value.ModifiedAtUnixNs);
    private static ContainerCopyPlan ContainerCopyPlanInput(App.ContainerCopyPlanDto plan)
    {
        ContainerCopyValidation.Plan(plan);
        var result = new ContainerCopyPlan { SchemaVersion = plan.SchemaVersion, Command = ContainerCopyCommandInput(plan.Command), Kind = plan.Kind,
            TargetProof = plan.TargetProof, SourceIdentity = CopyObject(plan.SourceIdentity), SourceChangedAtUnixNs = plan.SourceChangedAtUnixNs,
            SourceParent = Parent(plan.SourceParent), TargetParent = Parent(plan.TargetParent), TargetParentVersion = plan.TargetParentVersion,
            OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint };
        if (plan.FilePlan is { } file) result.FilePlan = new() { Command = MutationCommand(file.Command), SourceIdentity = CopyObject(file.SourceIdentity!),
            SourceParent = Parent(file.SourceParent), TargetParent = Parent(file.TargetParent), SizeBytes = file.SizeBytes, Sha256 = file.Sha256, Fingerprint = file.Fingerprint };
        else if (plan.TreePlan is { } tree) result.TreePlan = new() { Command = TreeCommand(tree.Command), ManifestSha256 = tree.ManifestSha256,
            EntryCount = (uint)tree.EntryCount, TotalBytes = tree.TotalBytes, TargetParent = Parent(tree.TargetParent),
            SourcePolicyHash = tree.SourcePolicyHash, TargetPolicyHash = tree.TargetPolicyHash, Fingerprint = tree.Fingerprint };
        return result;
    }
    private static App.ContainerCopyPlanDto ContainerCopyPlanOutput(ContainerCopyPlan value)
    {
        if (value.Command?.Source is not { } source || value.SourceIdentity is null || value.SourceParent is null || value.TargetParent is null)
            throw ContainerCopyValidation.Invalid();
        App.FileMutationPlan? file = null; App.FileTreeCopyPlan? tree = null;
        if (value.InnerPlanCase == ContainerCopyPlan.InnerPlanOneofCase.FilePlan)
        {
            var inner = value.FilePlan;
            if (inner.Command?.Source is null || inner.Command.Target is null || inner.Command.Kind != FileOperationKind.CopyFile || inner.SourceIdentity is null)
                throw ContainerCopyValidation.Invalid();
            file = new(new("copy", new(inner.Command.Source.RootId, inner.Command.Source.Path), new(inner.Command.Target.RootId, inner.Command.Target.Path), inner.Command.ExpectedVersion, inner.Command.Mode),
                CopyObject(inner.SourceIdentity), Parent(inner.SourceParent), Parent(inner.TargetParent), inner.SizeBytes, inner.Sha256, inner.Fingerprint);
        }
        else if (value.InnerPlanCase == ContainerCopyPlan.InnerPlanOneofCase.TreePlan)
        {
            var inner = value.TreePlan;
            if (inner.Command?.Source is null || inner.Command.Target is null || inner.TargetParent is null || inner.EntryCount > int.MaxValue) throw ContainerCopyValidation.Invalid();
            tree = new(new(new(inner.Command.Source.RootId, inner.Command.Source.Path), inner.Command.ExpectedVersion, new(inner.Command.Target.RootId, inner.Command.Target.Path)),
                inner.ManifestSha256, (int)inner.EntryCount, inner.TotalBytes, new(inner.TargetParent.Device, inner.TargetParent.Inode), inner.SourcePolicyHash, inner.TargetPolicyHash, inner.Fingerprint);
        }
        var plan = new App.ContainerCopyPlanDto(value.SchemaVersion, new(new(source.ContainerId, source.InstanceId, source.Path), value.Command.TargetPath, value.Command.ExpectedVersion),
            value.Kind, value.TargetProof, CopyObject(value.SourceIdentity), value.SourceChangedAtUnixNs,
            new(value.SourceParent.Device, value.SourceParent.Inode), new(value.TargetParent.Device, value.TargetParent.Inode), value.TargetParentVersion,
            value.OwnerUid, value.OwnerGid, value.Fingerprint, file, tree);
        try { ContainerCopyValidation.Plan(plan); } catch (ArgumentException) { throw ContainerCopyValidation.Invalid(); }
        return plan;
    }
}
