using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class AgentGateway : IContainerRecursiveAclRestoreGateway
{
    public async Task RequireContainerRecursiveAclRestoreWorkflowAsync(CancellationToken cancellationToken) => _ = await RecursiveAclRestoreClient(cancellationToken);
    private async Task<ContainerRecursiveAclRestoreService.ContainerRecursiveAclRestoreServiceClient> RecursiveAclRestoreClient(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var caps = await GetCapabilitiesAsync(timeout.Token);
        if (caps.ProtocolMajor != 1 || caps.ProtocolMinor < 55 || !caps.Capabilities.Contains("docker.files.recursive-acl-restore-intents"))
            throw new RpcException(new(StatusCode.Unimplemented, "CONTAINER_RECURSIVE_ACL_RESTORE_CAPABILITY_UNAVAILABLE"));
        return new(_channel);
    }
    private Metadata RecursiveAclRestoreHeaders(string action, IMessage request)
    {
        if (_capabilityKey is not { Length: >= 32 }) throw new RpcException(new(StatusCode.PermissionDenied, "CONTAINER_RECURSIVE_ACL_RESTORE_CAPABILITY_UNAVAILABLE"));
        var method = ContainerRecursiveAclRestoreBinding.Service + action; var hash = ContainerRecursiveAclRestoreBinding.Hash(request);
        var expires = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture); var nonce = Guid.NewGuid().ToString("N");
        var signature = HMACSHA256.HashData(_capabilityKey, Encoding.UTF8.GetBytes(ContainerRecursiveAclRestoreBinding.Payload(expires, nonce, method, hash)));
        return new() { { "x-whaledeck-capability", expires + "." + nonce + "." + Convert.ToHexString(signature) }, { ContainerRecursiveAclRestoreBinding.Header, hash } };
    }
    public async Task<ContainerRecursiveAclRestorePlanDto> PlanContainerRecursiveAclRestoreAsync(TerminalActor actor, ContainerRecursiveAclRestoreCommandDto command, CancellationToken cancellationToken)
    {
        ContainerRecursiveAclRestoreValidation.Command(command);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(actor.TraceId) || actor.TraceId.Length > 128 || actor.TraceId.Any(char.IsControl)) throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_CONTEXT_INVALID");
        var request = new ContainerRecursiveAclRestorePlanRequest { Command = RecursiveAclRestoreCommand(command), Context = new() { ActorSubject = actor.Subject, TraceId = actor.TraceId } };
        var client = await RecursiveAclRestoreClient(cancellationToken);
        var response = await client.PlanAsync(request, RecursiveAclRestoreHeaders("Plan", request), deadline: DateTime.UtcNow.AddSeconds(330), cancellationToken: cancellationToken);
        var plan = RecursiveAclRestorePlan(response);
        if (!ContainerRecursiveAclRestoreValidation.SameCommand(plan.Command, command)) throw ContainerRecursiveAclRestoreValidation.Invalid();
        return plan;
    }
    public Task<ContainerRecursiveAclRestoreReceiptDto> ApplyContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken) => RecursiveAclRestoreAction("Apply", operation, cancellationToken);
    public Task<ContainerRecursiveAclRestoreReceiptDto> DiscardContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken) => RecursiveAclRestoreAction("Discard", operation, cancellationToken);
    public async Task<ContainerRecursiveAclRestoreReceiptDto?> InspectContainerRecursiveAclRestoreAsync(ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken)
    {
        try { return await RecursiveAclRestoreAction("Inspect", operation, cancellationToken); }
        catch (RpcException error) when (error.StatusCode == StatusCode.NotFound && error.Status.Detail == "CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND") { return null; }
    }
    private async Task<ContainerRecursiveAclRestoreReceiptDto> RecursiveAclRestoreAction(string action, ContainerRecursiveAclRestoreOperationDto operation, CancellationToken cancellationToken)
    {
        ContainerRecursiveAclRestoreValidation.Operation(operation);
        var request = new ContainerRecursiveAclRestoreRequest { OperationId = operation.Id.ToString("N"), Owner = operation.Owner,
            Plan = RecursiveAclRestorePlan(operation.Plan), Context = UploadContext(operation.Context) };
        var client = await RecursiveAclRestoreClient(cancellationToken);
        var headers = RecursiveAclRestoreHeaders(action, request); var deadline = DateTime.UtcNow.AddSeconds(330);
        var response = action switch
        {
            "Apply" => await client.ApplyAsync(request, headers, deadline, cancellationToken),
            "Inspect" => await client.InspectAsync(request, headers, deadline, cancellationToken),
            "Discard" => await client.DiscardAsync(request, headers, deadline, cancellationToken),
            _ => throw new ArgumentException("CONTAINER_RECURSIVE_ACL_RESTORE_ACTION_INVALID")
        };
        if (response.OperationId != request.OperationId) throw ContainerRecursiveAclRestoreValidation.Invalid();
        var receipt = new ContainerRecursiveAclRestoreReceiptDto(operation.Id, response.ContainerId, response.InstanceId, response.Fingerprint,
            response.TargetProof, response.State, response.BindingCurrent, response.Replayed, response.CompletedCount, response.ChangedCount,
            response.AbortedCount, response.NotStartedCount, response.UnknownCount, response.HistoryMayExist, response.HistoryVerified,
            response.HistoryDigest, response.HistoryAvailable, response.OutcomeDigest, response.AclType);
        ContainerRecursiveAclRestoreValidation.Receipt(operation, receipt); return receipt;
    }
    private static ContainerRecursiveAclRestoreCommand RecursiveAclRestoreCommand(ContainerRecursiveAclRestoreCommandDto command) => new()
    { Location = ContainerLocation(command.Location), CaptureId = command.CaptureId, ExpectedHistoryDigest = command.ExpectedHistoryDigest, ExpectedVersion = command.ExpectedVersion };
    private static ContainerRecursiveAclRestorePlan RecursiveAclRestorePlan(ContainerRecursiveAclRestorePlanDto plan) => new()
    {
        SchemaVersion = plan.SchemaVersion, Command = RecursiveAclRestoreCommand(plan.Command), TargetProof = plan.TargetProof,
        ParentIdentity = new() { Device = plan.ParentIdentity.Device, Inode = plan.ParentIdentity.Inode }, ParentVersion = plan.ParentVersion,
        OwnerUid = plan.OwnerUid, OwnerGid = plan.OwnerGid, Fingerprint = plan.Fingerprint,
        RootIdentity = new() { Device = plan.RootIdentity.Device, Inode = plan.RootIdentity.Inode, Mode = plan.RootIdentity.Mode,
            OwnerUid = plan.RootIdentity.OwnerUid, OwnerGid = plan.RootIdentity.OwnerGid, LinkCount = plan.RootIdentity.LinkCount,
            SizeBytes = plan.RootIdentity.SizeBytes, ModifiedAtUnixNs = plan.RootIdentity.ModifiedAtUnixNs },
        RootChangedAtUnixNs = plan.RootChangedAtUnixNs, ManifestDigest = plan.ManifestDigest, EntryCount = plan.EntryCount, FileCount = plan.FileCount,
        DirectoryCount = plan.DirectoryCount, ChangeCount = plan.ChangeCount,
        RestoreSource = new() { CaptureId = plan.RestoreSource.CaptureId, HistoryDigest = plan.RestoreSource.HistoryDigest, CreatedAtUnixMs = plan.RestoreSource.CreatedAtUnixMs,
            SourceInstanceId = plan.RestoreSource.SourceInstanceId, ChangedAclType = plan.RestoreSource.ChangedAclType, ManifestDigest = plan.RestoreSource.ManifestDigest,
            EntryCount = plan.RestoreSource.EntryCount, FileCount = plan.RestoreSource.FileCount, DirectoryCount = plan.RestoreSource.DirectoryCount, ChangeCount = plan.RestoreSource.ChangeCount },
        RestoreSourceFrame = new() { Device = plan.RestoreSourceFrame.Device, Inode = plan.RestoreSourceFrame.Inode, Mode = plan.RestoreSourceFrame.Mode,
            OwnerUid = plan.RestoreSourceFrame.OwnerUid, OwnerGid = plan.RestoreSourceFrame.OwnerGid, LinkCount = plan.RestoreSourceFrame.LinkCount,
            SizeBytes = plan.RestoreSourceFrame.SizeBytes, ModifiedAtUnixNs = plan.RestoreSourceFrame.ModifiedAtUnixNs, ChangedAtUnixNs = plan.RestoreSourceFrame.ChangedAtUnixNs },
        RestoreTargetsDigest = plan.RestoreTargetsDigest
    };
    private static ContainerRecursiveAclRestorePlanDto RecursiveAclRestorePlan(ContainerRecursiveAclRestorePlan response)
    {
        if (response.Command?.Location is not { } location || response.ParentIdentity is null || response.RootIdentity is null || response.RestoreSource is null ||
            response.RestoreSourceFrame is not { HasDevice: true, HasInode: true, HasMode: true, HasOwnerUid: true, HasOwnerGid: true, HasLinkCount: true, HasSizeBytes: true, HasModifiedAtUnixNs: true, HasChangedAtUnixNs: true } frame)
            throw ContainerRecursiveAclRestoreValidation.Invalid();
        var plan = new ContainerRecursiveAclRestorePlanDto(response.SchemaVersion,
            new(new(location.ContainerId, location.InstanceId, location.Path), response.Command.CaptureId, response.Command.ExpectedHistoryDigest, response.Command.ExpectedVersion),
            response.TargetProof, new(response.ParentIdentity.Device, response.ParentIdentity.Inode), response.ParentVersion, response.OwnerUid, response.OwnerGid, response.Fingerprint,
            new(response.RootIdentity.Device, response.RootIdentity.Inode, response.RootIdentity.Mode, response.RootIdentity.OwnerUid,
                response.RootIdentity.OwnerGid, response.RootIdentity.LinkCount, response.RootIdentity.SizeBytes, response.RootIdentity.ModifiedAtUnixNs),
            response.RootChangedAtUnixNs, response.ManifestDigest, response.EntryCount, response.FileCount, response.DirectoryCount, response.ChangeCount,
            RecursiveAclHistoryEntry(response.RestoreSource), new(frame.Device, frame.Inode, frame.Mode, frame.OwnerUid, frame.OwnerGid, frame.LinkCount, frame.SizeBytes, frame.ModifiedAtUnixNs, frame.ChangedAtUnixNs), response.RestoreTargetsDigest);
        try { ContainerRecursiveAclRestoreValidation.Plan(plan); } catch (ArgumentException) { throw ContainerRecursiveAclRestoreValidation.Invalid(); }
        return plan;
    }
}
